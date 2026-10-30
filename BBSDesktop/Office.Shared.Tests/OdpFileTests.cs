// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Office.Shared.Tests;

/// <summary>
/// <c>Fixtures/small.osp</c> was written by the Rust crate (<c>aad-odp</c>, example <c>pack</c>) from a 22 KB DXF and a take-off .vdb that held O-CoreOp's project
/// record, so these tests also prove that the two sides read the same file.
/// </summary>
public class OdpFileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "odp-" + Guid.NewGuid().ToString("N"));

    public OdpFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    string Copy()
    {
        var path = Path.Combine(_dir, "small.osp");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "small.osp"), path);
        return path;
    }

    [Fact]
    public void A_file_the_rust_side_wrote_is_read()
    {
        using var odp = OdpFile.Open(Copy());
        var meta = odp.Meta();
        Assert.Equal("Small", meta.Name);
        Assert.Equal(2, meta.Revision);
        Assert.Equal(32, meta.ProjectId.Length);
        Assert.Equal(new[] { "meter.dxf" }, odp.DrawingNames());
        var d = odp.Drawing("meter.dxf")!;
        Assert.Equal("dxf", d.Ext);
        Assert.Equal(22135, d.Bytes.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(d.Bytes)).ToLowerInvariant(), d.Sha256);
        var header = Assert.Single(odp.TakeoffHeaders());
        Assert.Equal("complete", header.State);
        Assert.Equal(14, odp.TakeoffRows(header.Slot).Count);
        Assert.Equal("house", JsonNode.Parse(odp.ProjectDoc()!)!["project"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Saving_the_project_is_one_revision_and_touches_nothing_else()
    {
        var path = Copy();
        using (var odp = OdpFile.Open(path))
        {
            var drawing = odp.Drawing("meter.dxf")!.Sha256;
            Assert.Equal(3, odp.SetProjectDoc("{\"project\":{\"name\":\"Changed\"}}"));
            Assert.Equal(3, odp.Revision());
            Assert.Equal(drawing, odp.Drawing("meter.dxf")!.Sha256);
            Assert.Equal(14, odp.TakeoffRows(odp.TakeoffHeaders()[0].Slot).Count);
            Assert.Equal(new[] { (3L, "project_doc", "1", "put") }, odp.ChangesSince(2));
        }
        using var again = OdpFile.Open(path);
        Assert.Contains("Changed", again.ProjectDoc());
    }

    [Fact]
    public void A_second_handle_sees_a_commit_made_through_the_first()
    {
        var path = Copy();
        using var writer = OdpFile.Open(path);
        using var reader = OdpFile.Open(path);
        Assert.Equal(2, reader.Revision());
        writer.SetProjectDoc("{\"project\":{}}");
        Assert.Equal(3, reader.Revision());
    }

    [Fact]
    public void A_new_file_is_created_with_the_same_tables_and_a_drawing_round_trips()
    {
        var path = Path.Combine(_dir, "new.osp");
        var bytes = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 251)).ToArray();
        using (var odp = OdpFile.Create(path, "New"))
        {
            Assert.Equal(0, odp.Revision());
            Assert.Equal(1, odp.SetDrawing("a.DWG", ".DWG", bytes));
        }
        Assert.True(OdpFile.LooksLikeOdp(path));
        using var open = OdpFile.Open(path);
        Assert.Equal(bytes, open.Drawing("a.DWG")!.Bytes);
        Assert.Equal("dwg", open.Drawing("a.DWG")!.Ext);
        Assert.Null(open.ProjectDoc());
        Assert.Throws<IOException>(() => OdpFile.Create(path, "again"));
    }

    [Fact]
    public void Files_that_are_not_projects_are_refused()
    {
        var text = Path.Combine(_dir, "t.osp");
        File.WriteAllText(text, "this is not sqlite");
        Assert.False(OdpFile.LooksLikeOdp(text));
        Assert.Throws<InvalidDataException>(() => OdpFile.Open(text));
        Assert.Throws<FileNotFoundException>(() => OdpFile.Open(Path.Combine(_dir, "missing.osp")));

        var other = Path.Combine(_dir, "o.db");
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={other};Pooling=False"))
        {
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT); INSERT INTO meta VALUES ('format','something-else');";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => OdpFile.Open(other));
    }

    [Fact]
    public void A_project_that_is_not_json_is_refused_and_changes_nothing()
    {
        using var odp = OdpFile.Open(Copy());
        Assert.ThrowsAny<Exception>(() => odp.SetProjectDoc("not json"));
        Assert.Equal(2, odp.Revision());
    }

    /// <summary>Not a check: with ODP_WRITE_FIXTURE=path it writes the file the Rust tests read back (aad-odp tests/from_dotnet.rs).</summary>
    [Fact]
    public void Writes_the_dotnet_fixture_when_asked()
    {
        var target = Environment.GetEnvironmentVariable("ODP_WRITE_FIXTURE");
        if (string.IsNullOrEmpty(target)) return;
        if (File.Exists(target)) File.Delete(target);
        using var odp = OdpFile.Create(target, "FromDotnet");
        odp.SetDrawing("plan.dxf", "dxf", System.Text.Encoding.ASCII.GetBytes("0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n"));
        odp.SetProjectDoc("{\"project\":{\"name\":\"Grüße — house\"}}");
    }

    [Fact]
    public void A_shilpidb_file_is_imported_with_take_off_pdf_and_project()
    {
        ulong Id(int kind, ulong slot, ulong n) => ((ulong)kind << 56) | (slot << 32) | n;
        VdbRecord Rec(ulong id, string text) => new(id, 0, 0, 0, 0, System.Text.Encoding.UTF8.GetBytes(text));
        var vdb = new VdbContents();
        vdb.Records.Add(Rec(Id(1, 5, 0), "{\"format\":\"hcw-takeoff-shilpi\",\"state\":\"complete\"}"));
        vdb.Records.Add(Rec(Id(3, 5, 1), "{\"ref\":\"W1\"}"));
        vdb.Records.Add(Rec(Id(4, 5, 1), "{\"ref\":\"2A1\"}"));
        vdb.Records.Add(Rec(Id(9, 5, 1), "BBB"));
        vdb.Records.Add(Rec(Id(9, 5, 0), "AAA"));
        ShilpiProjectFile.SetProject(vdb, "{\"project\":{\"name\":\"Old\"}}");
        var vdbPath = Path.Combine(_dir, "old.vdb");
        File.WriteAllBytes(vdbPath, ShilpiProjectFile.Serialize(vdb));

        using var odp = OdpFile.Create(Path.Combine(_dir, "new.osp"), "New");
        Assert.Equal(1, odp.ImportVdb(vdbPath));
        Assert.Equal("complete", Assert.Single(odp.TakeoffHeaders()).State);
        Assert.Equal(2, odp.TakeoffRows(5).Count);
        Assert.Equal("AAABBB", System.Text.Encoding.ASCII.GetString(odp.Asset("annotated-5.pdf")!.Value.Bytes));
        Assert.Contains("Old", odp.ProjectDoc());
    }

    [Fact]
    public void Four_handles_writing_at_once_lose_nothing_and_make_one_revision_per_commit()
    {
        var path = Path.Combine(_dir, "busy.osp");
        OdpFile.Create(path, "Busy").Dispose();
        const int Writers = 4, Each = 40;
        var tasks = Enumerable.Range(0, Writers).Select(w => Task.Run(() =>
        {
            using var odp = OdpFile.Open(path);
            for (var n = 0; n < Each; n++) odp.SetTakeoffRow((uint)(w + 1), 3, (uint)n, $"{{\"writer\":{w},\"n\":{n}}}");
        })).ToArray();
        Task.WaitAll(tasks);
        using var check = OdpFile.Open(path);
        Assert.Equal(Writers * Each, check.Revision());
        for (var w = 0; w < Writers; w++) Assert.Equal(Each, check.TakeoffRows((uint)(w + 1)).Count);
        Assert.Equal(Enumerable.Range(1, Writers * Each).Select(i => (long)i), check.ChangesSince(0).Select(c => c.Revision));
    }

    /// <summary>Not a check: with ODP_STRESS_FILE=path it writes 200 revisions, to run beside the Rust example <c>stress</c> on the same file.</summary>
    [Fact]
    public void Stress_writes_when_asked()
    {
        var target = Environment.GetEnvironmentVariable("ODP_STRESS_FILE");
        if (string.IsNullOrEmpty(target)) return;
        using var odp = File.Exists(target) ? OdpFile.Open(target) : OdpFile.Create(target, "Stress");
        for (var n = 0; n < 200; n++) odp.SetTakeoffRow(2, 3, (uint)n, $"{{\"from\":\"dotnet\",\"n\":{n}}}");
    }
}
