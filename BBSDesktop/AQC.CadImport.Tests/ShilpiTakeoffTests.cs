using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Aqc.CadImport;
using Xunit;

namespace AQC.CadImport.Tests;

public class ShilpiTakeoffTests
{
    static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", n));

    /// <summary>What O-Draft is asked to write: the sample bridge file split into one record per row plus a header.</summary>
    internal static List<ShilpiRecord> RecordsFromFixture(string name = "small-house.json")
    {
        var root = (JsonObject)JsonNode.Parse(Fixture(name))!;
        var header = new JsonObject
        {
            ["format"] = ShilpiTakeoff.HeaderFormat, ["version"] = 1, ["units"] = "mm",
            ["source"] = root["source"]!.DeepClone(),
        };
        foreach (var k in new[] { "project", "levels", "beam_depths_mm", "opening_schedule" })
            if (root[k] is { } n) header[k] = n.DeepClone();
        var list = new List<ShilpiRecord> { new(ShilpiTakeoff.HeaderId, 0, 0, 0, 0, header.ToJsonString()) };
        var kinds = new Dictionary<string, byte>
        {
            ["walls"] = ShilpiTakeoff.KindWall, ["openings"] = ShilpiTakeoff.KindOpening, ["columns"] = ShilpiTakeoff.KindColumn,
            ["lintels"] = ShilpiTakeoff.KindLintel, ["rooms"] = ShilpiTakeoff.KindRoom, ["slabs"] = ShilpiTakeoff.KindSlab,
        };
        ulong n2 = 1;
        foreach (var (section, kind) in kinds)
            if (root[section] is JsonArray arr)
                foreach (var row in arr)
                    list.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(kind, n2++), 0, 0, 1000, 1000, row!.ToJsonString()));
        return list;
    }

    static ImportModel EmptyModel()
    {
        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        m.Project["name"] = "Untitled Project";
        return m;
    }

    [Fact]
    public void Id_carries_the_kind_and_the_number()
    {
        var id = ShilpiTakeoff.MakeId(ShilpiTakeoff.KindWall, 0x2A1);
        Assert.Equal(ShilpiTakeoff.KindWall, ShilpiTakeoff.KindOf(id));
        Assert.Equal(0x2A1UL, id & 0x00FF_FFFF_FFFF_FFFFUL);
        Assert.Equal(ShilpiTakeoff.KindHeader, ShilpiTakeoff.KindOf(ShilpiTakeoff.HeaderId));
    }

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
        Assert.Equal(fromFile.Levels.Count, fromDb.Levels.Count);
        Assert.Equal(fromFile.Project["name"], fromDb.Project["name"]);
        // the same source tag, so a file import and a ShilpiDB import of one drawing never duplicate each other
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
    public void Missing_or_double_header_is_refused()
    {
        var recs = RecordsFromFixture().Where(r => ShilpiTakeoff.KindOf(r.Id) != ShilpiTakeoff.KindHeader).ToList();
        Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.Read(recs));

        var two = RecordsFromFixture();
        two.Add(two[0] with { Id = ShilpiTakeoff.MakeId(ShilpiTakeoff.KindHeader, 1) });
        Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.Read(two));
    }

    [Theory]
    [InlineData("\"version\":1", "\"version\":2")]
    [InlineData("\"units\":\"mm\"", "\"units\":\"m\"")]
    [InlineData("hcw-takeoff-shilpi", "something-else")]
    public void Wrong_version_units_or_format_is_refused(string from, string to)
    {
        var recs = RecordsFromFixture();
        recs[0] = recs[0] with { PayloadText = recs[0].PayloadText!.Replace(from, to) };
        Assert.Throws<BridgeFormatException>(() => ShilpiTakeoff.Read(recs));
    }

    [Fact]
    public void Unknown_kinds_and_bad_payloads_are_reported_not_fatal()
    {
        var recs = RecordsFromFixture();
        recs.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(99, 1), 0, 0, 1, 1, "{}"));
        recs.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindWall, 900), 0, 0, 1, 1, "not json"));
        recs.Add(new ShilpiRecord(ShilpiTakeoff.MakeId(ShilpiTakeoff.KindWall, 901), 0, 0, 1, 1, null));
        var r = ShilpiTakeoff.Read(recs);
        Assert.Equal(3, r.Skipped.Count);
        Assert.Equal(3, r.File.Walls.Count);
    }

    // ---- gateway client against a fake HTTP handler ----

    sealed class Fake : HttpMessageHandler
    {
        public readonly Dictionary<ulong, ShilpiRecord> Records = new();
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            var path = req.RequestUri!.AbsolutePath.Trim('/');
            if (path == "health") return Task.FromResult(Json("{\"status\":\"ok\"}"));
            if (path == "records")
                return Task.FromResult(Json("{\"records\":[" + string.Join(",", Records.Keys.Select(i => $"{{\"id\":{i},\"bbox\":{{\"minX\":0,\"minY\":0,\"maxX\":1,\"maxY\":1}}}}")) + "]}"));
            if (path.StartsWith("records/") && ulong.TryParse(path[8..], out var id))
            {
                if (!Records.TryGetValue(id, out var r)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"record not found\"}") });
                var text = r.PayloadText is null ? "null" : System.Text.Json.JsonSerializer.Serialize(r.PayloadText);
                return Task.FromResult(Json($"{{\"id\":{id},\"bbox\":{{\"minX\":{r.MinX},\"minY\":{r.MinY},\"maxX\":{r.MaxX},\"maxY\":{r.MaxY}}},\"payloadHex\":\"00\",\"payloadText\":{text}}}"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task Gateway_client_reads_every_record_including_ids_above_the_int64_range()
    {
        var fake = new Fake();
        foreach (var r in RecordsFromFixture()) fake.Records[r.Id] = r;
        fake.Records[ulong.MaxValue] = new ShilpiRecord(ulong.MaxValue, 0, 0, 1, 1, "{}");   // kind 255
        using var client = new ShilpiGatewayClient(new Uri("http://shilpi.test/"), fake);

        Assert.True(await client.IsUpAsync());
        var seen = new List<int>();
        var recs = await client.ReadAllAsync(new Progress<(int Done, int Total)>(p => { lock (seen) seen.Add(p.Total); }));
        Assert.Equal(fake.Records.Count, recs.Count);
        var result = ShilpiTakeoff.Read(recs);
        Assert.Equal(3, result.File.Walls.Count);
        Assert.Single(result.Skipped);   // the kind-255 record
    }

    [Fact]
    public async Task A_record_deleted_between_the_list_and_the_read_is_skipped()
    {
        var fake = new Fake();
        foreach (var r in RecordsFromFixture()) fake.Records[r.Id] = r;
        var list = fake.Records.Keys.ToList();
        using var client = new ShilpiGatewayClient(new Uri("http://shilpi.test/"), new DeletingHandler(fake, list[^1]));
        var recs = await client.ReadAllAsync();
        Assert.Equal(list.Count - 1, recs.Count);
    }

    sealed class DeletingHandler : DelegatingHandler
    {
        readonly ulong _gone;
        public DeletingHandler(HttpMessageHandler inner, ulong gone) : base(inner) => _gone = gone;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/" + _gone))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return base.SendAsync(req, ct);
        }
    }

    [Fact]
    public async Task Gateway_errors_surface_as_exceptions_and_a_down_gateway_is_not_up()
    {
        using var down = new ShilpiGatewayClient(new Uri("http://127.0.0.1:1/"));
        Assert.False(await down.IsUpAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => down.ReadAllAsync());
    }
}
