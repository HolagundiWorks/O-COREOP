// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Aorms.Connect;
using Xunit;

namespace Aorms.Connect.Tests;

public class RowBaselineTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "aqc-baseline-" + Guid.NewGuid().ToString("N"));
    string BaseFile => Path.Combine(_dir, "sub", "p.base.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* temp */ } }

    static SyncRow Row(string section, string id, string name) => new(section, id, new JsonObject { ["name"] = name });

    static List<SyncRow> Three() => new() { Row("columns", "a", "C1"), Row("columns", "b", "C2"), Row("beams", "c", "B1") };

    [Fact]
    public void A_new_project_sends_everything_once_then_nothing()
    {
        var b = new RowBaseline();
        var (up, del) = b.Diff(Three());
        Assert.Equal(3, up.Count);
        Assert.Empty(del);
        var (up2, del2) = b.Diff(Three());
        Assert.Empty(up2); Assert.Empty(del2);
    }

    [Fact]
    public void An_edit_a_new_row_and_a_removed_row_are_each_found()
    {
        var b = new RowBaseline();
        b.Reset(Three());
        var now = new List<SyncRow> { Row("columns", "a", "C1 edited"), Row("beams", "c", "B1"), Row("slabs", "d", "S1") };   // b removed, d added, a changed
        var (up, del) = b.Diff(now);
        Assert.Equal(new[] { "a", "d" }, up.Select(r => r.RowId).OrderBy(x => x));
        var gone = Assert.Single(del);
        Assert.Equal(("columns", "b", true), (gone.Section, gone.RowId, gone.Deleted));
        Assert.Empty(b.Diff(now).Upserts);   // the baseline moved on
    }

    [Fact]
    public void Reset_after_a_push_makes_the_current_state_the_baseline()
    {
        var b = new RowBaseline();
        b.Reset(Three());
        var (up, del) = b.Diff(Three());
        Assert.Empty(up); Assert.Empty(del);
        Assert.Equal(3, b.Count);
    }

    [Fact]
    public void The_baseline_survives_a_restart()
    {
        var a = new RowBaseline(BaseFile);
        Assert.False(a.Loaded);
        a.Reset(Three());
        a.Diff(new List<SyncRow> { Row("columns", "a", "C1 edited") });   // moves on and saves (b and c are now "deleted")

        var again = new RowBaseline(BaseFile);
        Assert.True(again.Loaded);
        Assert.Equal(1, again.Count);
        var (up, del) = again.Diff(new List<SyncRow> { Row("columns", "a", "C1 edited") });
        Assert.Empty(up); Assert.Empty(del);
        Assert.False(File.Exists(BaseFile + ".tmp"));
    }

    [Fact]
    public void A_corrupt_baseline_file_is_treated_as_none()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BaseFile)!);
        File.WriteAllText(BaseFile, "{ not json");
        var b = new RowBaseline(BaseFile);
        Assert.False(b.Loaded);
        Assert.Equal(0, b.Count);
    }

    [Fact]
    public void Rows_adopted_from_the_server_do_not_look_like_local_edits()
    {
        var b = new RowBaseline();
        b.Reset(Three());
        var fromServer = new[] { Row("columns", "a", "C1 changed elsewhere"), new SyncRow("beams", "c", new JsonObject(), true) };
        b.Adopt(fromServer);
        // the project now matches the server: a is the new text, c is gone
        var (up, del) = b.Diff(new List<SyncRow> { Row("columns", "a", "C1 changed elsewhere"), Row("columns", "b", "C2") });
        Assert.Empty(up); Assert.Empty(del);
    }

    [Fact]
    public void Deleted_rows_in_the_current_list_are_ignored_and_delete_removes_the_file()
    {
        var b = new RowBaseline(BaseFile);
        b.Reset(Three());
        Assert.True(File.Exists(BaseFile));
        b.Delete();
        Assert.False(File.Exists(BaseFile));
        Assert.Equal(0, b.Count);
    }

    [Fact]
    public void A_row_whose_fields_are_identical_is_not_resent()
    {
        var b = new RowBaseline();
        b.Reset(new[] { new SyncRow("columns", "a", new JsonObject { ["w"] = "230", ["d"] = "450" }) });
        var (up, _) = b.Diff(new[] { new SyncRow("columns", "a", new JsonObject { ["w"] = "230", ["d"] = "450" }) });
        Assert.Empty(up);
    }
}

public class ApplyDeltaTests
{
    static JsonObject Pilot() => (JsonObject)JsonNode.Parse(File.ReadAllText("pilot-sample.json"))!;

    static Dictionary<(string, string), SyncRow> Rows(JsonObject doc) => ProjectMapper.ToSync(doc).Rows.ToDictionary(r => (r.Section, r.RowId));

    [Fact]
    public void An_empty_delta_changes_nothing()
    {
        var doc = Pilot();
        var before = Rows(doc);
        var after = Rows(ProjectMapper.ApplyDelta(doc, Array.Empty<SyncRow>()));
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var k in before.Keys) Assert.Equal(before[k].Fields.ToJsonString(), after[k].Fields.ToJsonString());
    }

    [Fact]
    public void A_changed_row_replaces_only_that_row()
    {
        var doc = Pilot();
        var before = Rows(doc);
        var target = before.Values.First(r => r.Section == "masonry");
        var edited = (JsonObject)target.Fields.DeepClone();
        edited["length"] = "99999";
        var after = Rows(ProjectMapper.ApplyDelta(doc, new[] { target with { Fields = edited } }));
        Assert.Equal("99999", (string)after[(target.Section, target.RowId)].Fields["length"]!);
        Assert.Equal(before.Count, after.Count);
        foreach (var k in before.Keys.Where(k => k != (target.Section, target.RowId)))
            Assert.Equal(before[k].Fields.ToJsonString(), after[k].Fields.ToJsonString());
    }

    [Fact]
    public void A_new_row_is_added_and_a_deleted_row_is_removed()
    {
        var doc = Pilot();
        var before = Rows(doc);
        var gone = before.Values.First(r => r.Section == "masonry");
        var added = new SyncRow("masonry", "ffffffffffffffffffffffffffffffff", new JsonObject { ["mark"] = "MW-NEW", ["_rid"] = "ffffffffffffffffffffffffffffffff" });
        var after = Rows(ProjectMapper.ApplyDelta(doc, new[] { added, new SyncRow(gone.Section, gone.RowId, new JsonObject(), true) }));
        Assert.True(after.ContainsKey(("masonry", added.RowId)));
        Assert.False(after.ContainsKey((gone.Section, gone.RowId)));
        Assert.Equal(before.Count, after.Count);   // one in, one out
    }

    [Fact]
    public void The_delta_is_idempotent()
    {
        var doc = Pilot();
        var target = Rows(doc).Values.First(r => r.Section == "columns");
        var edited = (JsonObject)target.Fields.DeepClone(); edited["width"] = "777";
        var delta = new[] { target with { Fields = edited } };
        var once = ProjectMapper.ApplyDelta(doc, delta);
        var twice = ProjectMapper.ApplyDelta(once, delta);
        Assert.Equal(once.ToJsonString(), twice.ToJsonString());
    }

    [Fact]
    public void Takeoff_and_last_estimate_and_settings_are_kept()
    {
        var doc = Pilot();
        doc["takeoff"] = new JsonObject { ["pdf_path"] = "x.pdf" };
        doc["last_estimate"] = new JsonObject { ["total"] = 1 };
        var after = ProjectMapper.ApplyDelta(doc, Array.Empty<SyncRow>());
        Assert.Equal("x.pdf", (string)after["takeoff"]!["pdf_path"]!);
        Assert.Equal(1, (int)after["last_estimate"]!["total"]!);
        Assert.Equal(doc["name"]?.ToJsonString(), after["name"]?.ToJsonString());
    }

    [Fact]
    public void Deleting_a_parent_takes_its_lines_with_it()
    {
        var doc = new JsonObject
        {
            ["format"] = "bbsproj", ["version"] = 17, ["name"] = "t",
            ["contracts"] = new JsonObject { ["contracts"] = new JsonArray(new JsonObject
            {
                ["id"] = "k1", ["title"] = "K",
                ["lines"] = new JsonArray(new JsonObject { ["desc"] = "L0" }, new JsonObject { ["desc"] = "L1" }),
            }) },
        };
        Assert.Equal(3, Rows(doc).Count);   // parent + 2 lines
        var after = ProjectMapper.ApplyDelta(doc, new[] { new SyncRow("contracts", "k1", new JsonObject(), true) });
        Assert.Empty(Rows(after));
    }

    [Fact]
    public void A_line_changed_elsewhere_lands_in_the_right_parent()
    {
        var doc = new JsonObject
        {
            ["format"] = "bbsproj", ["version"] = 17, ["name"] = "t",
            ["contracts"] = new JsonObject { ["contracts"] = new JsonArray(new JsonObject
            {
                ["id"] = "k1", ["title"] = "K",
                ["lines"] = new JsonArray(new JsonObject { ["desc"] = "L0" }, new JsonObject { ["desc"] = "L1" }),
            }) },
        };
        var after = ProjectMapper.ApplyDelta(doc, new[] { new SyncRow("contract_lines", "k1:1", new JsonObject { ["desc"] = "L1 changed" }) });
        var lines = (JsonArray)after["contracts"]!["contracts"]![0]!["lines"]!;
        Assert.Equal(new[] { "L0", "L1 changed" }, lines.Select(l => (string)l!["desc"]!));
    }
}
