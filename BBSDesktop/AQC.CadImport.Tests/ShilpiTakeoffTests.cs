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
        string? projectOfficeId = null, JsonObject? extraHeader = null)
    {
        var root = (JsonObject)JsonNode.Parse(Fixture(name))!;
        var source = (JsonObject)root["source"]!.DeepClone();
        if (projectOfficeId is not null) source["project_office_id"] = projectOfficeId;
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

    // ---- several drawings, one database ----

    static List<ShilpiRecord> TwoDrawings(string? projectA = "proj-A", string? projectB = "proj-B")
    {
        var a = RecordsFromFixture("small-house.json", projectOfficeId: projectA);
        var b = RecordsFromFixture("levels-only.json", projectOfficeId: projectB);
        return a.Concat(b).ToList();
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
        Assert.Empty(r.File.Levels.Where(l => l.Name == "First"));   // levels-only.json's floors belong to the other drawing
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

    // ---- gateway client against a fake HTTP handler ----

    sealed class Fake : HttpMessageHandler
    {
        public readonly Dictionary<ulong, ShilpiRecord> Records = new();
        public readonly List<ulong> Fetched = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath.Trim('/');
            if (path == "health") return Task.FromResult(Json("{\"status\":\"ok\"}"));
            if (path == "records")
                return Task.FromResult(Json("{\"records\":[" + string.Join(",", Records.Keys.Select(i => $"{{\"id\":{i},\"bbox\":{{\"minX\":0,\"minY\":0,\"maxX\":1,\"maxY\":1}}}}")) + "]}"));
            if (path.StartsWith("records/") && ulong.TryParse(path[8..], out var id))
            {
                lock (Fetched) Fetched.Add(id);
                if (!Records.TryGetValue(id, out var r)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"record not found\"}") });
                var text = r.PayloadText is null ? "null" : System.Text.Json.JsonSerializer.Serialize(r.PayloadText);
                return Task.FromResult(Json($"{{\"id\":{id},\"bbox\":{{\"minX\":{r.MinX},\"minY\":{r.MinY},\"maxX\":{r.MaxX},\"maxY\":{r.MaxY}}},\"payloadHex\":\"{r.PayloadHex ?? "00"}\",\"payloadText\":{text}}}"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task Gateway_client_reads_records_including_ids_above_the_int64_range()
    {
        var fake = new Fake();
        foreach (var r in RecordsFromFixture()) fake.Records[r.Id] = r;
        fake.Records[ulong.MaxValue] = new ShilpiRecord(ulong.MaxValue, 0, 0, 1, 1, "{}");
        using var client = new ShilpiGatewayClient(new Uri("http://shilpi.test/"), fake);

        Assert.True(await client.IsUpAsync());
        var recs = await client.ReadAllAsync();
        Assert.Equal(fake.Records.Count, recs.Count);
        var result = ShilpiTakeoff.Read(recs, ShilpiTakeoff.SlotOf(recs.First(r => ShilpiTakeoff.KindOf(r.Id) == ShilpiTakeoff.KindHeader).Id));
        Assert.Equal(3, result.File.Walls.Count);
    }

    [Fact]
    public async Task Reading_the_take_off_does_not_download_the_annotated_pdf_but_asking_for_it_does()
    {
        var fake = new Fake();
        var (recs, pdf) = WithPdf(slot: 77);
        foreach (var r in recs) fake.Records[r.Id] = r;
        using var client = new ShilpiGatewayClient(new Uri("http://shilpi.test/"), fake);

        var takeoff = await client.ReadAllAsync(include: ShilpiTakeoff.IsTakeoffRecord);
        Assert.DoesNotContain(fake.Fetched, i => ShilpiTakeoff.KindOf(i) == ShilpiTakeoff.KindAnnotatedChunk);
        Assert.Equal(3, ShilpiTakeoff.Read(takeoff, 77).File.Walls.Count);

        var chunks = await client.ReadAllAsync(include: id => ShilpiTakeoff.SlotOf(id) == 77 && (ShilpiTakeoff.KindOf(id) is ShilpiTakeoff.KindHeader or ShilpiTakeoff.KindAnnotatedChunk));
        Assert.Equal(pdf, ShilpiTakeoff.ReadAnnotatedPdf(chunks, 77));
    }

    [Fact]
    public async Task A_record_deleted_between_the_list_and_the_read_is_skipped()
    {
        var fake = new Fake();
        foreach (var r in RecordsFromFixture()) fake.Records[r.Id] = r;
        var list = fake.Records.Keys.ToList();
        using var client = new ShilpiGatewayClient(new Uri("http://shilpi.test/"), new DeletingHandler(fake, list[^1]));
        Assert.Equal(list.Count - 1, (await client.ReadAllAsync()).Count);
    }

    sealed class DeletingHandler : DelegatingHandler
    {
        readonly ulong _gone;
        public DeletingHandler(HttpMessageHandler inner, ulong gone) : base(inner) => _gone = gone;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            req.RequestUri!.AbsolutePath.EndsWith("/" + _gone) ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)) : base.SendAsync(req, ct);
    }

    [Fact]
    public async Task A_down_gateway_is_not_up_and_reading_it_throws()
    {
        using var down = new ShilpiGatewayClient(new Uri("http://127.0.0.1:1/"));
        Assert.False(await down.IsUpAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => down.ReadAllAsync());
    }
}
