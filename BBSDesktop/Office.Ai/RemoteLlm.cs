// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Office.Ai;

/// <summary>
/// Uses the suite AI Core on loopback (O-Studio hosts Olmo; O-CoreOp and O-Draft connect here). Does not load weights in-process.
/// </summary>
public sealed class RemoteLlm : IDisposable
{
    readonly HttpClient _http;

    RemoteLlm(HttpClient http) => _http = http;

    public static string Host =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OSAS_AI_HOST"))
            ? "127.0.0.1"
            : Environment.GetEnvironmentVariable("OSAS_AI_HOST")!.Trim();

    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("OSAS_AI_PORT"), out var p) ? p : AiCoreListener.DefaultPort;

    static string ReadToken()
    {
        var env = Environment.GetEnvironmentVariable("OSAS_AI_TOKEN")?.Trim();
        if (!string.IsNullOrEmpty(env)) return env;
        try
        {
            if (File.Exists(AiCoreListener.TokenPath) && File.ReadAllText(AiCoreListener.TokenPath).Trim() is { Length: > 0 } t)
                return t;
        }
        catch { }
        return "";
    }

    static HttpClient CreateClient()
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri($"http://{Host}:{Port}/"),
            Timeout = TimeSpan.FromMinutes(10),
        };
        var token = ReadToken();
        if (token.Length > 0)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>True when something is listening and authorisation succeeds.</summary>
    public static async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = CreateClient();
            using var response = await http.GetAsync("/v1/health", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public static async Task<RemoteLlm?> ConnectAsync(CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct).ConfigureAwait(false))
            return null;
        return new RemoteLlm(CreateClient());
    }

    /// <summary>Same contract as <see cref="LocalLlm.CompleteAsync"/> via <c>POST /v1/completion</c> on the host.</summary>
    public async Task<string> CompleteAsync(string prompt, int maxTokens, IProgress<string>? partial = null, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { prompt, max_tokens = maxTokens });
        using var response = await _http.PostAsync("/v1/completion",
            new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ParseError(text) ?? $"The assistant host answered {(int)response.StatusCode}.");
        using var doc = JsonDocument.Parse(text);
        var content = doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        partial?.Report(content);
        return content.Trim();
    }

    static string? ParseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
                ? m.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    public void Dispose() => _http.Dispose();
}
