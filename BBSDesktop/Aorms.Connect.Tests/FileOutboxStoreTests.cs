// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Aorms.Connect;
using Xunit;

namespace Aorms.Connect.Tests;

public class FileOutboxStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "aqc-outbox-" + Guid.NewGuid().ToString("N"));
    string File1 => Path.Combine(_dir, "sub", "proj.json");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* temp */ } }

    static JsonObject Row(string name) => new() { ["name"] = name };

    [Fact]
    public void Unsent_edits_survive_a_restart_in_order()
    {
        var a = new Outbox(new FileOutboxStore(File1));
        a.Upsert("columns", "r1", Row("C1"));
        a.Upsert("beams", "r2", Row("B1"));
        a.Delete("columns", "r3");

        var b = new Outbox(new FileOutboxStore(File1));   // a new process, same file
        Assert.Equal(3, b.Count);
        var snap = b.Snapshot();
        Assert.Equal(new[] { "r1", "r2", "r3" }, snap.Select(r => r.RowId));
        Assert.True(snap[2].Deleted);
        Assert.Equal("C1", (string)snap[0].Fields["name"]!);
    }

    [Fact]
    public void A_later_change_to_the_same_row_replaces_the_earlier_one_after_restart()
    {
        var a = new Outbox(new FileOutboxStore(File1));
        a.Upsert("columns", "r1", Row("old"));
        a.Upsert("columns", "r1", Row("new"));
        var b = new Outbox(new FileOutboxStore(File1));
        Assert.Single(b.Snapshot());
        Assert.Equal("new", (string)b.Snapshot()[0].Fields["name"]!);
    }

    [Fact]
    public void Acknowledged_rows_stay_gone_after_restart()
    {
        var a = new Outbox(new FileOutboxStore(File1));
        a.Upsert("columns", "r1", Row("C1"));
        a.Upsert("columns", "r2", Row("C2"));
        a.Acknowledge(a.Snapshot().Where(r => r.RowId == "r1"));
        var b = new Outbox(new FileOutboxStore(File1));
        Assert.Equal(new[] { "r2" }, b.Snapshot().Select(r => r.RowId));
    }

    [Fact]
    public void Missing_file_is_an_empty_outbox_and_creates_folders_on_first_save()
    {
        var s = new FileOutboxStore(File1);
        Assert.Null(s.Load());
        s.Save("[]");
        Assert.True(File.Exists(File1));
        Assert.False(File.Exists(File1 + ".tmp"));
    }

    [Fact]
    public void A_corrupt_file_is_moved_aside_not_deleted_and_reported()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        File.WriteAllText(File1, "{ this is not an outbox");
        var s = new FileOutboxStore(File1);
        Assert.Null(s.Load());
        Assert.NotNull(s.QuarantinedPath);
        Assert.True(File.Exists(s.QuarantinedPath));
        Assert.Equal("{ this is not an outbox", File.ReadAllText(s.QuarantinedPath!));
        Assert.False(File.Exists(File1));
        // and the outbox is usable again afterwards
        var box = new Outbox(s);
        box.Upsert("columns", "r1", Row("C1"));
        Assert.Single(new Outbox(new FileOutboxStore(File1)).Snapshot());
    }

    [Fact]
    public void A_json_file_that_is_not_an_array_is_treated_as_corrupt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        File.WriteAllText(File1, "{\"a\":1}");
        var s = new FileOutboxStore(File1);
        Assert.Null(s.Load());
        Assert.NotNull(s.QuarantinedPath);
    }

    [Fact]
    public void A_leftover_temp_file_from_a_crash_does_not_replace_the_good_file()
    {
        var a = new Outbox(new FileOutboxStore(File1));
        a.Upsert("columns", "r1", Row("C1"));
        File.WriteAllText(File1 + ".tmp", "[ half written");   // crash during the next save
        var b = new Outbox(new FileOutboxStore(File1));
        Assert.Single(b.Snapshot());
        b.Upsert("columns", "r2", Row("C2"));                   // the next save overwrites the stale temp file
        Assert.Equal(2, new Outbox(new FileOutboxStore(File1)).Count);
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        var s = new FileOutboxStore(File1);
        s.Save("[]");
        s.Delete();
        Assert.False(File.Exists(File1));
        s.Delete();   // twice is fine
    }
}
