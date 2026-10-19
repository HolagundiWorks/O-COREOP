// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Aqc.CadImport;

namespace BBSApp.Services.Shilpi;

/// <summary>
/// Reads the take-off O-Draft recorded in ShilpiDB (docs/TAKEOFF-SHILPIDB.md). Two sources:
/// a take-off database file (.vdb), which this starts a read-only <c>shilpid</c> and <c>shilpi-http</c> for, reads and stops;
/// or a gateway that is already running (for example one O-Draft or the ShilpiDB desktop app started).
/// </summary>
public static class ShilpiTakeoffService
{
    public const string DefaultGateway = "http://127.0.0.1:7421";

    /// <summary>Where shilpid.exe and shilpi-http.exe are: SHILPIDB_BIN, then %LocalAppData%\ShilpiDB\bin, then PATH.</summary>
    public static string? FindBinDirectory()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("SHILPIDB_BIN") is { Length: > 0 } env) candidates.Add(env);
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShilpiDB", "bin"));
        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(p.Trim());
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "shilpid.exe")) && File.Exists(Path.Combine(d, "shilpi-http.exe")));
    }

    /// <summary>Reads a take-off .vdb. Throws with a message fit to show the user.</summary>
    public static async Task<ShilpiTakeoff.Result> ReadVdbAsync(string vdbPath, IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (!File.Exists(vdbPath)) throw new FileNotFoundException($"The take-off database was not found: {vdbPath}");
        var bin = FindBinDirectory()
                  ?? throw new InvalidOperationException("ShilpiDB is not installed. Install shilpid and shilpi-http (github.com/HolagundiWorks/shilpidb), or set SHILPIDB_BIN to the folder that holds them.");

        int dbPort = FreePort(), httpPort = FreePort();
        Process? db = null, gw = null;
        try
        {
            status?.Report("Opening the take-off database…");
            db = Launch(Path.Combine(bin, "shilpid.exe"), $"--bind 127.0.0.1:{dbPort} --data \"{vdbPath}\" --read-only --log-level error");
            gw = Launch(Path.Combine(bin, "shilpi-http.exe"), $"--bind 127.0.0.1:{httpPort} --backend 127.0.0.1:{dbPort}");
            using var client = new ShilpiGatewayClient(new Uri($"http://127.0.0.1:{httpPort}/"));
            for (int i = 0; ; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (db.HasExited) throw new InvalidOperationException($"shilpid stopped at start (exit code {db.ExitCode}). Is the file a ShilpiDB database?");
                if (gw.HasExited) throw new InvalidOperationException($"shilpi-http stopped at start (exit code {gw.ExitCode}).");
                if (await client.IsUpAsync(ct).ConfigureAwait(false)) break;
                if (i > 100) throw new TimeoutException("ShilpiDB did not start in time.");
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return await ReadAsync(client, status, ct).ConfigureAwait(false);
        }
        finally
        {
            Stop(gw); Stop(db);
        }
    }

    /// <summary>Reads from a shilpi-http gateway that is already running.</summary>
    public static async Task<ShilpiTakeoff.Result> ReadGatewayAsync(string baseUrl, IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter the gateway address as http://host:port.");
        using var client = new ShilpiGatewayClient(uri);
        if (!await client.IsUpAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException($"No ShilpiDB gateway answered at {baseUrl}.");
        return await ReadAsync(client, status, ct).ConfigureAwait(false);
    }

    static async Task<ShilpiTakeoff.Result> ReadAsync(ShilpiGatewayClient client, IProgress<string>? status, CancellationToken ct)
    {
        var progress = new Progress<(int Done, int Total)>(p => status?.Report($"Reading take-off… {p.Done} of {p.Total}"));
        var records = await client.ReadAllAsync(progress, ct).ConfigureAwait(false);
        return ShilpiTakeoff.Read(records);
    }

    static Process Launch(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false })
        ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(exe)}.");

    static void Stop(Process? p)
    {
        try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch { /* already gone */ }
        p?.Dispose();
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; } finally { l.Stop(); }
    }
}
