// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Net;
using System.Text.Json.Nodes;
using Aorms.Connect;
using Xunit;

namespace Aorms.Connect.Tests;

/// <summary>
/// The loop the app's ProjectSyncHost runs, using the same library pieces in the same order: edit the project, work out what changed
/// (<see cref="RowBaseline"/>), put it in the file-backed <see cref="Outbox"/>, flush it under the lease, restart, and catch up
/// (<see cref="ProjectMapper.ApplyDelta"/>). Against a scripted server; nothing here touches the network.
/// </summary>
public class HostFlowTests : IDisposable
{
    const string Cfg = """{"contract":"2026-10-aqc-1","supabaseUrl":"https://hub.example","supabaseAnonKey":"k","limits":{"rowBatch":500,"maxRowsPerProject":20000,"maxFileBytes":26214400,"leaseSeconds":120}}""";
    const string Login = """{"accessToken":"a","refreshToken":"r","expiresAt":4000000000,"userId":"u1","email":"a@x.in"}""";
    const string Sess = """{"contract":"2026-10-aqc-1","sessionId":"s1","account":{"id":"u1","email":"a@x.in"},"studio":{"firmId":"f1","name":"A","publicId":"S1"},"role":"OWNER","capabilities":{"write":true,"fees:manage":true,"cost:approve":false},"entitlement":{"connected":true,"plan":"PROFESSIONAL","expiresAt":null}}""";
    const string Rows = "POST api/aqc/v1/projects/q1/rows";
    const string Lease = "POST api/aqc/v1/projects/q1/lease";

    readonly string _dir = Path.Combine(Path.GetTempPath(), "aqc-host-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* temp */ } }

    sealed class Clock { public DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero); public void Add(double s) => Now = Now.AddSeconds(s); }

    static JsonObject Pilot() => (JsonObject)JsonNode.Parse(File.ReadAllText("pilot-sample.json"))!;

    async Task<(ProjectSync sync, FakeServer server, Clock clock, RowBaseline baseline)> Open(FakeServer s, JsonObject doc, long head = 5)
    {
        s.On("GET api/aqc/v1/config", HttpStatusCode.OK, Cfg).On("POST api/aqc/v1/auth/login", HttpStatusCode.OK, Login).On("POST api/aqc/v1/session", HttpStatusCode.OK, Sess);
        var c = new ConnectClient(new Uri("https://aorms.example/"), new InMemoryTokenStore(), s);
        await c.SignInAsync("a@x.in", "pw", "t");
        var clock = new Clock();
        var outbox = new Outbox(new FileOutboxStore(Path.Combine(_dir, "q1.json")));
        var baseline = new RowBaseline(Path.Combine(_dir, "q1.base.json"));
        if (!baseline.Loaded) baseline.Reset(ProjectMapper.ToSync(doc).Rows);
        return (new ProjectSync(c, "q1", head, outbox, () => clock.Now), s, clock, baseline);
    }

    /// <summary>What the host's Enqueue does.</summary>
    static void Enqueue(ProjectSync sync, RowBaseline baseline, JsonObject doc)
    {
        var (up, del) = baseline.Diff(ProjectMapper.ToSync(doc).Rows);
        if (up.Count == 0 && del.Count == 0) return;
        sync.Outbox.UpsertMany(up.Concat(del));
        sync.NoteChange();
    }

    static JsonObject FirstRow(JsonObject doc, string section) => (JsonObject)((JsonArray)doc[section]!)[0]!;

    static List<string> SentRowIds(FakeServer s) =>
        s.Requests.Where(r => r.Method == "POST" && r.Path.EndsWith("/rows"))
            .SelectMany(r => JsonNode.Parse(r.Body)!["rows"]!.AsArray().Select(x => (string)x!["row_id"]!)).ToList();

    [Fact]
    public async Task One_edit_sends_exactly_one_row_and_nothing_is_left_over()
    {
        var doc = Pilot();
        ProjectMapper.ToSync(doc);   // mint ids, as MintRowIds does
        var (sync, server, clock, baseline) = await Open(new FakeServer().On(Lease, HttpStatusCode.OK, """{"held":true}""").On(Rows, HttpStatusCode.OK, """{"headSeq":6}"""), doc);
        await sync.AcquireLeaseAsync();

        var row = FirstRow(doc, "masonry");
        row["length"] = "99999";
        Enqueue(sync, baseline, doc);
        clock.Add(3); await sync.TickAsync();

        Assert.Equal(new[] { (string)row["_rid"]! }, SentRowIds(server));
        Assert.Equal(0, sync.Outbox.Count);
        Assert.Equal(SyncState.Synced, sync.State);
        Assert.Equal(6, sync.HeadSeq);
        // nothing changed since: the next tick sends nothing more
        Enqueue(sync, baseline, doc);
        Assert.Equal(0, sync.Outbox.Count);
    }

    [Fact]
    public async Task A_deleted_row_is_sent_as_a_delete()
    {
        var doc = Pilot();
        ProjectMapper.ToSync(doc);
        var (sync, server, clock, baseline) = await Open(new FakeServer().On(Lease, HttpStatusCode.OK, """{"held":true}""").On(Rows, HttpStatusCode.OK, """{"headSeq":6}"""), doc);
        await sync.AcquireLeaseAsync();
        var arr = (JsonArray)doc["masonry"]!;
        var gone = (string)((JsonObject)arr[0]!)["_rid"]!;
        arr.RemoveAt(0);
        Enqueue(sync, baseline, doc);
        clock.Add(3); await sync.TickAsync();
        var sent = JsonNode.Parse(server.Requests.Last(r => r.Path.EndsWith("/rows")).Body)!["rows"]!.AsArray();
        Assert.Equal(gone, (string)sent[0]!["row_id"]!);
        Assert.True((bool)sent[0]!["deleted"]!);
    }

    [Fact]
    public async Task Edits_made_offline_survive_a_restart_and_go_on_the_next_run()
    {
        var doc = Pilot();
        ProjectMapper.ToSync(doc);
        // first run: the server is down, so the edit stays in the outbox file
        var (sync, server, clock, baseline) = await Open(new FakeServer().On(Lease, HttpStatusCode.OK, """{"held":true}""").OnThrow(Rows), doc);
        await sync.AcquireLeaseAsync();
        var row = FirstRow(doc, "columns");
        row["width"] = "777";
        Enqueue(sync, baseline, doc);
        clock.Add(3); await sync.TickAsync();
        Assert.Equal(SyncState.Offline, sync.State);
        Assert.Equal(1, sync.Outbox.Count);

        // the app is closed and reopened: a new outbox and baseline read from disk
        var (sync2, server2, clock2, baseline2) = await Open(new FakeServer().On(Lease, HttpStatusCode.OK, """{"held":true}""").On(Rows, HttpStatusCode.OK, """{"headSeq":9}"""), doc);
        Assert.True(baseline2.Loaded);
        Assert.Equal(1, sync2.Outbox.Count);
        Enqueue(sync2, baseline2, doc);          // nothing new: it was already queued
        Assert.Equal(1, sync2.Outbox.Count);
        clock2.Add(3); await sync2.TickAsync();
        Assert.Equal(new[] { (string)row["_rid"]! }, SentRowIds(server2));
        Assert.Equal(0, sync2.Outbox.Count);
    }

    [Fact]
    public async Task A_catch_up_is_applied_and_does_not_come_back_as_a_local_edit()
    {
        var doc = Pilot();
        ProjectMapper.ToSync(doc);
        var target = ProjectMapper.ToSync(doc).Rows.First(r => r.Section == "masonry");
        var theirs = (JsonObject)target.Fields.DeepClone();
        theirs["length"] = "12345";
        var wire = ProjectMapper.ToWire(new[] { target with { Fields = theirs } }).ToJsonString();
        var (sync, _, _, baseline) = await Open(new FakeServer()
            .On("GET api/aqc/v1/projects/q1/rows", HttpStatusCode.OK, $$"""{"rows":{{wire}},"headSeq":8,"more":false}"""), doc);

        var pull = await sync.CatchUpAsync();
        Assert.False(pull.NeedsReview);
        Assert.Equal(5, sync.HeadSeq);   // not advanced until the rows are applied
        sync.AdoptHead(pull.HeadSeq);
        Assert.Equal(8, sync.HeadSeq);
        var newDoc = ProjectMapper.ApplyDelta(doc, pull.Rows);
        baseline.Adopt(pull.Rows);

        var after = ProjectMapper.ToSync(newDoc).Rows.First(r => r.Section == "masonry" && r.RowId == target.RowId);
        Assert.Equal("12345", (string)after.Fields["length"]!);
        Enqueue(sync, baseline, newDoc);
        Assert.Equal(0, sync.Outbox.Count);   // their change is not mistaken for ours
    }

    [Fact]
    public async Task A_catch_up_while_edits_are_unsent_asks_for_review_and_keeps_the_head()
    {
        var doc = Pilot();
        ProjectMapper.ToSync(doc);
        var target = ProjectMapper.ToSync(doc).Rows.First(r => r.Section == "masonry");
        var wire = ProjectMapper.ToWire(new[] { target with { Fields = (JsonObject)target.Fields.DeepClone() } }).ToJsonString();
        var (sync, _, _, baseline) = await Open(new FakeServer()
            .On("GET api/aqc/v1/projects/q1/rows", HttpStatusCode.OK, $$"""{"rows":{{wire}},"headSeq":8,"more":false}"""), doc, head: 5);
        FirstRow(doc, "columns")["width"] = "1";
        Enqueue(sync, baseline, doc);
        var pull = await sync.CatchUpAsync();
        Assert.True(pull.NeedsReview);
        Assert.Equal(5, sync.HeadSeq);   // not advanced until the user decides
    }

    [Fact]
    public void Duplicated_rows_must_not_share_an_id_the_diff_would_otherwise_lose_one()
    {
        // two rows with the same _rid collapse into one key; the host's MintRowIds gives the copy a new id first
        var a = new SyncRow("columns", "same", new JsonObject { ["n"] = "C1" });
        var b = new SyncRow("columns", "same", new JsonObject { ["n"] = "C1 copy" });
        var baseline = new RowBaseline();
        var (up, _) = baseline.Diff(new[] { a, b });
        Assert.Equal(2, up.Count);                 // both are reported ...
        Assert.Equal(1, baseline.Count);           // ... but only one can be held: this is why ids must be unique per row
    }
}
