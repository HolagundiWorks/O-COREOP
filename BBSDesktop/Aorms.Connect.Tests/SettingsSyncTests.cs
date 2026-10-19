// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Net;
using System.Text.Json.Nodes;
using Aorms.Connect;
using Xunit;

namespace Aorms.Connect.Tests;

/// <summary>Defects found in the 2026-10-19 debug pass: settings that belong to one computer must stay there, and settings changed elsewhere must come in.</summary>
public class SettingsSyncTests
{
    const string Cfg = """{"contract":"2026-10-aqc-1","supabaseUrl":"https://hub.example","supabaseAnonKey":"k","limits":{"rowBatch":500,"maxRowsPerProject":20000,"maxFileBytes":26214400,"leaseSeconds":120}}""";
    const string Login = """{"accessToken":"a","refreshToken":"r","expiresAt":4000000000,"userId":"u1","email":"a@x.in"}""";
    const string Sess = """{"contract":"2026-10-aqc-1","sessionId":"s1","account":{"id":"u1","email":"a@x.in"},"studio":{"firmId":"f1","name":"A","publicId":"S1"},"role":"OWNER","capabilities":{"write":true,"fees:manage":true,"cost:approve":false},"entitlement":{"connected":true,"plan":"PROFESSIONAL","expiresAt":null}}""";

    static async Task<ConnectClient> Client(FakeServer s)
    {
        s.On("GET api/aqc/v1/config", HttpStatusCode.OK, Cfg).On("POST api/aqc/v1/auth/login", HttpStatusCode.OK, Login).On("POST api/aqc/v1/session", HttpStatusCode.OK, Sess);
        var c = new ConnectClient(new Uri("https://aorms.example/"), new InMemoryTokenStore(), s);
        await c.SignInAsync("a@x.in", "pw", "t");
        return c;
    }

    static JsonObject DocWithLocalPaths() => new()
    {
        ["format"] = "bbsproj", ["version"] = 17, ["name"] = "P",
        ["project"] = new JsonObject
        {
            ["name"] = "P", ["client_name"] = "K",
            ["logo_path"] = @"C:\Users\someone\logo.png",
            ["takeoff_vdb_path"] = @"D:\take-offs\p.vdb",
            ["aorms_head_seq"] = 41,
            ["annotated_drawings"] = new JsonArray(new JsonObject { ["file"] = @"C:\Users\someone\a.pdf" }),
            ["aorms_aqc_id"] = "q1",
        },
        ["levels"] = new JsonArray(new JsonObject { ["id"] = "Lvl0", ["name"] = "Plinth" }),
        ["columns"] = new JsonArray(),
    };

    [Fact]
    public void SyncSettings_removes_only_the_keys_that_belong_to_this_computer_and_leaves_the_original_alone()
    {
        var settings = ProjectMapper.ToSync(DocWithLocalPaths()).Settings;
        var clean = ProjectMapper.SyncSettings(settings);
        var p = (JsonObject)clean["project"]!;
        foreach (var k in ProjectMapper.LocalOnlyProjectKeys) Assert.False(p.ContainsKey(k), k);
        Assert.Equal("K", (string)p["client_name"]!);
        Assert.Equal("q1", (string)p["aorms_aqc_id"]!);            // the binding id is the same for everyone
        Assert.NotNull(clean["levels"]);
        Assert.True(((JsonObject)settings["project"]!).ContainsKey("takeoff_vdb_path"));   // the input is untouched
    }

    [Fact]
    public void MergeSettings_takes_the_servers_settings_and_puts_this_computers_paths_back()
    {
        var local = ProjectMapper.ToSync(DocWithLocalPaths()).Settings;
        var server = new JsonObject
        {
            ["project"] = new JsonObject { ["name"] = "P renamed", ["takeoff_vdb_path"] = @"E:\someone-elses.vdb" },   // a stray path must not win either
            ["levels"] = new JsonArray(new JsonObject { ["id"] = "Lvl0", ["name"] = "Plinth" }, new JsonObject { ["id"] = "Lvl1", ["name"] = "Ground" }),
        };
        var merged = ProjectMapper.MergeSettings(local, server);
        Assert.Equal("P renamed", (string)merged["project"]!["name"]!);
        Assert.Equal(@"D:\take-offs\p.vdb", (string)merged["project"]!["takeoff_vdb_path"]!);
        Assert.Equal(41, (int)merged["project"]!["aorms_head_seq"]!);
        Assert.Equal(2, ((JsonArray)merged["levels"]!).Count);
    }

    [Fact]
    public async Task Pushing_a_project_online_never_sends_local_paths()
    {
        var s = new FakeServer().On("POST api/aqc/v1/projects", HttpStatusCode.Created, """{"aqcProjectId":"q1","headSeq":3,"rows":0}""");
        var api = new ProjectsApi(await Client(s));
        await api.PushOnlineAsync("po1", DocWithLocalPaths());
        var body = s.Requests.Last(r => r.Method == "POST" && r.Path == "api/aqc/v1/projects").Body;
        Assert.DoesNotContain("takeoff_vdb_path", body);
        Assert.DoesNotContain("annotated_drawings", body);
        Assert.DoesNotContain("logo_path", body);
        Assert.DoesNotContain("aorms_head_seq", body);
        Assert.DoesNotContain("someone", body);
        Assert.Contains("client_name", body);
    }

    [Fact]
    public async Task Pushing_settings_never_sends_local_paths()
    {
        var s = new FakeServer()
            .On("POST api/aqc/v1/projects/q1/lease", HttpStatusCode.OK, """{"held":true}""")
            .On("PUT api/aqc/v1/projects/q1/settings", HttpStatusCode.OK, """{"ok":true}""");
        var sync = new ProjectSync(await Client(s), "q1", 0, new Outbox());
        await sync.PushSettingsAsync(ProjectMapper.ToSync(DocWithLocalPaths()).Settings);
        var body = s.Requests.Last(r => r.Method == "PUT").Body;
        Assert.DoesNotContain("takeoff_vdb_path", body);
        Assert.DoesNotContain("annotated_drawings", body);
        Assert.DoesNotContain("aorms_head_seq", body);
        Assert.Contains("client_name", body);
    }

    [Fact]
    public async Task A_catch_up_returns_the_settings_the_server_sent_and_keeps_the_head_until_applied()
    {
        var json = """{"rows":[{"section":"columns","row_id":"z","fields":{"n":"Z"}}],"settings":{"levels":[{"id":"Lvl0","name":"Plinth"},{"id":"Lvl1","name":"Ground"}]},"headSeq":9,"more":false}""";
        var s = new FakeServer().On("GET api/aqc/v1/projects/q1/rows", HttpStatusCode.OK, json);
        var sync = new ProjectSync(await Client(s), "q1", 4, new Outbox());
        var pull = await sync.CatchUpAsync();
        Assert.Single(pull.Rows);
        Assert.Equal(2, ((JsonArray)pull.Settings!["levels"]!).Count);
        Assert.Equal(4, sync.HeadSeq);
        sync.AdoptHead(pull.HeadSeq);
        Assert.Equal(9, sync.HeadSeq);
    }

    [Fact]
    public async Task An_empty_catch_up_moves_the_head()
    {
        var s = new FakeServer().On("GET api/aqc/v1/projects/q1/rows", HttpStatusCode.OK, """{"rows":[],"headSeq":12,"more":false}""");
        var sync = new ProjectSync(await Client(s), "q1", 4, new Outbox());
        var pull = await sync.CatchUpAsync();
        Assert.Empty(pull.Rows);
        Assert.Equal(12, sync.HeadSeq);
    }

    [Fact]
    public void ApplyDelta_with_server_settings_brings_them_in_without_losing_local_paths()
    {
        var doc = DocWithLocalPaths();
        ProjectMapper.ToSync(doc);
        var server = new JsonObject { ["levels"] = new JsonArray(new JsonObject { ["id"] = "Lvl0", ["name"] = "Plinth" }, new JsonObject { ["id"] = "Lvl1", ["name"] = "Ground" }) };
        var after = ProjectMapper.ApplyDelta(doc, Array.Empty<SyncRow>(), server);
        Assert.Equal(2, ((JsonArray)after["levels"]!).Count);
        Assert.Equal(@"D:\take-offs\p.vdb", (string)after["project"]!["takeoff_vdb_path"]!);
        Assert.Equal(@"C:\Users\someone\logo.png", (string)after["project"]!["logo_path"]!);
    }
}
