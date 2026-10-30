using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Aqc.CadImport;
using Xunit;

namespace AQC.CadImport.Tests;

public class ShilpiTakeoffTests
{
    static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", n));

    /// <summary>What O-Draft is asked to write: a sample bridge file split into one record per row plus a header, in the given drawing slot.</summary>
    internal static List<ShilpiRecord> RecordsFromFixture(string name = "small-house.json", uint? slot = null, string? state = null,
        string? projectOfficeId = null, JsonObject? extraHeader = null, JsonObject? projectRef = null)
    {
        var root = (JsonObject)JsonNode.Parse(Fixture(name))!;
        var source = (JsonObject)root["source"]!.DeepClone();
        if (projectOfficeId is not null) source["project_office_id"] = projectOfficeId;
        if (projectRef is not null) source["project_ref"] = projectRef.DeepClone();
        var s = slot ?? ShilpiTakeoff.SlotFor((string)source["drawing_id"]!);
        var header = new JsonObject { ["format"] = ShilpiTakeoff.HeaderFormat, ["version"] = 1, ["units"] = "mm", ["state"] = state ?? "complete", ["source"] = source };
        foreach (var k in new[] { "project", "levels", "beam_depths_mm", "opening_schedule" })
            if (root[k] is { } n) header[k] = n.DeepClone();
        if (extraHeader is not null) foreach (var kv in extraHeader) header[kv.Key] = kv.Value?.DeepClone();
        var list = new List<ShilpiRecord> { new(ShilpiTakeoff.HeaderId(s), 0, 0, 0, 0, header.ToJsonString()) };
        var kinds = new Dictionary<string, byte>
        {
            ["walls"] = ShilpiTakeoff.KindWall, ["openings"] = ShilpiTakeoff.KindOpening, ["columns"] = ShilpiTakeoff.KindColumn,
            ["lintels"] = ShilpiTakeoff.KindLintel, ["rooms"] = ShilpiTakeoff.KindRoom, ["slabs"] = ShilpiTakeoff.KindSlab,
        };
        uint n2 = 1;
        foreach (var (section, kind) in kinds)
            if (root[section] is JsonArray arr)
                foreach (var row in arr)
                    list.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(kind, s, n2++), 0, 0, 1000, 1000, row!.ToJsonString()));
        return list;
    }

    static byte[] FakePdf(int size = 5000)
    {
        var b = new byte[size];
        new Random(7).NextBytes(b);
        Encoding.ASCII.GetBytes("%PDF-1.7\n").CopyTo(b, 0);
        return b;
    }

    /// <summary>Annotated PDF as chunk records plus the header descriptor O-Draft must write.</summary>
    internal static (List<ShilpiRecord> records, byte[] pdf) WithPdf(uint slot, int chunkBytes = 1024, int size = 5000, string? sha = null)
    {
        var pdf = FakePdf(size);
        var chunks = (pdf.Length + chunkBytes - 1) / chunkBytes;
        var desc = new JsonObject
        {
            ["annotated_pdf"] = new JsonObject
            {
                ["sha256"] = sha ?? Convert.ToHexString(SHA256.HashData(pdf)), ["bytes"] = pdf.Length, ["chunks"] = chunks, ["pages"] = 1,
                ["generated"] = "2026-10-19T12:00:00Z",
            },
        };
        var recs = RecordsFromFixture(slot: slot, extraHeader: desc);
        for (int i = 0; i < chunks; i++)
        {
            var part = pdf.Skip(i * chunkBytes).Take(chunkBytes).ToArray();
            recs.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindAnnotatedChunk, slot, (uint)i), 0, 0, 0, 0, null, Convert.ToHexString(part)));
        }
        return (recs, pdf);
    }

    static ImportModel EmptyModel()
    {
        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        m.Project["name"] = "Untitled Project";
        return m;
    }

    // ---- ids and slots ----

    [Fact]
    public void Id_carries_kind_slot_and_n()
    {
        var id = ShilpiTakeoff.MakeId(ShilpiTakeoff.KindWall, 0xABCDEF, 0x2A1);
        Assert.Equal(ShilpiTakeoff.KindWall, ShilpiTakeoff.KindOf(id));
        Assert.Equal(0xABCDEFu, ShilpiTakeoff.SlotOf(id));
        Assert.Equal(0x2A1u, ShilpiTakeoff.NOf(id));
        Assert.Equal(ShilpiTakeoff.KindHeader, ShilpiTakeoff.KindOf(ShilpiTakeoff.HeaderId(5)));
        Assert.Equal(0u, ShilpiTakeoff.NOf(ShilpiTakeoff.HeaderId(5)));
        Assert.Equal(ulong.MaxValue, ShilpiTakeoff.MakeId(255, 0xFFFFFF, uint.MaxValue));
    }

    [Theory]   // published test vectors: O-Draft's SlotFor must give these
    [InlineData("0b7d9c34-1e6f-4f0b-8a13-9b2c7d5e41aa")]
    [InlineData("6f1c1b0e-7a52-4d0a-9c1e-2d8a3f4b5c60")]
    [InlineData("a")]
    public void Slot_is_fnv1a_folded_to_24_bits_and_never_zero(string drawingId)
    {
        uint h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(drawingId)) { h ^= b; h *= 16777619; }
        var expected = (h ^ (h >> 24)) & 0xFFFFFF;
        Assert.Equal(expected == 0 ? 1u : expected, ShilpiTakeoff.SlotFor(drawingId));
        Assert.InRange(ShilpiTakeoff.SlotFor(drawingId), 1u, 0xFFFFFFu);
    }

    [Fact]
    public void Slot_matches_an_independent_64_bit_implementation()
    {
        // FNV-1a 32-bit written separately with 64-bit arithmetic, so the two can only agree if the algorithm in the contract is what runs
        static uint Reference(string s)
        {
            ulong h = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h = (h * 16777619UL) & 0xFFFFFFFFUL; }
            var f = (uint)((h ^ (h >> 24)) & 0xFFFFFF);
            return f == 0 ? 1u : f;
        }
        foreach (var id in new[] { "a", "", "0b7d9c34-1e6f-4f0b-8a13-9b2c7d5e41aa", "6f1c1b0e-7a52-4d0a-9c1e-2d8a3f4b5c60", "drawing é ü 1" })
            Assert.Equal(Reference(id), ShilpiTakeoff.SlotFor(id));
        // published vectors (docs/TAKEOFF-SHILPIDB.md): a writer in another language must produce these
        Assert.Equal(6389047u, ShilpiTakeoff.SlotFor("0b7d9c34-1e6f-4f0b-8a13-9b2c7d5e41aa"));
        Assert.Equal(15751439u, ShilpiTakeoff.SlotFor("6f1c1b0e-7a52-4d0a-9c1e-2d8a3f4b5c60"));
    }

    [Fact]
    public void Next_slot_wraps_and_skips_zero() => Assert.Equal(1u, ShilpiTakeoff.NextSlot(0xFFFFFF));

    // ---- one drawing ----

    [Fact]
    public void Records_import_exactly_like_the_file_does()
    {
        var fromFile = EmptyModel();
        CadImporter.Apply(CadImporter.Plan(BridgeFile.Parse(Fixture("small-house.json")), fromFile), fromFile);

        var fromDb = EmptyModel();
        var result = ShilpiTakeoff.Read(RecordsFromFixture());
        CadImporter.Apply(CadImporter.Plan(result.File, fromDb), fromDb);

        Assert.Empty(result.Skipped);
        foreach (var sheet in new[] { "masonry", "masonry_openings", "doors", "windows", "columns", "lintels", "flooring", "slabs" })
            Assert.Equal(fromFile.Sheet(sheet).Count, fromDb.Sheet(sheet).Count);
        Assert.Equal(3, fromDb.Sheet("masonry").Count);
        Assert.Equal(fromFile.Project["name"], fromDb.Project["name"]);
        Assert.All(fromDb.Sheet("masonry"), r => Assert.Equal(fromFile.Sheet("masonry")[0]["source"], r["source"]));
    }

    [Fact]
    public void A_file_import_then_a_shilpidb_import_of_the_same_drawing_changes_nothing()
    {
        var m = EmptyModel();
        CadImporter.Apply(CadImporter.Plan(BridgeFile.Parse(Fixture("small-house.json")), m), m);
        var plan = CadImporter.Plan(ShilpiTakeoff.Read(RecordsFromFixture()).File, m);
        Assert.Equal(0, plan.Count(PlanAction.Add));
        Assert.Equal(0, plan.Count(PlanAction.Update));
    }

    [Fact]
    public void Record_order_does_not_matter()
    {
        var recs = RecordsFromFixture();
        recs.Reverse();
        Assert.Equal(3, ShilpiTakeoff.Read(recs).File.Walls.Count);
    }

    [Fact]
    public void Missing_header_is_refused()
    {
        var recs = RecordsFromFixture().Where(r => ShilpiTakeoff.KindOf(r.Id) != ShilpiTakeoff.KindHeader).ToList();
        Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.Read(recs));
    }

    [Theory]
    [InlineData("\"version\":1", "\"version\":2")]
    [InlineData("\"units\":\"mm\"", "\"units\":\"m\"")]
    [InlineData("hcw-takeoff-shilpi", "something-else")]
    public void Wrong_version_units_or_format_is_refused_and_listed_with_the_problem(string from, string to)
    {
        var recs = RecordsFromFixture();
        recs[0] = recs[0] with { PayloadText = recs[0].PayloadText!.Replace(from, to) };
        Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.Read(recs));
        var listed = Assert.Single(ShilpiTakeoff.ListDrawings(recs));
        Assert.NotNull(listed.Problem);
    }

    [Fact]
    public void Unknown_kinds_and_bad_payloads_are_reported_not_fatal()
    {
        var recs = RecordsFromFixture();
        var slot = ShilpiTakeoff.SlotOf(recs[0].Id);
        recs.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(99, slot, 1), 0, 0, 1, 1, "{}"));
        recs.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindWall, slot, 900), 0, 0, 1, 1, "not json"));
        recs.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindWall, slot, 901), 0, 0, 1, 1, null));
        var r = ShilpiTakeoff.Read(recs);
        Assert.Equal(3, r.Skipped.Count);
        Assert.Equal(3, r.File.Walls.Count);
    }

    [Fact]
    public void A_take_off_still_being_recorded_is_refused_until_complete()
    {
        var recs = RecordsFromFixture(state: "recording");
        var d = Assert.Single(ShilpiTakeoff.ListDrawings(recs));
        Assert.False(d.IsComplete);
        var ex = Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.Read(recs));
        Assert.Contains("still being recorded", ex.Message);
    }

    [Fact]
    public void A_header_without_a_state_counts_as_complete() =>
        Assert.True(Assert.Single(ShilpiTakeoff.ListDrawings(RecordsFromFixture(extraHeader: new JsonObject { ["state"] = null }))).IsComplete);

    [Fact]
    public void A_drawing_linked_to_a_project_in_the_company_file_names_it()
    {
        var linked = Assert.Single(ShilpiTakeoff.ListDrawings(RecordsFromFixture(projectRef: new JsonObject { ["v"] = 1, ["company_uuid"] = "u-1", ["project_id"] = 7 })));
        Assert.Equal(("u-1", 7L), (linked.ProjectRefCompany, linked.ProjectRefId));

        var unlinked = Assert.Single(ShilpiTakeoff.ListDrawings(RecordsFromFixture()));
        Assert.Equal((null, null), (unlinked.ProjectRefCompany, unlinked.ProjectRefId));

        var odd = Assert.Single(ShilpiTakeoff.ListDrawings(RecordsFromFixture(projectRef: new JsonObject { ["company_uuid"] = "u-2", ["project_id"] = "not a number" })));
        Assert.Equal(("u-2", null), (odd.ProjectRefCompany, odd.ProjectRefId));
    }

    // ---- several drawings, one database ----

    static List<ShilpiRecord> TwoDrawings(string? projectA = "proj-A", string? projectB = "proj-B")
    {
        var a = RecordsFromFixture("small-house.json", projectOfficeId: projectA);
        var b = RecordsFromFixture("levels-only.json", projectOfficeId: projectB);
        return a.Concat(b).ToList();
    }

    [Fact]
    public void Electrical_board_and_circuit_kinds_read_and_import_onto_sheets()
    {
        const uint slot = 42;
        var header = new JsonObject
        {
            ["format"] = ShilpiTakeoff.HeaderFormat, ["version"] = 1, ["units"] = "mm", ["state"] = "complete",
            ["source"] = new JsonObject { ["drawing_id"] = "elec-demo", ["drawing"] = "elec.dwg" },
            ["levels"] = new JsonArray { new JsonObject { ["name"] = "Ground", ["height_mm"] = 3150 } },
        };
        var board = new JsonObject
        {
            ["ref"] = "EBabc", ["switchboard"] = "SB-01", ["level"] = "Ground", ["height"] = null,
            ["5amp_socket"] = 2, ["15amp_socket"] = 1, ["connected_fixture"] = 3,
            ["two_way_switches"] = 0, ["one_way_switches"] = 1,
        };
        var circuit = new JsonObject
        {
            ["ref"] = "ECdef", ["switchboard"] = "SB-01", ["point"] = "LP-01", ["kind"] = "LP", ["level"] = "Ground",
            ["height"] = null, ["5amp_socket"] = 0, ["15amp_socket"] = 0, ["connected_fixture"] = 1,
            ["two_way_switches"] = 0, ["one_way_switches"] = 0,
        };
        var recs = new List<ShilpiRecord>
        {
            new(ShilpiTakeoff.HeaderId(slot), 0, 0, 0, 0, header.ToJsonString()),
            new(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindElectricalBoard, slot, 1), 0, 0, 0, 0, board.ToJsonString()),
            new(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindElectricalCircuit, slot, 2), 0, 0, 0, 0, circuit.ToJsonString()),
        };
        var read = ShilpiTakeoff.Read(recs, slot);
        Assert.Single(read.File.Electrical);
        Assert.Single(read.File.ElectricalCircuits);
        Assert.Equal(2, read.File.Electrical[0]!["5amp_socket"]!.GetValue<int>());

        var m = EmptyModel();
        // Map the file's Ground floor onto the model's only storey.
        m.Levels[0].Name = "Ground";
        var plan = CadImporter.Plan(read.File, m);
        CadImporter.Apply(plan, m);
        Assert.Single(m.Sheet("electrical"));
        Assert.Single(m.Sheet("electrical_circuits"));
        Assert.Equal("SB-01", m.Sheet("electrical")[0]["switchboard"]);
        Assert.Equal("2", m.Sheet("electrical")[0]["five_amp"]);
        Assert.Equal("LP-01", m.Sheet("electrical_circuits")[0]["point"]);
        // Second pass is idempotent on source_ref.
        var again = CadImporter.Plan(read.File, m);
        Assert.Equal(0, again.Count(PlanAction.Add));
    }

    [Fact]
    public void Openings_that_already_name_their_wall_are_not_reassigned_by_position()
    {
        var read = ShilpiTakeoff.Read(RecordsFromFixture());
        Assert.Equal(5, read.File.Openings.Count);
        Assert.All(read.File.Openings, o => Assert.False(string.IsNullOrEmpty((string?)o["wall_ref"])));
        Assert.DoesNotContain(read.Notes ?? [], n => n.Contains("placed in wall", StringComparison.OrdinalIgnoreCase));
        Assert.All(read.File.Openings, o => Assert.Null(o["_note"]));

        var m = EmptyModel();
        CadImporter.Apply(CadImporter.Plan(read.File, m), m);
        Assert.Equal(5, m.Sheet("masonry_openings").Count);
        Assert.All(m.Sheet("masonry_openings"), r => Assert.False(string.IsNullOrEmpty(r.GetValueOrDefault("wall_mark"))));
        // Door and window schedule rows upsert on source_ref; a second pass adds nothing.
        Assert.Equal(0, CadImporter.Plan(read.File, m).Count(PlanAction.Add));
    }

    [Fact]
    public void Drawings_in_one_database_are_listed_separately_with_their_counts()
    {
        var list = ShilpiTakeoff.ListDrawings(TwoDrawings());
        Assert.Equal(2, list.Count);
        var house = list.Single(d => d.Drawing == "small house.dwg");
        Assert.Equal("proj-A", house.ProjectOfficeId);
        Assert.Equal(3, house.Counts[ShilpiTakeoff.KindWall]);
        Assert.Equal(5, house.Counts[ShilpiTakeoff.KindOpening]);
        Assert.Equal(14, house.Rows);
        Assert.Equal("proj-B", list.Single(d => d.Drawing == "building permission.dwg").ProjectOfficeId);
    }

    [Fact]
    public void Reading_one_drawing_never_mixes_in_the_other()
    {
        var recs = TwoDrawings();
        var house = ShilpiTakeoff.ListDrawings(recs).Single(d => d.Drawing == "small house.dwg");
        var r = ShilpiTakeoff.Read(recs, house.Slot);
        Assert.Equal(3, r.File.Walls.Count);
        Assert.Equal("small house.dwg", r.File.Drawing);
        Assert.DoesNotContain(r.File.Levels, l => l.Name == "First");   // levels-only.json's floors belong to the other drawing
    }

    [Fact]
    public void Reading_without_a_slot_when_there_are_several_drawings_asks_to_choose()
    {
        var ex = Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.Read(TwoDrawings()));
        Assert.Contains("choose one", ex.Message);
    }

    [Fact]
    public void Two_drawings_imported_in_turn_keep_their_rows_apart_and_do_not_duplicate_on_a_second_pass()
    {
        var recs = TwoDrawings();
        var m = EmptyModel();
        foreach (var d in ShilpiTakeoff.ListDrawings(recs))
            CadImporter.Apply(CadImporter.Plan(ShilpiTakeoff.Read(recs, d.Slot).File, m), m);
        var tags = m.Sheet("masonry").Select(r => r["source"]).Distinct().ToList();
        Assert.Single(tags);   // only the small house has walls
        Assert.Equal(3, m.Sheet("masonry").Count);

        var again = CadImporter.Plan(ShilpiTakeoff.Read(recs, ShilpiTakeoff.ListDrawings(recs).First(d => d.Drawing == "small house.dwg").Slot).File, m);
        Assert.Equal(0, again.Count(PlanAction.Add));
    }

    // ---- the annotated drawing ----

    [Fact]
    public void Annotated_pdf_is_reassembled_and_verified()
    {
        var (recs, pdf) = WithPdf(slot: 42);
        var d = Assert.Single(ShilpiTakeoff.ListDrawings(recs));
        Assert.Equal(pdf.Length, d.Annotated!.Bytes);
        Assert.Equal(5, d.Annotated.Chunks);
        Assert.Equal(pdf, ShilpiTakeoff.ReadAnnotatedPdf(recs, 42));
        Assert.Equal(14, d.Rows);   // chunks are not counted as take-off rows
    }

    [Fact]
    public void Chunks_are_not_part_of_the_take_off_and_can_be_skipped_when_reading()
    {
        var (recs, _) = WithPdf(slot: 42);
        Assert.False(ShilpiTakeoff.IsTakeoffRecord(recs.Last().Id));
        var result = ShilpiTakeoff.Read(recs.Where(r => ShilpiTakeoff.IsTakeoffRecord(r.Id)));
        Assert.Equal(3, result.File.Walls.Count);
    }

    [Fact]
    public void A_missing_or_changed_chunk_or_wrong_checksum_is_refused()
    {
        var (recs, _) = WithPdf(slot: 42);
        Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.ReadAnnotatedPdf(recs.Where(r => !(ShilpiTakeoff.KindOf(r.Id) == ShilpiTakeoff.KindAnnotatedChunk && ShilpiTakeoff.NOf(r.Id) == 2)), 42));

        var tampered = recs.Select(r => ShilpiTakeoff.KindOf(r.Id) == ShilpiTakeoff.KindAnnotatedChunk && ShilpiTakeoff.NOf(r.Id) == 1
            ? r with { PayloadHex = "00" + r.PayloadHex![2..] } : r).ToList();
        var ex = Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.ReadAnnotatedPdf(tampered, 42));
        Assert.Contains("checksum", ex.Message);

        var (badSha, _) = WithPdf(slot: 42, sha: new string('0', 64));
        Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.ReadAnnotatedPdf(badSha, 42));
    }

    [Fact]
    public void A_drawing_with_no_annotated_pdf_says_so()
    {
        var ex = Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.ReadAnnotatedPdf(RecordsFromFixture(slot: 7), 7));
        Assert.Contains("no annotated drawing", ex.Message);
    }

    [Fact]
    public void Something_that_is_not_a_pdf_is_refused()
    {
        var notPdf = new byte[100];
        var chunk = new ShilpiRecord(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindAnnotatedChunk, 9, 0), 0, 0, 0, 0, null, Convert.ToHexString(notPdf));
        var desc = new JsonObject { ["annotated_pdf"] = new JsonObject { ["sha256"] = Convert.ToHexString(SHA256.HashData(notPdf)), ["bytes"] = 100, ["chunks"] = 1, ["pages"] = 1 } };
        var recs = RecordsFromFixture(slot: 9, extraHeader: desc);
        recs.Add(chunk);
        var ex = Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.ReadAnnotatedPdf(recs, 9));
        Assert.Contains("not a PDF", ex.Message);
    }
}
