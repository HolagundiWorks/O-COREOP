using System.Text;
using Aqc.CadImport;
using Xunit;

namespace AQC.CadImport.Tests;

public class VdbFileTests
{
    static byte[] Build(uint version, IEnumerable<(ulong id, double[] box, byte[] payload)> records, byte[]? trailer = null, uint? claimedCount = null)
    {
        var list = records.ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("SHILPIDB"));
        w.Write(version);
        w.Write(claimedCount ?? (uint)list.Count);
        foreach (var (id, box, payload) in list)
        {
            w.Write(id);
            foreach (var d in box) w.Write(d);
            w.Write((uint)payload.Length);
            w.Write(payload);
        }
        if (trailer is not null) w.Write(trailer);
        return ms.ToArray();
    }

    static (ulong, double[], byte[]) Rec(ulong id, string text, double[]? box = null) => (id, box ?? new[] { 0.0, 0, 10, 20 }, Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void Versions_1_and_2_read_the_same_records(uint version)
    {
        var bytes = Build(version, new[] { Rec(72057594037927936UL, "{\"a\":1}"), Rec(266398271407128864UL, "{\"b\":\"é\"}", new[] { 1.5, -2.5, 3.5, 4.5 }) });
        var r = VdbFile.Parse(bytes);
        Assert.Equal(2, r.Count);
        Assert.Equal(72057594037927936UL, r[0].Id);
        Assert.Equal("{\"b\":\"é\"}", r[1].PayloadText);
        Assert.Equal((1.5, -2.5, 3.5, 4.5), (r[1].MinX, r[1].MinY, r[1].MaxX, r[1].MaxY));
    }

    [Fact]
    public void A_trailing_spatial_grid_after_the_records_is_ignored()
    {
        var bytes = Build(2, new[] { Rec(1, "{}") }, trailer: new byte[500]);
        Assert.Single(VdbFile.Parse(bytes));
    }

    [Fact]
    public void Binary_payloads_have_no_text_and_keep_their_bytes_as_hex_and_pdf_chunks_always_do()
    {
        var raw = new byte[] { 0x25, 0x50, 0x44, 0x46, 0xFF, 0xFE, 0x00, 0x80 };   // %PDF then bytes that are not UTF-8
        var chunkId = ShilpiTakeoff.MakeId(ShilpiTakeoff.KindAnnotatedChunk, 5, 0);
        var r = VdbFile.Parse(Build(2, new[] { (chunkId, new double[4], raw), Rec(9, "text that happens to be binary-safe") }));
        Assert.Null(r[0].PayloadText);
        Assert.Equal(Convert.ToHexString(raw), r[0].PayloadHex);
        Assert.Equal("text that happens to be binary-safe", r[1].PayloadText);
    }

    [Fact]
    public void Bad_files_are_refused_with_a_message()
    {
        Assert.Contains("not a ShilpiDB", Assert.Throws<InvalidDataException>(() => VdbFile.Parse(Encoding.ASCII.GetBytes("this is not a database at all"))).Message);
        Assert.Contains("version 3", Assert.Throws<InvalidDataException>(() => VdbFile.Parse(Build(3, Array.Empty<(ulong, double[], byte[])>()))).Message);
        Assert.Contains("ends inside", Assert.Throws<InvalidDataException>(() => VdbFile.Parse(Build(2, new[] { Rec(1, "{}") }, claimedCount: 5))).Message);
        var truncated = Build(2, new[] { Rec(1, "{\"long\":\"payload\"}") }); Array.Resize(ref truncated, truncated.Length - 6);
        Assert.Throws<InvalidDataException>(() => VdbFile.Parse(truncated));
        Assert.Throws<InvalidDataException>(() => VdbFile.Parse(Build(2, Array.Empty<(ulong, double[], byte[])>(), claimedCount: 2_000_000)));
        Assert.Throws<FileNotFoundException>(() => VdbFile.Read(Path.Combine(Path.GetTempPath(), "no-such-file.vdb")));
    }

    [Fact]
    public void An_empty_database_is_an_empty_list() => Assert.Empty(VdbFile.Parse(Build(2, Array.Empty<(ulong, double[], byte[])>())));

    // ---- the file O-Draft's own engine wrote for a parametric wall (fixtures/odraft-parametric.takeoff.vdb, version 2) ----

    static List<ShilpiRecord> ODraft() => VdbFile.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "odraft-parametric.takeoff.vdb"));

    [Fact]
    public void The_file_O_Draft_wrote_is_version_2_and_the_installed_shilpid_cannot_open_it_but_this_reader_can()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "odraft-parametric.takeoff.vdb"));
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, 8));   // shilpid 0.2.0 accepts only 1: "unsupported data file version 2"
        Assert.Equal(2, ODraft().Count);
    }

    [Fact]
    public void O_Drafts_parametric_wall_reads_imports_and_keeps_its_PW_ref_and_thickness()
    {
        var recs = ODraft();
        var d = Assert.Single(ShilpiTakeoff.ListDrawings(recs));
        Assert.True(d.IsComplete);
        // O-Draft's FILE mode writes the original one-drawing-per-file layout: slot 0, ids (kind << 56) | handle. Its server mode uses the hashed
        // slot (SlotFor). Slot 0 is accepted for a file that holds one drawing; it is not allowed on the shared server, where drawings must not collide.
        Assert.Equal(0u, d.Slot);
        Assert.Equal(ShilpiTakeoff.HeaderId(0), recs.Min(x => x.Id));
        Assert.NotEqual(0u, ShilpiTakeoff.SlotFor(d.DrawingId));
        var r = ShilpiTakeoff.Read(recs, d.Slot);
        var wall = Assert.Single(r.File.Walls);
        Assert.StartsWith("PW-", (string)wall["ref"]!);
        Assert.Equal(230, (double)wall["thickness_mm"]!);

        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        var plan = CadImporter.Plan(r.File, m);
        Assert.Equal(0, plan.Count(PlanAction.Skipped));
        CadImporter.Apply(plan, m);
        var row = Assert.Single(m.Sheet("masonry"));
        Assert.Equal("230", row["thickness"]);
        Assert.Equal("5000", row["length"]);
        Assert.Equal("3000", row["height"]);
        Assert.Equal("", plan.Items.Single(i => i.Section == "masonry").Reason);   // a stated thickness needs no "check it"
    }
}
