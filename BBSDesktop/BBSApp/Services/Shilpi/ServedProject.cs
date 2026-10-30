// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;
using Aqc.CadImport;
using Office.Shared;

namespace BBSApp.Services.Shilpi;

/// <summary>
/// A project file that O-Draft is serving: while a drawing is open O-Draft runs <c>shilpid</c> on its ShilpiDB file (and the <c>shilpi-http</c>
/// gateway) and says so in the active-project pointer. The file then belongs to that server, so O-CoreOp does not touch it: it reads the take-off
/// and the project, and saves the project, through the gateway. A take-off recorded in O-Draft is in the server the moment it is written.
/// </summary>
public static class ServedProject
{
    /// <summary>The gateway serving <paramref name="vdbPath"/> right now, or null (nobody serves it, or the server does not answer).</summary>
    public static string? GatewayFor(string? vdbPath)
    {
        if (string.IsNullOrEmpty(vdbPath)) return null;
        var active = ActiveProject.Read();
        if (active?.Server is not { } url || !ActiveProject.SamePath(active.ShilpiDb, vdbPath)) return null;
        return Run(() => ShilpiTakeoffService.IsGatewayUpAsync(url)) ? url : null;
    }

    static Uri Base(string url) => new(url.TrimEnd('/') + "/");

    /// <summary>The project document in the served file (null when it has none yet).</summary>
    public static string? ReadProject(string gatewayUrl) => Run(async () =>
    {
        using var client = new ShilpiGatewayClient(Base(gatewayUrl));
        var chunks = await client.ReadAllAsync(include: id => ShilpiTakeoff.KindOf(id) == ShilpiProjectFile.KindProject).ConfigureAwait(false);
        if (chunks.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var c in chunks.OrderBy(c => ShilpiTakeoff.NOf(c.Id))) sb.Append(c.PayloadText ?? "");
        return sb.ToString();
    });

    /// <summary>Replace the project document in the served file and flush the server to disk. Other records are not touched.</summary>
    public static void SaveProject(string gatewayUrl, string json) => Run(async () =>
    {
        using var client = new ShilpiGatewayClient(Base(gatewayUrl));
        var scratch = new VdbContents();
        ShilpiProjectFile.SetProject(scratch, json);
        var keep = scratch.Records.Select(r => r.Id).ToHashSet();
        var old = (await client.ListIdsAsync().ConfigureAwait(false)).Where(id => ShilpiTakeoff.KindOf(id) == ShilpiProjectFile.KindProject && !keep.Contains(id));
        foreach (var id in old) await client.DeleteAsync(id).ConfigureAwait(false);
        foreach (var r in scratch.Records) await client.PutAsync(r.Id, r.Payload).ConfigureAwait(false);
        await client.SaveAsync().ConfigureAwait(false);
        return true;
    });

    /// <summary>The number of records in the served file; changes when O-Draft records, or removes, a take-off row.</summary>
    public static Task<long> CountAsync(string gatewayUrl) => Task.Run(async () =>
    {
        using var client = new ShilpiGatewayClient(Base(gatewayUrl));
        return await client.CountAsync().ConfigureAwait(false);
    });

    /// <summary>Raised when the served file's record count changes: O-Draft recorded, updated or removed take-off rows.</summary>
    public static event Action? Changed;

    static long _count = -1;
    static readonly Dictionary<string, long> _ownRevisions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Our own save made revision <paramref name="revision"/> of a project file: it is not "O-Draft changed something".</summary>
    public static void NoteOwnRevision(string path, long revision) => _ownRevisions[Path.GetFullPath(path)] = revision;

    /// <summary>
    /// Look at the served file once (the main window calls this every two seconds). Nothing served, or the server not answering, resets it; a
    /// change in the number of records after the first look raises <see cref="Changed"/>.
    /// </summary>
    public static async Task PollAsync(string? vdbPath)
    {
        long n = -1;
        if (ProjectFiles.IsOdp(vdbPath))
        {
            // A project file has no server: the revision inside it says whether another program (O-Draft, the viewer) wrote since we looked.
            try { n = await Task.Run(() => { using var odp = OdpFile.Open(vdbPath!); return odp.Revision(); }).ConfigureAwait(true); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException) { n = -1; }
            long own = _ownRevisions.TryGetValue(Path.GetFullPath(vdbPath!), out var o) ? o : -1;
            bool outside = n >= 0 && _count >= 0 && n != _count && n != own;
            _count = n;
            if (outside) Changed?.Invoke();
            return;
        }
        try
        {
            var active = await Task.Run(ActiveProject.Read).ConfigureAwait(true);
            if (active?.Server is { } url && ActiveProject.SamePath(active.ShilpiDb, vdbPath))
                n = await CountAsync(url).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { n = -1; }
        bool changed = n >= 0 && _count >= 0 && n != _count;
        _count = n;
        if (changed) Changed?.Invoke();
    }

    // The calls are short loopback requests; the clients use ConfigureAwait(false), so waiting here cannot deadlock the UI thread.
    static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
