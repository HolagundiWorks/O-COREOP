// A scripted stand-in for the AORMS server, enough for AQC's sign-in, project list, push online, lease, rows and settings.
// Logs every request to a file the test script reads. Control: POST /_test/delta {"rows":[...]} queues a catch-up answer.
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

var port = int.Parse(args[0]); var logPath = args[1];
var l = new HttpListener(); l.Prefixes.Add($"http://127.0.0.1:{port}/"); l.Start();
long head = 1; JsonArray? delta = null; JsonObject? deltaSettings = null; bool bound = false;
var rowsStore = new Dictionary<string, JsonObject>();
void Log(string s) { lock (logPath) File.AppendAllText(logPath, s + "\n"); }
Console.WriteLine("fake AORMS on " + port);
while (true)
{
    var ctx = await l.GetContextAsync();
    _ = Task.Run(() =>
    {
        var req = ctx.Request; var res = ctx.Response;
        string body = new StreamReader(req.InputStream, Encoding.UTF8).ReadToEnd();
        var path = req.Url!.AbsolutePath; var key = $"{req.HttpMethod} {path}";
        string Send(int code, string json) { res.StatusCode = code; res.ContentType = "application/json"; var b = Encoding.UTF8.GetBytes(json); res.OutputStream.Write(b); res.Close(); return json; }
        try
        {
            if (key == "GET /api/aqc/v1/config") Send(200, new JsonObject { ["contract"] = "2026-10-aqc-1", ["supabaseUrl"] = $"http://127.0.0.1:{port}", ["supabaseAnonKey"] = "anon",
                ["limits"] = new JsonObject { ["rowBatch"] = 500, ["maxRowsPerProject"] = 20000, ["maxFileBytes"] = 26214400, ["leaseSeconds"] = 120 } }.ToJsonString());
            else if (key == "POST /api/aqc/v1/auth/login")
            {
                var j = JsonNode.Parse(body)!;
                if ((string)j["password"]! != "test-password") { Send(401, """{"error":{"code":"invalid_credentials","message":"Wrong email or password."}}"""); Log("LOGIN refused"); }
                else { Send(200, """{"accessToken":"acc","refreshToken":"ref","expiresAt":4000000000,"userId":"u1","email":"tester@example.test"}"""); Log("LOGIN ok"); }
            }
            else if (key == "POST /api/aqc/v1/session") { Send(200, """{"contract":"2026-10-aqc-1","sessionId":"s1","account":{"id":"u1","email":"tester@example.test"},"studio":{"firmId":"f1","name":"Fake Studio","publicId":"S1"},"role":"OWNER","capabilities":{"write":true,"fees:manage":true,"cost:approve":false},"entitlement":{"connected":true,"plan":"PROFESSIONAL","expiresAt":null}}"""); Log("SESSION"); }
            else if (key == "GET /api/aqc/v1/projects")
                Send(200, bound
                    ? """{"projects":[{"projectOfficeId":"po-1","ref":"P-001","title":"Fake House","status":"ACTIVE","city":"Hospet","aqc":{"id":"q1","headSeq":1,"formatVersion":17,"editedByOther":false,"updatedAt":"2026-10-19T00:00:00Z"}}]}"""
                    : """{"projects":[{"projectOfficeId":"po-1","ref":"P-001","title":"Fake House","status":"ACTIVE","city":"Hospet"}]}""");
            else if (key == "POST /api/aqc/v1/projects")
            {
                var j = JsonNode.Parse(body)!; bound = true;
                var rows = j["rows"]!.AsArray();
                foreach (var r in rows) rowsStore[(string)r!["section"]! + "/" + (string)r["row_id"]!] = (JsonObject)r["fields"]!.DeepClone();
                head = 2; Log($"PUSH ONLINE rows={rows.Count} settingsKeys={((JsonObject)j["settings"]!).Count} hasLocalPath={body.Contains("takeoff_vdb_path") || body.Contains("annotated_drawings") || body.Contains("aorms_head_seq")}");
                Send(201, $$"""{"aqcProjectId":"q1","headSeq":{{head}},"rows":{{rows.Count}}}""");
            }
            else if (key == "POST /api/aqc/v1/projects/q1/lease") { Send(200, """{"held":true}"""); Log("LEASE"); }
            else if (key == "POST /api/aqc/v1/projects/q1/rows")
            {
                var rows = JsonNode.Parse(body)!["rows"]!.AsArray(); head++;
                foreach (var r in rows) { var k = (string)r!["section"]! + "/" + (string)r["row_id"]!; if (r["deleted"] is not null) rowsStore.Remove(k); else rowsStore[k] = (JsonObject)r["fields"]!.DeepClone(); }
                Log($"ROWS {rows.Count} :: " + string.Join(", ", rows.Select(r => $"{(string)r!["section"]!}{(r["deleted"] is not null ? " DELETE" : "")} {(r["fields"] as JsonObject)?["length"]}")));
                foreach (var r in rows) Log("   ROWBODY " + (string)r!["section"]! + " " + (string)r["row_id"]! + " " + (r["deleted"] is not null ? "DELETED" : r["fields"]!.ToJsonString()));
                Send(200, $$"""{"headSeq":{{head}}}""");
            }
            else if (key == "PUT /api/aqc/v1/projects/q1/settings") { Log($"SETTINGS localPath={body.Contains("takeoff_vdb_path") || body.Contains("annotated_drawings") || body.Contains("aorms_head_seq")}"); Send(200, """{"ok":true}"""); }
            else if (key == "GET /api/aqc/v1/projects/q1/rows")
            {
                var since = long.Parse(req.QueryString["since"] ?? "0");
                var rowsJson = delta ?? new JsonArray(); var s = deltaSettings;
                if (delta is not null) head++;
                var o = new JsonObject { ["rows"] = rowsJson.DeepClone(), ["headSeq"] = head, ["more"] = false };
                if (s is not null) o["settings"] = s.DeepClone();
                delta = null; deltaSettings = null;
                Log($"CATCHUP since={since} -> {rowsJson.Count} rows");
                Send(200, o.ToJsonString());
            }
            else if (key == "GET /_test/keys") Send(200, new JsonArray(rowsStore.Select(k => (JsonNode?)new JsonObject { ["key"] = k.Key, ["fields"] = k.Value.DeepClone() }).ToArray()).ToJsonString());
            else if (key == "POST /_test/delta") { var j = JsonNode.Parse(body)!; delta = (JsonArray)j["rows"]!.DeepClone(); deltaSettings = j["settings"] as JsonObject; Send(200, "{}"); }
            else if (key == "GET /_test/state") Send(200, new JsonObject { ["bound"] = bound, ["head"] = head, ["rows"] = rowsStore.Count, ["lengths"] = new JsonArray(rowsStore.Where(k => k.Key.StartsWith("masonry/")).Select(k => (JsonNode?)JsonValue.Create(k.Value["length"]?.ToString())).ToArray()) }.ToJsonString());
            else { Log("UNHANDLED " + key); Send(404, """{"error":{"code":"not_found","message":"nope"}}"""); }
        }
        catch (Exception e) { Log("ERROR " + key + " " + e.Message); try { Send(500, """{"error":{"code":"unavailable","message":"boom"}}"""); } catch { } }
    });
}
