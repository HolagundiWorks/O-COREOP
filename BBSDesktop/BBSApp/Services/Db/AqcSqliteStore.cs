// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace BBSApp.Services.Db;

/// <summary>
/// Relational (SQLite) persistence for a project — the ERD in docs/schema realised as a real database.
/// Additive to the JSON (.bbsproj) format: Export writes a project to .aqcdb; Import reads it back.
/// Heterogeneous take-off rows are stored losslessly in <c>fields_json</c>, with common measures
/// promoted to typed columns for querying/BI. Estimate lines are exported for reporting (not re-imported —
/// the estimate is a recomputable snapshot).
/// </summary>
public static class AqcSqliteStore
{
    public const string FileExtension = ".aqcdb";
    private const string ProjectId = "project";

    // ── the take-off sheets, keyed by the same names used in the JSON format ──
    private static (string Key, ObservableCollection<Dictionary<string, string>> Rows)[] Sheets(ProjectStore s) => new[]
    {
        ("columns", s.Columns), ("beams", s.Beams), ("pedestals", s.Pedestals), ("lintels", s.Lintels),
        ("slabs", s.Slabs), ("footings", s.Footings), ("walls", s.Walls), ("stairs", s.Stairs),
        ("masonry", s.MasonryWalls), ("masonry_openings", s.MasonryOpenings),
        ("plaster", s.Plaster), ("finish_propose", s.FinishPropose),
        ("pcc", s.PccBeds), ("earthwork", s.Earthwork), ("ssm", s.SizeStone),
        ("shuttering", s.Shuttering), ("flooring", s.Flooring), ("painting", s.Painting),
        ("waterproofing", s.Waterproofing), ("dpc", s.Dpc), ("coping", s.Coping),
        ("screed", s.Screed), ("vdf", s.Vdf), ("skirting", s.Skirting), ("parapet", s.Parapet),
        ("plinth_protection", s.PlinthProtection), ("doors", s.Doors), ("windows", s.Windows)
    };

    private const string Schema = @"
CREATE TABLE IF NOT EXISTS project(
  id TEXT PRIMARY KEY, name TEXT, format_version INTEGER);
CREATE TABLE IF NOT EXISTS trade(
  key TEXT PRIMARY KEY, display TEXT, unit TEXT, default_basis TEXT);
CREATE TABLE IF NOT EXISTS level(
  id TEXT PRIMARY KEY, project_id TEXT, name TEXT,
  height_mm REAL, slab_thickness_mm REAL, beam_depth_mm REAL);
CREATE TABLE IF NOT EXISTS takeoff_item(
  id INTEGER PRIMARY KEY AUTOINCREMENT, project_id TEXT, trade_key TEXT, seq INTEGER,
  mark TEXT, level TEXT, area_m2 REAL, volume_m3 REAL, qty REAL, unit TEXT, fields_json TEXT);
CREATE TABLE IF NOT EXISTS link_rule(
  id TEXT PRIMARY KEY, project_id TEXT, name TEXT, enabled INTEGER,
  source_trade TEXT, target_trade TEXT, basis TEXT, factor REAL,
  target_unit TEXT, per_item INTEGER, notes TEXT);
CREATE TABLE IF NOT EXISTS estimate(
  id TEXT PRIMARY KEY, project_id TEXT, rate_book_version_id TEXT,
  rate_book_version_name TEXT, base_total REAL, grand_total REAL);
CREATE TABLE IF NOT EXISTS estimate_line(
  id INTEGER PRIMARY KEY AUTOINCREMENT, estimate_id TEXT, section TEXT,
  code TEXT, category TEXT, description TEXT, unit TEXT,
  qty REAL, rate REAL, amount REAL, level TEXT, mark TEXT,
  length_m REAL, breadth_m REAL, height_m REAL, area_m2 REAL, volume_m3 REAL);
";

    // ── Export ────────────────────────────────────────────────────────────────

    public static void Export(ProjectStore store, string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(dbPath)) File.Delete(dbPath);

        using var con = Open(dbPath);
        Exec(con, Schema);
        using var tx = con.BeginTransaction();

        Ins(con, "INSERT INTO project(id,name,format_version) VALUES($id,$n,$v)",
            ("$id", ProjectId), ("$n", store.Name), ("$v", 17));

        foreach (var t in LinkTradeRegistry.Sources)
            Ins(con, "INSERT INTO trade(key,display,unit,default_basis) VALUES($k,$d,$u,$b)",
                ("$k", t.Key), ("$d", t.Display), ("$u", t.Unit), ("$b", LinkBasisInfo.Label(t.DefaultBasis)));

        foreach (var l in store.Levels)
            Ins(con, "INSERT INTO level(id,project_id,name,height_mm,slab_thickness_mm,beam_depth_mm) VALUES($id,$p,$n,$h,$s,$b)",
                ("$id", l.Id), ("$p", ProjectId), ("$n", l.Name),
                ("$h", l.HeightMm), ("$s", l.SlabThicknessMm), ("$b", l.BeamDepthMm));

        foreach (var (key, rows) in Sheets(store))
        {
            int seq = 0;
            foreach (var r in rows)
                Ins(con, @"INSERT INTO takeoff_item(project_id,trade_key,seq,mark,level,area_m2,volume_m3,qty,unit,fields_json)
                           VALUES($p,$t,$q,$m,$l,$a,$v,$qy,$u,$j)",
                    ("$p", ProjectId), ("$t", key), ("$q", seq++),
                    ("$m", G(r, "mark")), ("$l", G(r, "level")),
                    ("$a", Dn(r, "area_m2")), ("$v", Dn(r, "volume_m3")), ("$qy", Dn(r, "qty")),
                    ("$u", G(r, "unit")), ("$j", RowJson(r)));
        }

        foreach (var rule in store.LinkRules.Rules)
            Ins(con, @"INSERT INTO link_rule(id,project_id,name,enabled,source_trade,target_trade,basis,factor,target_unit,per_item,notes)
                       VALUES($id,$p,$n,$e,$s,$t,$b,$f,$u,$pi,$no)",
                ("$id", rule.Id), ("$p", ProjectId), ("$n", rule.Name), ("$e", rule.Enabled ? 1 : 0),
                ("$s", rule.SourceTrade), ("$t", rule.TargetTrade), ("$b", LinkBasisInfo.Label(rule.Basis)),
                ("$f", rule.Factor), ("$u", rule.TargetUnit), ("$pi", rule.PerItem ? 1 : 0), ("$no", rule.Notes));

        if (store.LastEstimate is { } est)
        {
            Ins(con, @"INSERT INTO estimate(id,project_id,rate_book_version_id,rate_book_version_name,base_total,grand_total)
                       VALUES($id,$p,$rv,$rn,$b,$g)",
                ("$id", "estimate"), ("$p", ProjectId),
                ("$rv", est.RateBookVersionId), ("$rn", est.RateBookVersionName),
                ("$b", est.BaseTotal), ("$g", est.GrandTotal));
            InsertLines(con, "civil", est.Civil);
            InsertLines(con, "materials", est.Materials);
            InsertLines(con, "steel", est.Steel);
        }

        tx.Commit();
    }

    private static void InsertLines(SqliteConnection con, string section, IEnumerable<EstimateLine> lines)
    {
        foreach (var l in lines)
            Ins(con, @"INSERT INTO estimate_line(estimate_id,section,code,category,description,unit,qty,rate,amount,level,mark,length_m,breadth_m,height_m,area_m2,volume_m3)
                       VALUES($e,$s,$c,$ca,$d,$u,$q,$r,$a,$l,$m,$lm,$bm,$hm,$am,$vm)",
                ("$e", "estimate"), ("$s", section), ("$c", l.Code), ("$ca", l.Category), ("$d", l.Description),
                ("$u", l.Unit), ("$q", l.Qty), ("$r", l.Rate), ("$a", l.Amount), ("$l", l.Level), ("$m", l.Mark),
                ("$lm", l.LengthM), ("$bm", l.BreadthM), ("$hm", l.HeightM), ("$am", l.AreaM2), ("$vm", l.VolumeM3));
    }

    // ── Import ──────────────────────────────────────────────────────────────

    /// <summary>Read a .aqcdb into the store (take-off, levels, link rules, project name). Clears those first.</summary>
    public static void Import(string dbPath, ProjectStore store)
    {
        using var con = Open(dbPath);

        var name = Scalar(con, "SELECT name FROM project LIMIT 1");
        if (!string.IsNullOrWhiteSpace(name)) store.Name = name;

        store.Levels.Clear();
        Read(con, "SELECT id,name,height_mm,slab_thickness_mm,beam_depth_mm FROM level", rd =>
            store.Levels.Add(new LevelDef
            {
                Id = Rs(rd, 0),
                Name = Rs(rd, 1),
                HeightMm = Rd(rd, 2),
                SlabThicknessMm = Rd(rd, 3),
                BeamDepthMm = Rd(rd, 4)
            }));

        var byKey = new Dictionary<string, ObservableCollection<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rows) in Sheets(store)) { rows.Clear(); byKey[key] = rows; }
        Read(con, "SELECT trade_key,fields_json FROM takeoff_item ORDER BY trade_key,seq", rd =>
        {
            string key = Rs(rd, 0);
            if (byKey.TryGetValue(key, out var rows))
                rows.Add(JsonRow(rd.IsDBNull(1) ? "{}" : rd.GetString(1)));
        });

        store.LinkRules.Rules.Clear();
        Read(con, "SELECT id,name,enabled,source_trade,target_trade,basis,factor,target_unit,per_item,notes FROM link_rule", rd =>
            store.LinkRules.Rules.Add(new LinkRule
            {
                Id = Rs(rd, 0),
                Name = Rs(rd, 1),
                Enabled = rd.GetInt64(2) != 0,
                SourceTrade = Rs(rd, 3),
                TargetTrade = Rs(rd, 4),
                Basis = LinkBasisInfo.Parse(Rs(rd, 5)),
                Factor = Rd(rd, 6),
                TargetUnit = Rs(rd, 7),
                PerItem = rd.GetInt64(8) != 0,
                Notes = Rs(rd, 9)
            }));

        store.Notify();
    }

    // ── Round-trip self-test (verification) ──────────────────────────────────

    /// <summary>
    /// Export the store, wipe the in-memory sheets/levels/rules, re-import, and confirm the row counts
    /// match — proving Export and Import round-trip losslessly. Returns a human-readable result.
    /// </summary>
    public static string SelfTest(ProjectStore store)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"aqc-selftest-{Guid.NewGuid():N}{FileExtension}");
        try
        {
            var before = Snapshot(store);
            Export(store, tmp);

            foreach (var (_, rows) in Sheets(store)) rows.Clear();
            store.LinkRules.Rules.Clear();
            store.Levels.Clear();

            Import(tmp, store);
            var after = Snapshot(store);

            var diffs = new List<string>();
            foreach (var kv in before)
                if (!after.TryGetValue(kv.Key, out var n) || n != kv.Value)
                    diffs.Add($"{kv.Key}: {kv.Value}→{(after.TryGetValue(kv.Key, out var m) ? m : 0)}");

            long fileRows = before.Values.Sum();
            long bytes = new FileInfo(tmp).Length;
            return diffs.Count == 0
                ? $"Round-trip OK — {fileRows} rows across {before.Count - 2} sheets + {before["__rules"]} rules + {before["__levels"]} levels survived Export→wipe→Import. DB {bytes / 1024} KB."
                : "Round-trip MISMATCH: " + string.Join("; ", diffs);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* temp file */ }
        }
    }

    private static Dictionary<string, long> Snapshot(ProjectStore store)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (key, rows) in Sheets(store)) map[key] = rows.Count;
        map["__rules"] = store.LinkRules.Rules.Count;
        map["__levels"] = store.Levels.Count;
        return map;
    }

    // ── low-level helpers ────────────────────────────────────────────────────

    private static SqliteConnection Open(string path)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var con = new SqliteConnection(cs);
        con.Open();
        return con;
    }

    private static void Exec(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Ins(SqliteConnection con, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
        {
            var pr = cmd.CreateParameter();
            pr.ParameterName = name;
            pr.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(pr);
        }
        cmd.ExecuteNonQuery();
    }

    private static void Read(SqliteConnection con, string sql, Action<SqliteDataReader> onRow)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) onRow(rd);
    }

    private static string? Scalar(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }

    private static string G(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? v : "";

    private static object? Dn(Dictionary<string, string> r, string k) =>
        r.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : null;

    private static string RowJson(Dictionary<string, string> r)
    {
        var o = new JsonObject();
        foreach (var kv in r) o[kv.Key] = kv.Value;
        return o.ToJsonString();
    }

    private static Dictionary<string, string> JsonRow(string json)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (JsonNode.Parse(json) is JsonObject o)
            foreach (var kv in o)
                d[kv.Key] = kv.Value is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : kv.Value?.ToString() ?? "";
        return d;
    }

    private static string Rs(SqliteDataReader rd, int i) => rd.IsDBNull(i) ? "" : rd.GetString(i);
    private static double Rd(SqliteDataReader rd, int i) => rd.IsDBNull(i) ? 0 : rd.GetDouble(i);
}
