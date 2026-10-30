// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aqc.CadImport;

/// <summary>One take-off record as the importer reads it: id, bounding box (millimetres), the payload as text (null when it is not UTF-8) and as hex.</summary>
public sealed record TakeoffRecord(ulong Id, double MinX, double MinY, double MaxX, double MaxY, string? PayloadText, string? PayloadHex = null);

/// <summary>One drawing's take-off in a project file: what its header says, without reading its rows.</summary>
public sealed record TakeoffDrawing(
    uint Slot, string DrawingId, string Drawing, string? ProjectOfficeId, string State, string? Exported, string? RevisionSha256,
    IReadOnlyDictionary<byte, int> Counts, AnnotatedPdfInfo? Annotated, string? Problem, string? ProjectRefCompany = null, long? ProjectRefId = null)
{
    public bool IsComplete => State == "complete";
    /// <summary>The drawing's name, or "drawing &lt;slot&gt;" when O-Draft recorded an unsaved drawing that has no name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Drawing) ? $"drawing {Slot}" : Drawing;
    public int Rows => Counts.Where(kv => kv.Key != OspTakeoff.KindHeader && kv.Key != OspTakeoff.KindAnnotatedChunk).Sum(kv => kv.Value);
}

/// <summary>The annotated drawing O-Draft generated once the take-off was complete: a PDF stored as numbered chunks in the project file.</summary>
public sealed record AnnotatedPdfInfo(string Sha256, long Bytes, int Chunks, int Pages, string? Generated);

/// <summary>
/// Take-off rows O-Draft records in a project file (`.osp` take-off tables), version 1. Each measured item is one record whose payload is the same JSON object
/// the hcw-aqc-bridge v1 file uses for that row, so the import rules, idempotence (source = hcwcad:&lt;drawing_id&gt;, source_ref = ref) and preview match the file importer.
/// </summary>
public static class OspTakeoff
{
    /// <summary>JSON <c>format</c> in take-off headers (wire name from O-Draft; unchanged for compatibility).</summary>
    public const string HeaderFormatWire = "hcw-takeoff-shilpi";
    public const int Version = 1;

    public const byte KindHeader = 1, KindWall = 3, KindOpening = 4, KindColumn = 5, KindLintel = 6, KindRoom = 7, KindSlab = 8, KindAnnotatedChunk = 9,
        KindElectricalBoard = 10, KindElectricalCircuit = 11;

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
        [KindElectricalBoard] = "electrical", [KindElectricalCircuit] = "electrical_circuits",
    };

    public sealed record Result(BridgeFile File, int Records, IReadOnlyList<string> Skipped, string BridgeJson, TakeoffDrawing Drawing, IReadOnlyList<string>? Notes = null);

    /// <summary>True for the records a take-off read needs (everything except annotated-PDF chunks, which can be large).</summary>
    public static bool IsTakeoffRecord(ulong id) => KindOf(id) != KindAnnotatedChunk;

    /// <summary>Every drawing that has a header in these records. A drawing with an unreadable header is listed with a <see cref="TakeoffDrawing.Problem"/>.</summary>
    public static List<TakeoffDrawing> ListDrawings(IEnumerable<TakeoffRecord> records)
    {
        var all = records.ToList();
        return ListDrawings(all, all.Select(r => r.Id));
    }

    /// <summary>Lists drawings from their header records alone, with the counts taken from every id in the database (so no row payload is downloaded).</summary>
    public static List<TakeoffDrawing> ListDrawings(IEnumerable<TakeoffRecord> headers, IEnumerable<ulong> allIds)
    {
        var counts = allIds.GroupBy(SlotOf).ToDictionary(g => g.Key,
            g => (IReadOnlyDictionary<byte, int>)g.GroupBy(KindOf).ToDictionary(k => k.Key, k => k.Count()));
        var list = new List<TakeoffDrawing>();
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
                list.Add(new TakeoffDrawing(slot, Str(src, "drawing_id"), Str(src, "drawing"), NullIfEmpty(Str(src, "project_office_id")),
                    NullIfEmpty(Str(o, "state")) ?? "complete", NullIfEmpty(Str(src, "exported")), NullIfEmpty(Str(src, "revision_sha256")), c,
                    ann is null ? null : new AnnotatedPdfInfo(Str(ann, "sha256"), Num(ann, "bytes"), (int)Num(ann, "chunks"), (int)Num(ann, "pages"), NullIfEmpty(Str(ann, "generated"))),
                    null, NullIfEmpty(Str(src?["project_ref"] as JsonObject, "company_uuid")), (src?["project_ref"] as JsonObject)?["project_id"] is JsonValue pid && pid.TryGetValue<long>(out var pidValue) ? pidValue : null));
            }
            catch (BridgeFormatException e)
            {
                list.Add(new TakeoffDrawing(slot, "", "", null, "invalid", null, null, c, null, e.Message));
            }
        }
        return list;
    }

    /// <summary>Reads the one drawing in these records. Throws when there are none or several (use <see cref="ListDrawings"/> and pass a slot).</summary>
    public static Result Read(IEnumerable<TakeoffRecord> records)
    {
        var all = records.ToList();
        var drawings = ListDrawings(all);
        if (drawings.Count == 0) throw new BridgeFormatException("This project has no take-off header record. Record the take-off in O-Draft first.");
        if (drawings.Count > 1) throw new BridgeFormatException($"This project holds {drawings.Count} drawings; choose one.");
        return Read(all, drawings[0].Slot);
    }

    /// <summary>Builds the bridge file for one drawing. Throws <see cref="BridgeFormatException"/> when its header is missing, not version 1 in millimetres, or still being recorded.</summary>
    public static Result Read(IEnumerable<TakeoffRecord> records, uint slot)
    {
        var mine = records.Where(r => SlotOf(r.Id) == slot).OrderBy(r => r.Id).ToList();
        var header = mine.FirstOrDefault(r => r.Id == HeaderId(slot))
                     ?? throw new BridgeFormatException($"Drawing slot {slot} has no take-off header record.");
        var drawing = ListDrawings(mine).First();
        if (drawing.Problem is not null) throw new BridgeFormatException(drawing.Problem);
        if (!drawing.IsComplete)
            throw new BridgeFormatException($"The take-off for {drawing.DisplayName} is still being recorded in O-Draft (state: {drawing.State}). Try again when it is complete.");

        var h = ParseObject(header, "header");
        var root = new JsonObject
        {
            ["format"] = "hcw-aqc-bridge", ["version"] = 1, ["units"] = "mm",
            ["source"] = h["source"]?.DeepClone() ?? new JsonObject(),
        };
        foreach (var key in new[] { "project", "levels", "beam_depths_mm", "opening_schedule" })
            if (h[key] is { } node) root[key] = node.DeepClone();

        var skipped = new List<string>();
        var notes = new List<string>();
        var sections = Sections.Values.ToDictionary(s => s, _ => new JsonArray());
        var boxes = new Dictionary<JsonObject, TakeoffRecord>(ReferenceEqualityComparer.Instance);
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
            boxes[row] = r;
            used++;
        }

        FillRoomSizesFromBoxes(sections["rooms"], boxes, notes);
        AssignOpeningsToWalls(sections["openings"], sections["walls"], boxes, notes);

        foreach (var (name, arr) in sections)
            if (arr.Count > 0) root[name] = arr;

        var json = root.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() });
        return new Result(BridgeFile.Parse(json), used, skipped, json, drawing, notes);
    }

    static void FillRoomSizesFromBoxes(JsonArray rooms, Dictionary<JsonObject, TakeoffRecord> boxes, List<string> notes)
    {
        foreach (var node in rooms)
        {
            if (node is not JsonObject room || !boxes.TryGetValue(room, out var rec)) continue;
            if (Dbl(room, "length_mm") > 0 && Dbl(room, "breadth_mm") > 0) continue;
            double w = rec.MaxX - rec.MinX, h = rec.MaxY - rec.MinY;
            if (!(w > 0 && h > 0)) continue;
            double area = room["area_m2"] is JsonValue av && double.TryParse(av.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 0;
            double boxArea = w * h / 1e6;
            double breadth = Math.Min(w, h), length = Math.Max(w, h);
            var name = Str(room, "name") is { Length: > 0 } n ? n : Str(room, "ref");
            if (area > 0 && Math.Abs(boxArea - area) / area > 0.02)
            {
                length = area * 1e6 / breadth;
                room["_note"] = "room outline is not a rectangle: length and breadth are approximate and the area is kept";
                notes.Add($"room {name}: not rectangular, size approximated from its area");
            }
            else notes.Add($"room {name}: length and breadth taken from its outline");
            room["length_mm"] = Math.Round(length, 1);
            room["breadth_mm"] = Math.Round(breadth, 1);
        }
    }

    static void AssignOpeningsToWalls(JsonArray openings, JsonArray walls, Dictionary<JsonObject, TakeoffRecord> boxes, List<string> notes)
    {
        const double tol = 150;
        var candidates = walls.OfType<JsonObject>().Where(w => boxes.ContainsKey(w) && Str(w, "ref").Length > 0).ToList();
        foreach (var node in openings)
        {
            if (node is not JsonObject op || Str(op, "wall_ref").Length > 0 || !boxes.TryGetValue(op, out var o)) continue;
            JsonObject? best = null; double bestDist = double.MaxValue;
            foreach (var w in candidates)
            {
                if (Str(w, "level") != Str(op, "level")) continue;
                var b = boxes[w];
                if (o.MaxX < b.MinX - tol || o.MinX > b.MaxX + tol || o.MaxY < b.MinY - tol || o.MinY > b.MaxY + tol) continue;
                double cx = (o.MinX + o.MaxX) / 2, cy = (o.MinY + o.MaxY) / 2;
                double dx = Math.Max(Math.Max(b.MinX - cx, 0), cx - b.MaxX), dy = Math.Max(Math.Max(b.MinY - cy, 0), cy - b.MaxY);
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < bestDist) { bestDist = dist; best = w; }
            }
            if (best is null) continue;
            op["wall_ref"] = Str(best, "ref");
            op["_note"] = "wall chosen by position in the drawing (the take-off did not name a wall); check it";
            notes.Add($"opening {(Str(op, "mark").Length > 0 ? Str(op, "mark") : Str(op, "ref"))}: placed in wall {Str(best, "ref")} by position");
        }
    }

    public static byte[] ReadAnnotatedPdf(IEnumerable<TakeoffRecord> records, uint slot)
    {
        var mine = records.Where(r => SlotOf(r.Id) == slot).ToList();
        var drawing = ListDrawings(mine).FirstOrDefault() ?? throw new BridgeFormatException($"Drawing slot {slot} has no header.");
        var info = drawing.Annotated ?? throw new BridgeFormatException($"{drawing.DisplayName}: no annotated drawing has been recorded yet.");
        var chunks = mine.Where(r => KindOf(r.Id) == KindAnnotatedChunk).OrderBy(r => NOf(r.Id)).ToList();
        if (chunks.Count != info.Chunks || chunks.Select((c, i) => NOf(c.Id) == (uint)i).Contains(false))
            throw new BridgeFormatException($"{drawing.DisplayName}: the annotated drawing is incomplete ({chunks.Count} of {info.Chunks} parts). Try again when O-Draft has finished.");
        using var ms = new MemoryStream();
        foreach (var c in chunks)
        {
            if (string.IsNullOrEmpty(c.PayloadHex)) throw new BridgeFormatException($"{drawing.DisplayName}: annotated drawing part {NOf(c.Id)} has no data.");
            ms.Write(Convert.FromHexString(c.PayloadHex));
        }
        var bytes = ms.ToArray();
        if (bytes.LongLength != info.Bytes) throw new BridgeFormatException($"{drawing.DisplayName}: the annotated drawing is {bytes.LongLength} bytes, the header says {info.Bytes}.");
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha, info.Sha256, StringComparison.OrdinalIgnoreCase)) throw new BridgeFormatException($"{drawing.DisplayName}: the annotated drawing does not match its checksum; it was changed while being read.");
        if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F') throw new BridgeFormatException($"{drawing.DisplayName}: the annotated drawing is not a PDF.");
        return bytes;
    }

    static void CheckHeader(JsonObject o)
    {
        if (Str(o, "format") != HeaderFormatWire) throw new BridgeFormatException($"The header is not a {HeaderFormatWire} record.");
        if (!(o["version"] is JsonValue v && v.TryGetValue<int>(out var ver) && ver == Version))
            throw new BridgeFormatException($"Unsupported take-off version {o["version"]} (this O-CoreOp reads version {Version}).");
        if (!string.Equals(Str(o, "units"), "mm", StringComparison.OrdinalIgnoreCase))
            throw new BridgeFormatException($"Unsupported units '{Str(o, "units")}' (expected mm).");
        if (string.IsNullOrWhiteSpace(Str(o["source"] as JsonObject, "drawing_id")))
            throw new BridgeFormatException("The header has no source.drawing_id.");
    }

    static JsonObject ParseObject(TakeoffRecord r, string what)
    {
        if (string.IsNullOrWhiteSpace(r.PayloadText))
            throw new BridgeFormatException($"record {r.Id} ({what}): the payload is not text");
        try { return JsonNode.Parse(r.PayloadText) as JsonObject ?? throw new BridgeFormatException($"record {r.Id} ({what}): the payload is not a JSON object"); }
        catch (JsonException e) { throw new BridgeFormatException($"record {r.Id} ({what}): invalid JSON ({e.Message})"); }
    }

    static string Str(JsonObject? o, string key) => o is not null && o[key] is JsonValue v ? v.ToString() : "";
    static long Num(JsonObject o, string key) => o[key] is JsonValue v && long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    static double Dbl(JsonObject o, string key) => o[key] is JsonValue v && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
