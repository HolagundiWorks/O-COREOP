// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aqc.CadImport;

/// <summary>One ShilpiDB record as the HTTP gateway returns it: id, bounding box (millimetres) and a UTF-8 text payload.</summary>
public sealed record ShilpiRecord(ulong Id, double MinX, double MinY, double MaxX, double MaxY, string? PayloadText);

/// <summary>
/// The take-off recorded in ShilpiDB by O-Draft (docs/TAKEOFF-SHILPIDB.md). Each measured item is one record whose id carries its kind
/// and whose payload is the same JSON object the hcw-aqc-bridge v1 file uses for that row, so the import rules, the idempotence
/// (source = hcwcad:&lt;drawing_id&gt;, source_ref = ref) and the preview are exactly the file importer's. One header record carries the
/// project, levels, beam depths and opening schedule.
/// </summary>
public static class ShilpiTakeoff
{
    public const string HeaderFormat = "hcw-takeoff-shilpi";
    public const int Version = 1;

    public const byte KindHeader = 1, KindWall = 3, KindOpening = 4, KindColumn = 5, KindLintel = 6, KindRoom = 7, KindSlab = 8;

    /// <summary>id = (kind &lt;&lt; 56) | n. n is the entity handle or a running number, stable across exports so a second export replaces the same records.</summary>
    public static ulong MakeId(byte kind, ulong n) => ((ulong)kind << 56) | (n & 0x00FF_FFFF_FFFF_FFFFUL);
    public static byte KindOf(ulong id) => (byte)(id >> 56);
    public static ulong HeaderId => MakeId(KindHeader, 0);

    static readonly Dictionary<byte, string> Sections = new()
    {
        [KindWall] = "walls", [KindOpening] = "openings", [KindColumn] = "columns",
        [KindLintel] = "lintels", [KindRoom] = "rooms", [KindSlab] = "slabs",
    };

    public sealed record Result(BridgeFile File, int Records, IReadOnlyList<string> Skipped, string BridgeJson);

    /// <summary>Builds the bridge file from the records. Throws <see cref="BridgeFormatException"/> when the header is missing or not version 1 in millimetres.</summary>
    public static Result Read(IEnumerable<ShilpiRecord> records)
    {
        var all = records.OrderBy(r => r.Id).ToList();
        var skipped = new List<string>();
        var headers = all.Where(r => KindOf(r.Id) == KindHeader).ToList();
        if (headers.Count == 0) throw new BridgeFormatException("This ShilpiDB has no take-off header record. Export the take-off from O-Draft first.");
        if (headers.Count > 1) throw new BridgeFormatException("This ShilpiDB has more than one take-off header; it holds more than one take-off. Use one database per drawing.");

        var header = ParseObject(headers[0], "header");
        if (Str(header, "format") != HeaderFormat)
            throw new BridgeFormatException($"The header is not a {HeaderFormat} record.");
        if (!(header["version"] is JsonValue v && v.TryGetValue<int>(out var ver) && ver == Version))
            throw new BridgeFormatException($"Unsupported take-off version {header["version"]} (this O-CoreOp reads version {Version}).");
        if (!string.Equals(Str(header, "units"), "mm", StringComparison.OrdinalIgnoreCase))
            throw new BridgeFormatException($"Unsupported units '{Str(header, "units")}' (expected mm).");

        var root = new JsonObject
        {
            ["format"] = "hcw-aqc-bridge",
            ["version"] = 1,
            ["units"] = "mm",
            ["source"] = header["source"]?.DeepClone() ?? new JsonObject(),
        };
        foreach (var key in new[] { "project", "levels", "beam_depths_mm", "opening_schedule" })
            if (header[key] is { } node) root[key] = node.DeepClone();

        var sections = Sections.Values.ToDictionary(s => s, _ => new JsonArray());
        int used = 0;
        foreach (var r in all)
        {
            var kind = KindOf(r.Id);
            if (kind == KindHeader) continue;
            if (!Sections.TryGetValue(kind, out var section))
            {
                skipped.Add($"record {r.Id}: unknown kind {kind}; ignored");
                continue;
            }
            JsonObject row;
            try { row = ParseObject(r, section); }
            catch (BridgeFormatException e) { skipped.Add(e.Message); continue; }
            sections[section].Add(row);
            used++;
        }
        foreach (var (name, arr) in sections)
            if (arr.Count > 0) root[name] = arr;

        var json = root.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() });
        return new Result(BridgeFile.Parse(json), used, skipped, json);
    }

    static JsonObject ParseObject(ShilpiRecord r, string what)
    {
        if (string.IsNullOrWhiteSpace(r.PayloadText))
            throw new BridgeFormatException($"record {r.Id} ({what}): the payload is not text");
        try { return JsonNode.Parse(r.PayloadText) as JsonObject ?? throw new BridgeFormatException($"record {r.Id} ({what}): the payload is not a JSON object"); }
        catch (JsonException e) { throw new BridgeFormatException($"record {r.Id} ({what}): invalid JSON ({e.Message})"); }
    }

    static string Str(JsonObject o, string key) => o[key] is JsonValue v ? v.ToString() : "";
}

/// <summary>Reads every record of a ShilpiDB through the shilpi-http gateway (GET /records, then GET /records/{id}).</summary>
public sealed class ShilpiGatewayClient : IDisposable
{
    readonly HttpClient _http;
    readonly bool _ownsHttp;

    public ShilpiGatewayClient(Uri baseUri, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ownsHttp = true;
        _http.BaseAddress = baseUri;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<bool> IsUpAsync(CancellationToken ct = default)
    {
        try { using var r = await _http.GetAsync("health", ct).ConfigureAwait(false); return r.IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    /// <param name="progress">Called with (done, total) as records arrive.</param>
    public async Task<List<ShilpiRecord>> ReadAllAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var ids = new List<ulong>();
        using (var res = await _http.GetAsync("records", ct).ConfigureAwait(false))
        {
            await EnsureOk(res).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            foreach (var e in doc.RootElement.GetProperty("records").EnumerateArray())
                ids.Add(e.GetProperty("id").GetUInt64());
        }

        var results = new ShilpiRecord?[ids.Count];
        int done = 0;
        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(ids.Select(async (id, i) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var res = await _http.GetAsync($"records/{id.ToString(CultureInfo.InvariantCulture)}", ct).ConfigureAwait(false);
                if (res.StatusCode == HttpStatusCode.NotFound) return;   // deleted between the list and the read
                await EnsureOk(res).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                var root = doc.RootElement;
                var bb = root.GetProperty("bbox");
                results[i] = new ShilpiRecord(id,
                    bb.GetProperty("minX").GetDouble(), bb.GetProperty("minY").GetDouble(),
                    bb.GetProperty("maxX").GetDouble(), bb.GetProperty("maxY").GetDouble(),
                    root.TryGetProperty("payloadText", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null);
            }
            finally
            {
                gate.Release();
                progress?.Report((Interlocked.Increment(ref done), ids.Count));
            }
        })).ConfigureAwait(false);
        return results.Where(r => r is not null).Select(r => r!).ToList();
    }

    static async Task EnsureOk(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException($"ShilpiDB gateway answered {(int)res.StatusCode}: {body}");
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
