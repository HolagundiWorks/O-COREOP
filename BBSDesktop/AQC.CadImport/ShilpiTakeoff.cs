// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aqc.CadImport;

/// <summary>One ShilpiDB record as the HTTP gateway returns it: id, bounding box (millimetres), the payload as text (null when it is not UTF-8) and as hex.</summary>
public sealed record ShilpiRecord(ulong Id, double MinX, double MinY, double MaxX, double MaxY, string? PayloadText, string? PayloadHex = null);

/// <summary>One drawing's take-off found in a ShilpiDB: what its header says, without reading its rows.</summary>
public sealed record ShilpiDrawing(
    uint Slot, string DrawingId, string Drawing, string? ProjectOfficeId, string State, string? Exported, string? RevisionSha256,
    IReadOnlyDictionary<byte, int> Counts, AnnotatedPdfInfo? Annotated, string? Problem)
{
    public bool IsComplete => State == "complete";
    public int Rows => Counts.Where(kv => kv.Key != ShilpiTakeoff.KindHeader && kv.Key != ShilpiTakeoff.KindAnnotatedChunk).Sum(kv => kv.Value);
}

/// <summary>The annotated drawing O-Draft generated once the take-off was complete: a PDF held in ShilpiDB as numbered chunks.</summary>
public sealed record AnnotatedPdfInfo(string Sha256, long Bytes, int Chunks, int Pages, string? Generated);

/// <summary>
/// The take-off recorded in ShilpiDB by O-Draft (docs/TAKEOFF-SHILPIDB.md, version 1). One shared database holds the take-offs of many drawings
/// and projects, side by side. Each measured item is one record whose payload is the same JSON object the hcw-aqc-bridge v1 file uses for that
/// row, so the import rules, the idempotence (source = hcwcad:&lt;drawing_id&gt;, source_ref = ref) and the preview are the file importer's.
/// </summary>
public static class ShilpiTakeoff
{
    public const string HeaderFormat = "hcw-takeoff-shilpi";
    public const int Version = 1;

    public const byte KindHeader = 1, KindWall = 3, KindOpening = 4, KindColumn = 5, KindLintel = 6, KindRoom = 7, KindSlab = 8, KindAnnotatedChunk = 9;

    /// <summary>id = kind (8 bits) | drawing slot (24 bits) | n (32 bits).</summary>
    public static ulong MakeId(byte kind, uint slot, uint n) => ((ulong)kind << 56) | ((ulong)(slot & 0xFFFFFF) << 32) | n;
    public static byte KindOf(ulong id) => (byte)(id >> 56);
    public static uint SlotOf(ulong id) => (uint)((id >> 32) & 0xFFFFFF);
    public static uint NOf(ulong id) => (uint)(id & 0xFFFFFFFF);
    public static ulong HeaderId(uint slot) => MakeId(KindHeader, slot, 0);

    /// <summary>
    /// The slot a drawing starts from: 32-bit FNV-1a of the UTF-8 drawing id, folded to 24 bits, never 0. If the header already at that slot
    /// names a different drawing, the writer tries the next slot (wrapping 0xFFFFFF to 1) until it finds a free one or its own.
    /// </summary>
    public static uint SlotFor(string drawingId)
    {
        uint h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(drawingId)) { h ^= b; h *= 16777619; }
        var slot = (h ^ (h >> 24)) & 0xFFFFFF;
        return slot == 0 ? 1 : slot;
    }

    /// <summary>The next slot to try after a collision.</summary>
    public static uint NextSlot(uint slot) => slot >= 0xFFFFFF ? 1 : slot + 1;

    static readonly Dictionary<byte, string> Sections = new()
    {
        [KindWall] = "walls", [KindOpening] = "openings", [KindColumn] = "columns",
        [KindLintel] = "lintels", [KindRoom] = "rooms", [KindSlab] = "slabs",
    };

    public sealed record Result(BridgeFile File, int Records, IReadOnlyList<string> Skipped, string BridgeJson, ShilpiDrawing Drawing);

    /// <summary>True for the records a take-off read needs (everything except annotated-PDF chunks, which can be large).</summary>
    public static bool IsTakeoffRecord(ulong id) => KindOf(id) != KindAnnotatedChunk;

    /// <summary>Every drawing that has a header in these records. A drawing with an unreadable header is listed with a <see cref="ShilpiDrawing.Problem"/>.</summary>
    public static List<ShilpiDrawing> ListDrawings(IEnumerable<ShilpiRecord> records)
    {
        var all = records.ToList();
        return ListDrawings(all, all.Select(r => r.Id));
    }

    /// <summary>Lists drawings from their header records alone, with the counts taken from every id in the database (so no row payload is downloaded).</summary>
    public static List<ShilpiDrawing> ListDrawings(IEnumerable<ShilpiRecord> headers, IEnumerable<ulong> allIds)
    {
        var counts = allIds.GroupBy(SlotOf).ToDictionary(g => g.Key,
            g => (IReadOnlyDictionary<byte, int>)g.GroupBy(KindOf).ToDictionary(k => k.Key, k => k.Count()));
        var list = new List<ShilpiDrawing>();
        foreach (var h in headers.Where(r => KindOf(r.Id) == KindHeader && NOf(r.Id) == 0).OrderBy(r => r.Id))
        {
            var slot = SlotOf(h.Id);
            var c = counts.GetValueOrDefault(slot) ?? new Dictionary<byte, int>();
            try
            {
                var o = ParseObject(h, "header");
                CheckHeader(o);
                var src = o["source"] as JsonObject;
                var ann = o["annotated_pdf"] as JsonObject;
                list.Add(new ShilpiDrawing(slot, Str(src, "drawing_id"), Str(src, "drawing"), NullIfEmpty(Str(src, "project_office_id")),
                    NullIfEmpty(Str(o, "state")) ?? "complete", NullIfEmpty(Str(src, "exported")), NullIfEmpty(Str(src, "revision_sha256")), c,
                    ann is null ? null : new AnnotatedPdfInfo(Str(ann, "sha256"), Num(ann, "bytes"), (int)Num(ann, "chunks"), (int)Num(ann, "pages"), NullIfEmpty(Str(ann, "generated"))),
                    null));
            }
            catch (BridgeFormatException e)
            {
                list.Add(new ShilpiDrawing(slot, "", "", null, "invalid", null, null, c, null, e.Message));
            }
        }
        return list;
    }

    /// <summary>Reads the one drawing in these records. Throws when there are none or several (use <see cref="ListDrawings"/> and pass a slot).</summary>
    public static Result Read(IEnumerable<ShilpiRecord> records)
    {
        var all = records.ToList();
        var drawings = ListDrawings(all);
        if (drawings.Count == 0) throw new BridgeFormatException("This ShilpiDB has no take-off header record. Record the take-off in O-Draft first.");
        if (drawings.Count > 1) throw new BridgeFormatException($"This ShilpiDB holds {drawings.Count} drawings; choose one.");
        return Read(all, drawings[0].Slot);
    }

    /// <summary>Builds the bridge file for one drawing. Throws <see cref="BridgeFormatException"/> when its header is missing, not version 1 in millimetres, or still being recorded.</summary>
    public static Result Read(IEnumerable<ShilpiRecord> records, uint slot)
    {
        var mine = records.Where(r => SlotOf(r.Id) == slot).OrderBy(r => r.Id).ToList();
        var header = mine.FirstOrDefault(r => r.Id == HeaderId(slot))
                     ?? throw new BridgeFormatException($"Drawing slot {slot} has no take-off header record.");
        var drawing = ListDrawings(mine).First();
        if (drawing.Problem is not null) throw new BridgeFormatException(drawing.Problem);
        if (!drawing.IsComplete)
            throw new BridgeFormatException($"The take-off for {drawing.Drawing} is still being recorded in O-Draft (state: {drawing.State}). Try again when it is complete.");

        var h = ParseObject(header, "header");
        var root = new JsonObject
        {
            ["format"] = "hcw-aqc-bridge", ["version"] = 1, ["units"] = "mm",
            ["source"] = h["source"]?.DeepClone() ?? new JsonObject(),
        };
        foreach (var key in new[] { "project", "levels", "beam_depths_mm", "opening_schedule" })
            if (h[key] is { } node) root[key] = node.DeepClone();

        var skipped = new List<string>();
        var sections = Sections.Values.ToDictionary(s => s, _ => new JsonArray());
        int used = 0;
        foreach (var r in mine)
        {
            var kind = KindOf(r.Id);
            if (kind is KindHeader or KindAnnotatedChunk) continue;
            if (!Sections.TryGetValue(kind, out var section)) { skipped.Add($"record {r.Id}: unknown kind {kind}; ignored"); continue; }
            JsonObject row;
            try { row = ParseObject(r, section); }
            catch (BridgeFormatException e) { skipped.Add(e.Message); continue; }
            sections[section].Add(row);
            used++;
        }
        foreach (var (name, arr) in sections)
            if (arr.Count > 0) root[name] = arr;

        var json = root.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() });
        return new Result(BridgeFile.Parse(json), used, skipped, json, drawing);
    }

    /// <summary>Assembles and verifies the annotated PDF of a drawing from its chunk records (SHA-256 and length must match the header).</summary>
    public static byte[] ReadAnnotatedPdf(IEnumerable<ShilpiRecord> records, uint slot)
    {
        var mine = records.Where(r => SlotOf(r.Id) == slot).ToList();
        var drawing = ListDrawings(mine).FirstOrDefault() ?? throw new BridgeFormatException($"Drawing slot {slot} has no header.");
        var info = drawing.Annotated ?? throw new BridgeFormatException($"{drawing.Drawing}: no annotated drawing has been recorded yet.");
        var chunks = mine.Where(r => KindOf(r.Id) == KindAnnotatedChunk).OrderBy(r => NOf(r.Id)).ToList();
        if (chunks.Count != info.Chunks || chunks.Select((c, i) => NOf(c.Id) == (uint)i).Contains(false))
            throw new BridgeFormatException($"{drawing.Drawing}: the annotated drawing is incomplete ({chunks.Count} of {info.Chunks} parts). Try again when O-Draft has finished.");
        using var ms = new MemoryStream();
        foreach (var c in chunks)
        {
            if (string.IsNullOrEmpty(c.PayloadHex)) throw new BridgeFormatException($"{drawing.Drawing}: annotated drawing part {NOf(c.Id)} has no data.");
            ms.Write(Convert.FromHexString(c.PayloadHex));
        }
        var bytes = ms.ToArray();
        if (bytes.LongLength != info.Bytes) throw new BridgeFormatException($"{drawing.Drawing}: the annotated drawing is {bytes.LongLength} bytes, the header says {info.Bytes}.");
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha, info.Sha256, StringComparison.OrdinalIgnoreCase)) throw new BridgeFormatException($"{drawing.Drawing}: the annotated drawing does not match its checksum; it was changed while being read.");
        if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F') throw new BridgeFormatException($"{drawing.Drawing}: the annotated drawing is not a PDF.");
        return bytes;
    }

    static void CheckHeader(JsonObject o)
    {
        if (Str(o, "format") != HeaderFormat) throw new BridgeFormatException($"The header is not a {HeaderFormat} record.");
        if (!(o["version"] is JsonValue v && v.TryGetValue<int>(out var ver) && ver == Version))
            throw new BridgeFormatException($"Unsupported take-off version {o["version"]} (this O-CoreOp reads version {Version}).");
        if (!string.Equals(Str(o, "units"), "mm", StringComparison.OrdinalIgnoreCase))
            throw new BridgeFormatException($"Unsupported units '{Str(o, "units")}' (expected mm).");
        if (string.IsNullOrWhiteSpace(Str(o["source"] as JsonObject, "drawing_id")))
            throw new BridgeFormatException("The header has no source.drawing_id.");
    }

    static JsonObject ParseObject(ShilpiRecord r, string what)
    {
        if (string.IsNullOrWhiteSpace(r.PayloadText))
            throw new BridgeFormatException($"record {r.Id} ({what}): the payload is not text");
        try { return JsonNode.Parse(r.PayloadText) as JsonObject ?? throw new BridgeFormatException($"record {r.Id} ({what}): the payload is not a JSON object"); }
        catch (JsonException e) { throw new BridgeFormatException($"record {r.Id} ({what}): invalid JSON ({e.Message})"); }
    }

    static string Str(JsonObject? o, string key) => o is not null && o[key] is JsonValue v ? v.ToString() : "";
    static long Num(JsonObject o, string key) => o[key] is JsonValue v && long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>Reads records of a ShilpiDB through the shilpi-http gateway (GET /records, then GET /records/{id}).</summary>
public sealed class ShilpiGatewayClient : IDisposable
{
    readonly HttpClient _http;

    public ShilpiGatewayClient(Uri baseUri, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = baseUri;
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<bool> IsUpAsync(CancellationToken ct = default)
    {
        try { using var r = await _http.GetAsync("health", ct).ConfigureAwait(false); return r.IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    /// <summary>The ids in the database (no payloads).</summary>
    public async Task<List<ulong>> ListIdsAsync(CancellationToken ct = default)
    {
        var ids = new List<ulong>();
        using var res = await _http.GetAsync("records", ct).ConfigureAwait(false);
        await EnsureOk(res).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        foreach (var e in doc.RootElement.GetProperty("records").EnumerateArray()) ids.Add(e.GetProperty("id").GetUInt64());
        return ids;
    }

    /// <summary>Reads the records whose id passes <paramref name="include"/> (all when null).</summary>
    /// <param name="progress">Called with (done, total) as records arrive.</param>
    public async Task<List<ShilpiRecord>> ReadAllAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default, Func<ulong, bool>? include = null)
    {
        var ids = (await ListIdsAsync(ct).ConfigureAwait(false)).Where(i => include?.Invoke(i) ?? true).ToList();
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
                    root.TryGetProperty("payloadText", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
                    root.TryGetProperty("payloadHex", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null);
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

    public void Dispose() => _http.Dispose();
}
