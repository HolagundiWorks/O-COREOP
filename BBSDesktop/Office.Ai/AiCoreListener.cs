// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Office.Ai;

/// <summary>
/// Lets O-Draft and O-CoreOp use the model the host app carries. It answers the suite's local contract on 127.0.0.1 only:
/// <c>GET /v1/health</c>, <c>POST /v1/chat</c>, and <c>POST /v1/completion</c> (Olmo prompt for CoreOp's Ask bar), behind the bearer
/// token in <c>%LOCALAPPDATA%\OSAS\ai\auth.token</c>. The model loads on the first request. O-Studio keeps this open while a workspace is signed in.
/// </summary>
public sealed class AiCoreListener : IDisposable
{
    public const int DefaultPort = 8742;
    const int MaxBody = 512 * 1024;

    readonly HttpListener _http = new();
    readonly CancellationTokenSource _stop = new();
    readonly SemaphoreSlim _one = new(1, 1);       // one answer at a time; the model has one slot
    readonly string _token;

    AiCoreListener(string token) { _token = token; }

    public static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OSAS", "ai", "auth.token");

    /// <summary>Start listening, or return null when the port is taken (another AI Core is already serving) or cannot be opened.</summary>
    public static AiCoreListener? TryStart(int port = DefaultPort)
    {
        try
        {
            var l = new AiCoreListener(ReadOrCreateToken());
            l._http.Prefixes.Add($"http://127.0.0.1:{port}/");
            l._http.Start();
            _ = Task.Run(l.LoopAsync);
            return l;
        }
        catch { return null; }
    }

    static string ReadOrCreateToken()
    {
        var env = Environment.GetEnvironmentVariable("OSAS_AI_TOKEN")?.Trim();
        if (!string.IsNullOrEmpty(env)) return env;
        try
        {
            if (File.Exists(TokenPath) && File.ReadAllText(TokenPath).Trim() is { Length: > 0 } t) return t;
        }
        catch { }
        var created = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        File.WriteAllText(TokenPath, created);
        return created;
    }

    async Task LoopAsync()
    {
        while (_http.IsListening && !_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync().ConfigureAwait(false); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (!Authorised(ctx.Request.Headers["Authorization"])) { await Reply(ctx, 401, Error("unauthorised")); return; }
            string path = ctx.Request.Url?.AbsolutePath ?? "";
            if (ctx.Request.HttpMethod == "GET" && path == "/v1/health")
            {
                bool ready = LocalLlmHost.Installed;
                await Reply(ctx, 200, JsonSerializer.Serialize(new { status = ready ? "ready" : "not_installed", model_ready = ready, model = LocalModel.FileName }));
            }
            else if (ctx.Request.HttpMethod == "POST" && path == "/v1/chat")
            {
                if (ctx.Request.ContentLength64 > MaxBody) { await Reply(ctx, 413, Error("request too large")); return; }
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                await Chat(ctx, body);
            }
            else if (ctx.Request.HttpMethod == "POST" && path == "/v1/completion")
            {
                if (ctx.Request.ContentLength64 > MaxBody) { await Reply(ctx, 413, Error("request too large")); return; }
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                await Completion(ctx, body);
            }
            else await Reply(ctx, 404, Error("not found"));
        }
        catch (Exception ex)
        {
            try { await Reply(ctx, 500, Error(ex.Message)); } catch { }
        }
    }

    async Task Completion(HttpListenerContext ctx, string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string prompt = root.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
        int maxTokens = root.TryGetProperty("max_tokens", out var mt) && mt.TryGetInt32(out var n) ? Math.Clamp(n, 16, 2048) : 512;
        if (prompt.Length == 0) { await Reply(ctx, 400, Error("prompt is required")); return; }
        if (prompt.Length / 3 + LocalLlm.ReserveTokens > LocalLlm.ContextTokens) { await Reply(ctx, 413, Error("the prompt is too long for the model")); return; }

        await _one.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            var llm = await LocalLlmHost.GetAsync(_stop.Token).ConfigureAwait(false);
            string answer = await llm.CompleteAsync(prompt, maxTokens, null, _stop.Token).ConfigureAwait(false);
            await Reply(ctx, 200, JsonSerializer.Serialize(new { status = "ok", content = answer }));
        }
        finally { _one.Release(); }
    }

    async Task Chat(HttpListenerContext ctx, string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        int maxTokens = root.TryGetProperty("max_tokens", out var mt) && mt.TryGetInt32(out var n) ? Math.Clamp(n, 16, 2048) : 512;
        var sb = new StringBuilder();
        bool hasSystem = false;
        foreach (var m in root.GetProperty("messages").EnumerateArray())
        {
            string role = m.GetProperty("role").GetString() ?? "user";
            string content = m.GetProperty("content").GetString() ?? "";
            switch (role)
            {
                case "system": sb.Append("<|im_start|>system\n").Append(OlmoChatNeutralise(content)).Append("<|im_end|>\n"); hasSystem = true; break;
                case "assistant": sb.Append("<|im_start|>assistant\n").Append(OlmoChatNeutralise(content)).Append("<|im_end|>\n"); break;
                case "tool": sb.Append("<|im_start|>environment\n").Append(OlmoChatNeutralise(content)).Append("<|im_end|>\n"); break;
                default: sb.Append("<|im_start|>user\n").Append(OlmoChatNeutralise(content)).Append("<|im_end|>\n"); break;
            }
        }
        if (!hasSystem) sb.Insert(0, "<|im_start|>system\nYou are a helpful assistant.<|im_end|>\n");
        sb.Append("<|im_start|>assistant\n");
        if (sb.Length / 3 + LocalLlm.ReserveTokens > LocalLlm.ContextTokens) { await Reply(ctx, 413, Error("the conversation is too long for the model")); return; }

        await _one.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            var llm = await LocalLlmHost.GetAsync(_stop.Token).ConfigureAwait(false);
            string answer = await llm.CompleteAsync(sb.ToString(), maxTokens, null, _stop.Token).ConfigureAwait(false);
            // A model that has asked for a tool goes on to invent the result; nothing after the call is wanted.
            foreach (var close in new[] { "</tool_call>", "</function_calls>" })
                if (answer.IndexOf(close, StringComparison.Ordinal) is var at and >= 0) { answer = answer[..(at + close.Length)]; break; }
            await Reply(ctx, 200, JsonSerializer.Serialize(new { status = "ok", message = new { role = "assistant", content = answer } }));
        }
        finally { _one.Release(); }
    }

    // The same neutralising the assistant uses, so a drawing's text cannot close a turn and forge another.
    static string OlmoChatNeutralise(string s) =>
        s.Replace("<|im_", "<​|im_").Replace("<|endoftext|>", "<​|endoftext|>");

    bool Authorised(string? header)
    {
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(header[7..].Trim());
        var want = Encoding.UTF8.GetBytes(_token);
        return CryptographicOperations.FixedTimeEquals(given, want);
    }

    static string Error(string message) => JsonSerializer.Serialize(new { status = "error", error = new { message } });

    static async Task Reply(HttpListenerContext ctx, int code, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _http.Close(); } catch { }
    }
}
