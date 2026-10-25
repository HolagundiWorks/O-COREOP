// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Diagnostics;
using Aqc.CadImport;

namespace BBSApp.Services.Shilpi;

/// <summary>Where the take-off is read from: the shared server that O-Draft and O-CoreOp both use, or a take-off database file.</summary>
public abstract record TakeoffSource
{
    /// <summary>A running shilpi-http gateway (the shared server).</summary>
    public sealed record Gateway(string Url) : TakeoffSource;
    /// <summary>A take-off .vdb file, read directly (no server). Files written by shilpid (version 1) and by O-Draft (version 2) both work.</summary>
    public sealed record VdbFile(string Path) : TakeoffSource;
}

/// <summary>
/// Reads the take-off O-Draft recorded in ShilpiDB (docs/TAKEOFF-SHILPIDB.md). O-Draft and O-CoreOp run side by side against one shared
/// <c>shilpid</c> (default 127.0.0.1:7420) and its <c>shilpi-http</c> gateway (default 127.0.0.1:7421), so a take-off O-Draft records
/// is readable here at once. A take-off .vdb can also be opened by itself, offline, without any server.
/// </summary>
public static class ShilpiTakeoffService
{
    public const string DefaultGateway = "http://127.0.0.1:7421";
    const int SharedDbPort = 7420, SharedHttpPort = 7421;

    /// <summary>The shared database file both apps use when this app is the one that starts the server.</summary>
    public static string SharedDataFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OSAS", "takeoff", "takeoff.vdb");

    /// <summary>
    /// Where shilpid.exe and shilpi-http.exe are: beside this app (engine bundle), then SHILPIDB_BIN,
    /// %LocalAppData%\ShilpiDB\bin, then PATH.
    /// </summary>
    public static string? FindBinDirectory()
    {
        var candidates = new List<string>();
        static void AddDir(List<string> list, string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            dir = Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!list.Contains(dir, StringComparer.OrdinalIgnoreCase)) list.Add(dir);
        }

        AddDir(candidates, AppContext.BaseDirectory);
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) AddDir(candidates, Path.GetDirectoryName(exe));
        }
        catch { /* ignore */ }

        if (Environment.GetEnvironmentVariable("SHILPIDB_BIN") is { Length: > 0 } env) AddDir(candidates, env);
        AddDir(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "O-CoreOp"));
        AddDir(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "O-Draft"));
        AddDir(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OSAS", "shilpidb"));
        AddDir(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShilpiDB", "bin"));
        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            AddDir(candidates, p.Trim());
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "shilpid.exe")) && File.Exists(Path.Combine(d, "shilpi-http.exe")));
    }

    public static async Task<bool> IsGatewayUpAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri)) return false;
        using var c = new ShilpiGatewayClient(uri);
        return await c.IsUpAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes sure the shared server is running: does nothing if it already answers (O-Draft may have started it), otherwise starts
    /// <c>shilpid</c> (autosave every 5 s) and <c>shilpi-http</c> on the shared data file and leaves them running for both apps.
    /// Returns true when this call started it.
    /// </summary>
    public static async Task<bool> EnsureSharedServerAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (await IsGatewayUpAsync(DefaultGateway, ct).ConfigureAwait(false)) return false;
        var bin = FindBinDirectory() ?? throw NotInstalled();
        Directory.CreateDirectory(Path.GetDirectoryName(SharedDataFile)!);
        status?.Report("Starting the shared take-off server…");
        Launch(Path.Combine(bin, "shilpid.exe"), $"--bind 127.0.0.1:{SharedDbPort} --data \"{SharedDataFile}\" --autosave-secs 5 --log-level error");
        Launch(Path.Combine(bin, "shilpi-http.exe"), $"--bind 127.0.0.1:{SharedHttpPort} --backend 127.0.0.1:{SharedDbPort}");
        for (int i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsGatewayUpAsync(DefaultGateway, ct).ConfigureAwait(false)) return true;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("The shared take-off server did not start in time.");
    }

    /// <summary>The drawings whose take-off is recorded at this source.</summary>
    public static async Task<List<ShilpiDrawing>> ListDrawingsAsync(TakeoffSource source, CancellationToken ct = default)
    {
        if (source is TakeoffSource.VdbFile f)
            return ShilpiTakeoff.ListDrawings(await ReadFileAsync(f, ct).ConfigureAwait(false));
        return await WithGateway(source, null, async (client, _) =>
        {
            var ids = await client.ListIdsAsync(ct).ConfigureAwait(false);
            var headers = await client.ReadAllAsync(ct: ct, include: id => ShilpiTakeoff.KindOf(id) == ShilpiTakeoff.KindHeader && ShilpiTakeoff.NOf(id) == 0).ConfigureAwait(false);
            return ShilpiTakeoff.ListDrawings(headers, ids);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Reads one drawing's take-off (rows only, never the annotated PDF).</summary>
    public static async Task<ShilpiTakeoff.Result> ReadDrawingAsync(TakeoffSource source, uint slot, IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (source is TakeoffSource.VdbFile f)
            return ShilpiTakeoff.Read((await ReadFileAsync(f, ct).ConfigureAwait(false)).Where(r => ShilpiTakeoff.SlotOf(r.Id) == slot && ShilpiTakeoff.IsTakeoffRecord(r.Id)), slot);
        return await WithGateway(source, status, async (client, st) =>
        {
            var progress = new Progress<(int Done, int Total)>(p => st?.Report($"Reading take-off… {p.Done} of {p.Total}"));
            var records = await client.ReadAllAsync(progress, ct, id => ShilpiTakeoff.SlotOf(id) == slot && ShilpiTakeoff.IsTakeoffRecord(id)).ConfigureAwait(false);
            return ShilpiTakeoff.Read(records, slot);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Fetches and verifies the annotated PDF O-Draft recorded for a drawing.</summary>
    public static async Task<byte[]> FetchAnnotatedPdfAsync(TakeoffSource source, uint slot, IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (source is TakeoffSource.VdbFile f)
            return ShilpiTakeoff.ReadAnnotatedPdf(await ReadFileAsync(f, ct).ConfigureAwait(false), slot);
        return await WithGateway(source, status, async (client, st) =>
        {
            var progress = new Progress<(int Done, int Total)>(p => st?.Report($"Fetching the annotated drawing… part {p.Done} of {p.Total}"));
            var records = await client.ReadAllAsync(progress, ct,
                id => ShilpiTakeoff.SlotOf(id) == slot && ShilpiTakeoff.KindOf(id) is ShilpiTakeoff.KindHeader or ShilpiTakeoff.KindAnnotatedChunk).ConfigureAwait(false);
            return ShilpiTakeoff.ReadAnnotatedPdf(records, slot);
        }, ct).ConfigureAwait(false);
    }

    // ---- plumbing ----

    static Task<List<ShilpiRecord>> ReadFileAsync(TakeoffSource.VdbFile f, CancellationToken ct) =>
        Task.Run(() =>
        {
            try { return Aqc.CadImport.VdbFile.Read(f.Path); }
            catch (InvalidDataException e) { throw new InvalidOperationException($"{Path.GetFileName(f.Path)}: {e.Message}", e); }
        }, ct);

    static async Task<T> WithGateway<T>(TakeoffSource source, IProgress<string>? status, Func<ShilpiGatewayClient, IProgress<string>?, Task<T>> work, CancellationToken ct)
    {
        if (source is not TakeoffSource.Gateway g) throw new ArgumentException("Unknown take-off source.");
        if (!Uri.TryCreate(g.Url.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter the gateway address as http://host:port.");
        using var client = new ShilpiGatewayClient(uri);
        if (!await client.IsUpAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException($"No ShilpiDB gateway answered at {g.Url}. Start the shared take-off server, or open a take-off file.");
        return await work(client, status).ConfigureAwait(false);
    }

    static InvalidOperationException NotInstalled() =>
        new("ShilpiDB engine binaries (shilpid, shilpi-http) were not found beside O-CoreOp. Reinstall O-CoreOp, or set SHILPIDB_BIN to the folder that holds them.");

    static void Launch(string exe, string args) =>
        (Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false })
         ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(exe)}.")).Dispose();
}
