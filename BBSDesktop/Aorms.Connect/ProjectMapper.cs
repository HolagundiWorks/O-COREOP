// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aorms.Connect;

/// <summary>One synced row: a section name, a stable row id and its fields.</summary>
public sealed record SyncRow(string Section, string RowId, JsonObject Fields, bool Deleted = false);

/// <summary>A project split for syncing: the project-level settings skeleton plus every row.</summary>
public sealed record SyncProject(JsonObject Settings, List<SyncRow> Rows);

/// <summary>
/// Maps a <c>.bbsproj</c> (v17) document to the contract's rows + settings (AQC-CLIENT-SPEC §5) and back.
/// Take-off rows carry no id, so one is minted and stored inside the row as <c>_rid</c>; it round-trips in the file.
/// <c>last_estimate</c> and <c>takeoff</c> never sync (a version, and a local file path). Pure and platform-neutral.
/// </summary>
public static class ProjectMapper
{
    public const string RowIdKey = "_rid";
    public const int FormatVersion = 17;

    static readonly string[] TakeoffSections =
    {
        "columns", "beams", "pedestals", "lintels", "slabs", "footings", "walls", "stairs",
        "masonry", "masonry_openings", "plaster", "finish_propose", "pcc", "earthwork", "ssm", "shuttering", "flooring",
        "painting", "waterproofing", "dpc", "coping", "screed", "vdf", "skirting", "parapet", "plinth_protection", "doors", "windows",
    };

    // (section, path to the array, id key) — plain id-keyed arrays.
    static readonly (string Section, string[] Path)[] IdSections =
    {
        ("schedule_activities", new[] { "schedule", "activities" }),
        ("transactions", new[] { "accounts", "transactions" }),
        ("documents", new[] { "office", "documents" }),
        ("suppliers", new[] { "stores", "suppliers" }), ("warehouses", new[] { "stores", "warehouses" }),
        ("orders", new[] { "stores", "orders" }), ("grns", new[] { "stores", "grns" }), ("issues", new[] { "stores", "issues" }),
        ("sites", new[] { "org", "sites" }), ("resources", new[] { "org", "resources" }),
        ("employees", new[] { "org", "employees" }), ("payroll", new[] { "org", "payroll" }),
        ("link_rules", new[] { "link_rules" }),
    };

    // (parent section, line section, path, child key)
    static readonly (string Parent, string Line, string[] Path)[] ParentLineSections =
    {
        ("contracts", "contract_lines", new[] { "contracts", "contracts" }),
        ("bills", "bill_lines", new[] { "accounts", "bills" }),
    };

    public static string NewRowId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gives every row a <c>_rid</c> that is unique within <paramref name="rows"/>: a row with none, an empty one, or one another row already has
    /// (a duplicated row copies its original's) gets a new id. The first holder of an id keeps it, so ids already on the server stay put.
    /// Two rows sharing an id would collapse into one on the server. Returns how many ids were minted.
    /// </summary>
    public static int MintRowIds(IEnumerable<IDictionary<string, string>> rows) => MintRowIds(rows.Select(r => ("", r)));

    /// <summary>A row the application derives from other rows (shuttering from RCC members, link-rule rows): its <c>source</c> starts with "auto".</summary>
    public static bool IsDerived(IDictionary<string, string> row) =>
        row.TryGetValue("source", out var s) && s.StartsWith("auto", StringComparison.OrdinalIgnoreCase)
        && row.TryGetValue("mark", out var m) && !string.IsNullOrWhiteSpace(m);

    /// <summary>
    /// A stable id for a derived row, worked out from what it is (its sheet, source, mark, level and the member it comes from) and not from a
    /// random number. Derived sheets are rebuilt from scratch whenever quantities are calculated; with random ids every rebuild would delete and
    /// re-insert every row on the server.
    /// </summary>
    public static string DerivedRowId(string section, IDictionary<string, string> row)
    {
        static string G(IDictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? v.Trim() : "";
        var key = string.Join("|", "derived", section, G(row, "source"), G(row, "mark"), G(row, "level"), G(row, "rcc_mark"), G(row, "source_ref"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)), 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// <see cref="MintRowIds(IEnumerable{IDictionary{string, string}})"/> for rows that know their sheet: a derived row gets <see cref="DerivedRowId"/>
    /// (overwriting a random one, so its id stops changing); the rest are as before. A derived row whose stable id is already taken gets a random one.
    /// </summary>
    public static int MintRowIds(IEnumerable<(string Section, IDictionary<string, string> Row)> rows)
    {
        int minted = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (section, row) in rows)
        {
            if (section.Length > 0 && IsDerived(row))
            {
                var stable = DerivedRowId(section, row);
                if (row.TryGetValue(RowIdKey, out var cur) && cur == stable && seen.Add(stable)) continue;
                if (!seen.Contains(stable)) { row[RowIdKey] = stable; seen.Add(stable); minted++; continue; }
            }
            else if (row.TryGetValue(RowIdKey, out var id) && !string.IsNullOrEmpty(id) && seen.Add(id)) continue;
            var fresh = NewRowId();
            row[RowIdKey] = fresh;
            seen.Add(fresh);
            minted++;
        }
        return minted;
    }

    /// <summary>Splits a project document into settings + rows. Mints <c>_rid</c> on take-off rows that lack one (mutates <paramref name="doc"/>).</summary>
    public static SyncProject ToSync(JsonObject doc)
    {
        var rows = new List<SyncRow>();
        // Built key by key so the take-off rows (thousands of them) are not deep-cloned only to be emptied again.
        var settings = new JsonObject();
        foreach (var kv in doc)
        {
            if (kv.Key is "last_estimate" or "takeoff") continue;
            settings[kv.Key] = kv.Value is JsonArray && Array.IndexOf(TakeoffSections, kv.Key) >= 0 ? new JsonArray() : kv.Value?.DeepClone();
        }

        foreach (var s in TakeoffSections)
        {
            if (doc[s] is not JsonArray arr) continue;
            foreach (var item in arr)
            {
                if (item is not JsonObject o) continue;
                if (o[RowIdKey] is null || string.IsNullOrEmpty(o[RowIdKey]!.GetValue<string>())) o[RowIdKey] = NewRowId();
                rows.Add(new SyncRow(s, o[RowIdKey]!.GetValue<string>(), (JsonObject)o.DeepClone()));
            }
            settings[s] = new JsonArray();
        }
        foreach (var (section, path) in IdSections)
        {
            var arr = Find(doc, path);
            if (arr is null) continue;
            foreach (var item in arr)
                if (item is JsonObject o && IdOf(o) is { } id) rows.Add(new SyncRow(section, id, (JsonObject)o.DeepClone()));
            SetEmpty(settings, path);
        }
        foreach (var (parent, line, path) in ParentLineSections)
        {
            var arr = Find(doc, path);
            if (arr is null) continue;
            foreach (var item in arr)
            {
                if (item is not JsonObject o || IdOf(o) is not { } id) continue;
                var head = (JsonObject)o.DeepClone();
                head.Remove("lines");
                rows.Add(new SyncRow(parent, id, head));
                if (o["lines"] is JsonArray lines)
                    for (var i = 0; i < lines.Count; i++)
                        if (lines[i] is JsonObject lo) rows.Add(new SyncRow(line, $"{id}:{i}", (JsonObject)lo.DeepClone()));
            }
            SetEmpty(settings, path);
        }
        return new SyncProject(settings, rows);
    }

    /// <summary>Rebuilds a project document from settings + rows (deleted rows are skipped).</summary>
    public static JsonObject FromSync(JsonObject settings, IEnumerable<SyncRow> rows)
    {
        var doc = (JsonObject)settings.DeepClone();
        var live = rows.Where(r => !r.Deleted).ToList();

        foreach (var s in TakeoffSections)
        {
            var mine = live.Where(r => r.Section == s).ToList();
            if (mine.Count == 0 && doc[s] is null) continue;
            var arr = new JsonArray();
            foreach (var r in mine) { var f = (JsonObject)r.Fields.DeepClone(); f[RowIdKey] = r.RowId; arr.Add(f); }
            doc[s] = arr;
        }
        foreach (var (section, path) in IdSections)
        {
            var mine = live.Where(r => r.Section == section).ToList();
            if (mine.Count == 0 && Find(doc, path) is null) continue;
            var arr = new JsonArray();
            foreach (var r in mine) arr.Add(r.Fields.DeepClone());
            SetArray(doc, path, arr);
        }
        foreach (var (parent, line, path) in ParentLineSections)
        {
            var heads = live.Where(r => r.Section == parent).ToList();
            if (heads.Count == 0 && Find(doc, path) is null) continue;
            var arr = new JsonArray();
            foreach (var h in heads)
            {
                var o = (JsonObject)h.Fields.DeepClone();
                var ls = new JsonArray();
                foreach (var l in live.Where(r => r.Section == line && r.RowId.StartsWith(h.RowId + ":", StringComparison.Ordinal))
                             .OrderBy(r => int.TryParse(r.RowId[(h.RowId.Length + 1)..], out var n) ? n : int.MaxValue))
                    ls.Add(l.Fields.DeepClone());
                o["lines"] = ls;
                arr.Add(o);
            }
            SetArray(doc, path, arr);
        }
        return doc;
    }

    /// <summary>
    /// Applies rows that changed elsewhere (a catch-up) to a project document and returns the new document. The document is split into rows,
    /// the changed rows are merged in by (section, row id) (a deleted row is removed; a deleted parent takes its lines with it) and the document
    /// is rebuilt, so line numbering and ordering follow <see cref="FromSync"/>. Settings, <c>takeoff</c> and <c>last_estimate</c> stay as they were.
    /// <paramref name="doc"/> is not modified apart from minted row ids.
    /// </summary>
    public static JsonObject ApplyDelta(JsonObject doc, IEnumerable<SyncRow> delta, JsonObject? serverSettings = null)
    {
        var sync = ToSync(doc);
        var merged = new Dictionary<(string, string), SyncRow>();
        var order = new List<(string, string)>();
        foreach (var r in sync.Rows) { merged[(r.Section, r.RowId)] = r; order.Add((r.Section, r.RowId)); }
        foreach (var d in delta)
        {
            var key = (d.Section, d.RowId);
            if (d.Deleted)
            {
                merged.Remove(key);
                foreach (var (_, line, _) in ParentLineSections)
                    if (d.Section == ParentOf(line))
                        foreach (var k in merged.Keys.Where(k => k.Item1 == line && k.Item2.StartsWith(d.RowId + ":", StringComparison.Ordinal)).ToList()) merged.Remove(k);
            }
            else
            {
                if (!merged.ContainsKey(key)) order.Add(key);
                merged[key] = d with { Deleted = false };
            }
        }
        var rows = order.Where(merged.ContainsKey).Select(k => merged[k]).ToList();
        var result = FromSync(serverSettings is null ? sync.Settings : MergeSettings(sync.Settings, serverSettings), rows);
        foreach (var keep in new[] { "takeoff", "last_estimate" })
            if (doc[keep] is { } v) result[keep] = v.DeepClone();
        return result;
    }

    static string ParentOf(string lineSection) => ParentLineSections.First(p => p.Line == lineSection).Parent;
    /// <summary>
    /// Keys of the <c>project</c> block that belong to this computer and must never go to the server or come back from it: where a take-off file is,
    /// where stored PDFs are, the logo path, and how far this file has caught up. Sending them would leak local paths to other users and would make
    /// every sync look like a settings change (the head moves each time).
    /// </summary>
    public static readonly string[] LocalOnlyProjectKeys = { "aorms_head_seq", "takeoff_vdb_path", "annotated_drawings", "logo_path" };

    /// <summary>A copy of the settings skeleton without the keys that are local to this computer.</summary>
    public static JsonObject SyncSettings(JsonObject settings)
    {
        var copy = (JsonObject)settings.DeepClone();
        if (copy["project"] is JsonObject p)
            foreach (var k in LocalOnlyProjectKeys) p.Remove(k);
        return copy;
    }

    /// <summary>Server settings with this computer's local-only project keys put back from <paramref name="local"/>.</summary>
    public static JsonObject MergeSettings(JsonObject local, JsonObject server)
    {
        var merged = SyncSettings(server);
        if (local["project"] is JsonObject lp)
        {
            if (merged["project"] is not JsonObject mp) { mp = new JsonObject(); merged["project"] = mp; }
            foreach (var k in LocalOnlyProjectKeys)
                if (lp[k] is { } v) mp[k] = v.DeepClone();
        }
        return merged;
    }

    /// <summary>Contract wire shape for <c>POST /projects/{id}/rows</c> and <c>POST /projects</c>.</summary>
    public static JsonArray ToWire(IEnumerable<SyncRow> rows)
    {
        var arr = new JsonArray();
        foreach (var r in rows)
        {
            var o = new JsonObject { ["section"] = r.Section, ["row_id"] = r.RowId, ["fields"] = r.Fields.DeepClone() };
            if (r.Deleted) o["deleted"] = true;
            arr.Add(o);
        }
        return arr;
    }

    public static SyncRow FromWire(JsonElement e) => new(
        e.GetProperty("section").GetString()!, e.GetProperty("row_id").GetString()!,
        e.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object ? (JsonObject)JsonNode.Parse(f.GetRawText())! : new JsonObject(),
        e.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True);

    static string? IdOf(JsonObject o) => o["id"] switch { null => null, var n => n.ToString() is { Length: > 0 } s ? s : null };

    static JsonArray? Find(JsonNode root, string[] path)
    {
        JsonNode? n = root;
        foreach (var p in path) n = (n as JsonObject)?[p];
        return n as JsonArray;
    }
    static void SetEmpty(JsonObject root, string[] path) { if (Find(root, path) is not null) SetArray(root, path, new JsonArray()); }
    static void SetArray(JsonObject root, string[] path, JsonArray arr)
    {
        var o = root;
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (o[path[i]] is not JsonObject next) { next = new JsonObject(); o[path[i]] = next; }
            o = next;
        }
        o[path[^1]] = arr;
    }
}
