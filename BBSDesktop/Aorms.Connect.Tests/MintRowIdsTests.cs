// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aorms.Connect;
using Xunit;

namespace Aorms.Connect.Tests;

public class MintRowIdsTests
{
    static Dictionary<string, string> Row(string? rid = null, string name = "C1")
    {
        var d = new Dictionary<string, string> { ["mark"] = name };
        if (rid is not null) d[ProjectMapper.RowIdKey] = rid;
        return d;
    }

    [Fact]
    public void Rows_without_an_id_get_distinct_ids_and_existing_ids_are_kept()
    {
        var a = Row("keep-1"); var b = Row(); var c = Row();
        var n = ProjectMapper.MintRowIds(new IDictionary<string, string>[] { a, b, c });
        Assert.Equal(2, n);
        Assert.Equal("keep-1", a[ProjectMapper.RowIdKey]);
        Assert.NotEqual(b[ProjectMapper.RowIdKey], c[ProjectMapper.RowIdKey]);
        Assert.Equal(32, b[ProjectMapper.RowIdKey].Length);
    }

    [Fact]
    public void An_empty_id_counts_as_none()
    {
        var a = Row("");
        Assert.Equal(1, ProjectMapper.MintRowIds(new IDictionary<string, string>[] { a }));
        Assert.NotEqual("", a[ProjectMapper.RowIdKey]);
    }

    [Fact]
    public void A_duplicated_row_keeps_the_original_id_on_the_original_and_the_copy_gets_a_new_one()
    {
        var original = Row("same", "C1");
        var copy = new Dictionary<string, string>(original) { ["mark"] = "C1 copy" };   // what "duplicate row" does
        var n = ProjectMapper.MintRowIds(new IDictionary<string, string>[] { original, copy });
        Assert.Equal(1, n);
        Assert.Equal("same", original[ProjectMapper.RowIdKey]);
        Assert.NotEqual("same", copy[ProjectMapper.RowIdKey]);
    }

    [Fact]
    public void Ids_across_different_sheets_are_unique_too_and_a_second_pass_changes_nothing()
    {
        var sheet1 = new[] { Row("x"), Row() };
        var sheet2 = new[] { Row("x"), Row() };   // "x" appears on both sheets
        var all = sheet1.Concat(sheet2).Cast<IDictionary<string, string>>().ToList();
        ProjectMapper.MintRowIds(all);
        Assert.Equal(4, all.Select(r => r[ProjectMapper.RowIdKey]).Distinct().Count());
        Assert.Equal(0, ProjectMapper.MintRowIds(all));
    }

    [Fact]
    public void The_minted_ids_survive_the_mapper_so_a_row_has_the_same_id_on_every_sync()
    {
        var rows = new[] { Row(), Row() };
        ProjectMapper.MintRowIds(rows.Cast<IDictionary<string, string>>());
        var doc = new System.Text.Json.Nodes.JsonObject
        {
            ["format"] = "bbsproj", ["version"] = 17, ["name"] = "t",
            ["columns"] = new System.Text.Json.Nodes.JsonArray(rows.Select(r =>
            {
                var o = new System.Text.Json.Nodes.JsonObject();
                foreach (var kv in r) o[kv.Key] = kv.Value;
                return (System.Text.Json.Nodes.JsonNode)o;
            }).ToArray()),
        };
        var first = ProjectMapper.ToSync(doc).Rows.Select(r => r.RowId).ToList();
        var second = ProjectMapper.ToSync(doc).Rows.Select(r => r.RowId).ToList();
        Assert.Equal(first, second);
        Assert.Equal(rows.Select(r => r[ProjectMapper.RowIdKey]), first);
    }

    // ---- derived rows: shuttering and link-rule rows are rebuilt from scratch whenever quantities are calculated ----

    static Dictionary<string, string> Shutter(string mark = "SH-C1", string include = "Yes", string level = "Lvl1") => new()
    {
        ["mark"] = mark, ["level"] = level, ["source"] = "auto", ["rcc_mark"] = mark.Replace("SH-", ""), ["include"] = include, ["area_m2"] = "12.5",
    };

    static string Mint(string section, Dictionary<string, string> row)
    {
        ProjectMapper.MintRowIds(new[] { (section, (IDictionary<string, string>)row) });
        return row[ProjectMapper.RowIdKey];
    }

    [Fact]
    public void A_derived_row_rebuilt_from_scratch_gets_the_same_id_every_time()
    {
        var first = Mint("shuttering", Shutter());
        var rebuilt = Mint("shuttering", Shutter());   // SyncStore clears the sheet and adds new dictionaries
        Assert.Equal(first, rebuilt);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void The_id_follows_what_the_row_is_not_what_is_in_it()
    {
        var id = Mint("shuttering", Shutter());
        Assert.Equal(id, Mint("shuttering", Shutter(include: "No")));          // a flag or quantity changing is an edit, not a new row
        Assert.NotEqual(id, Mint("shuttering", Shutter("SH-C2")));             // another member is another row
        Assert.NotEqual(id, Mint("shuttering", Shutter(level: "Lvl2")));       // another floor is another row
        Assert.NotEqual(id, Mint("plaster", Shutter()));                       // another sheet is another row
    }

    [Fact]
    public void A_derived_row_that_had_a_random_id_gets_the_stable_one_once()
    {
        var row = Shutter(); row[ProjectMapper.RowIdKey] = "random-old-id";
        var rows = new[] { ("shuttering", (IDictionary<string, string>)row) };
        Assert.Equal(1, ProjectMapper.MintRowIds(rows));
        Assert.Equal(ProjectMapper.DerivedRowId("shuttering", row), row[ProjectMapper.RowIdKey]);
        Assert.Equal(0, ProjectMapper.MintRowIds(rows));
    }

    [Fact]
    public void Two_derived_rows_with_the_same_identity_still_get_different_ids()
    {
        var a = Shutter(); var b = Shutter();
        ProjectMapper.MintRowIds(new[] { ("shuttering", (IDictionary<string, string>)a), ("shuttering", (IDictionary<string, string>)b) });
        Assert.NotEqual(a[ProjectMapper.RowIdKey], b[ProjectMapper.RowIdKey]);
    }

    [Fact]
    public void Typed_rows_are_not_treated_as_derived()
    {
        var typed = new Dictionary<string, string> { ["mark"] = "SH-C1", ["level"] = "Lvl1", ["source"] = "manual" };
        var id1 = Mint("shuttering", typed);
        var again = new Dictionary<string, string> { ["mark"] = "SH-C1", ["level"] = "Lvl1", ["source"] = "manual" };
        Assert.NotEqual(id1, Mint("shuttering", again));   // random, as before: a typed row is kept, never regenerated
        Assert.False(ProjectMapper.IsDerived(typed));
        Assert.False(ProjectMapper.IsDerived(new Dictionary<string, string> { ["source"] = "auto" }));   // no mark: cannot be identified
        Assert.True(ProjectMapper.IsDerived(new Dictionary<string, string> { ["source"] = "auto_wall", ["mark"] = "PL-1" }));
    }

    [Fact]
    public void Rebuilding_a_derived_sheet_between_two_diffs_changes_nothing_on_the_server()
    {
        var baseline = new RowBaseline();
        List<SyncRow> Snapshot()
        {
            var rows = new[] { Shutter("SH-C1"), Shutter("SH-C2") };   // a fresh sheet each time, like SyncStore
            ProjectMapper.MintRowIds(rows.Select(r => ("shuttering", (IDictionary<string, string>)r)));
            return rows.Select(r => new SyncRow("shuttering", r[ProjectMapper.RowIdKey], System.Text.Json.Nodes.JsonObject.Create(
                System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(r)).RootElement)!)).ToList();
        }
        baseline.Reset(Snapshot());
        var (up, del) = baseline.Diff(Snapshot());
        Assert.Empty(up); Assert.Empty(del);
    }
}
