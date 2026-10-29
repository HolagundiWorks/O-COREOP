// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Office.Ai;

/// <summary>
/// The model, served by llama.cpp's <c>llama-server</c> as a hidden child process that listens on 127.0.0.1 only, on a free
/// port, behind a random key, and ends when the app does. Nothing leaves the computer. The server keeps the conversation's
/// already-read prefix, so sending the whole prompt each turn costs only what is new.
/// </summary>
public sealed class LocalLlm : IDisposable
{
    public const int ContextTokens = 16384;
    /// <summary>Room kept free for the answer when a conversation is trimmed to fit.</summary>
    public const int ReserveTokens = 1024;

    readonly Process _server;
    readonly HttpClient _http;

    LocalLlm(Process server, HttpClient http) { _server = server; _http = http; }

    public string ModelPath { get; private init; } = "";

    /// <summary>Start the server and wait until the model is loaded (seconds when the file is cached, longer the first time).</summary>
    public static async Task<LocalLlm> StartAsync(string modelPath, string serverPath, CancellationToken ct = default)
    {
        int port = FreePort();
        string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var psi = new ProcessStartInfo(serverPath)
        {
            WorkingDirectory = Path.GetDirectoryName(serverPath)!,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "-m", modelPath, "--host", "127.0.0.1", "--port", port.ToString(), "-c", ContextTokens.ToString(),
                                  "-np", "1", "--no-webui", "--no-jinja", "--api-key", key })
            psi.ArgumentList.Add(a);
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("The model server did not start.");
        JobObject.Attach(proc);                       // the server dies with the app, even if the app crashes
        var log = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) { if (log.Length < 20000) log.AppendLine(e.Data); } };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) { if (log.Length < 20000) log.AppendLine(e.Data); } };
        proc.BeginOutputReadLine(); proc.BeginErrorReadLine();

        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var llm = new LocalLlm(proc, http) { ModelPath = modelPath };
        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (proc.HasExited)
                {
                    string tail; lock (log) tail = string.Join('\n', log.ToString().Split('\n').TakeLast(6));
                    throw new InvalidOperationException("The model server stopped while loading:\n" + tail.Trim());
                }
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The model took more than 3 minutes to load.");
                try
                {
                    using var r = await http.GetAsync("/health", ct).ConfigureAwait(false);
                    if (r.IsSuccessStatusCode) return llm;
                }
                catch (HttpRequestException) { }
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
        }
        catch { llm.Dispose(); throw; }
    }

    /// <summary>
    /// The model's answer to <paramref name="prompt"/> (a whole Olmo-format conversation ending in the open assistant turn),
    /// up to the end of its turn. Temperature 0.2, top-p 0.8, top-k 20: steady enough for tool calls.
    /// </summary>
    public async Task<string> CompleteAsync(string prompt, int maxTokens, IProgress<string>? partial = null, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            prompt, n_predict = maxTokens, temperature = 0.2, top_p = 0.8, top_k = 20,
            stop = new[] { "<|im_end|>", "<|endoftext|>" }, cache_prompt = true, stream = true,
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/completion") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"The model server answered {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)}");
        var sb = new StringBuilder();
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            using var doc = JsonDocument.Parse(line.AsSpan(5).Trim().ToString());
            if (doc.RootElement.TryGetProperty("content", out var c) && c.GetString() is { Length: > 0 } piece)
            {
                sb.Append(piece);
                partial?.Report(sb.ToString());
            }
            if (doc.RootElement.TryGetProperty("stop", out var s) && s.ValueKind == JsonValueKind.True) break;
        }
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _http.Dispose();
        try { if (!_server.HasExited) _server.Kill(entireProcessTree: true); } catch { }
        _server.Dispose();
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>A Windows job that kills its processes when this app's handle to it closes (that is, when the app ends).</summary>
    static class JobObject
    {
        static readonly IntPtr Job = Create();

        static IntPtr Create()
        {
            try
            {
                var job = CreateJobObject(IntPtr.Zero, null);
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int size = Marshal.SizeOf(info);
                var p = Marshal.AllocHGlobal(size);
                try { Marshal.StructureToPtr(info, p, false); SetInformationJobObject(job, 9, p, (uint)size); }
                finally { Marshal.FreeHGlobal(p); }
                return job;
            }
            catch { return IntPtr.Zero; }
        }

        public static void Attach(Process p) { if (Job != IntPtr.Zero) AssignProcessToJobObject(Job, p.Handle); }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint size);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
    }
}
