// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Office.Shared;

namespace BBSApp.Services.Odp;

/// <summary>
/// Watches the open project file for writes by another program. O-Draft (or the viewer) writes take-off into the same file; the revision inside it
/// says whether anyone but this app wrote since the last look.
/// </summary>
public static class ServedProject
{
    /// <summary>Raised when another program wrote to the project file: O-Draft recorded, updated or removed take-off rows.</summary>
    public static event Action? Changed;

    static long _count = -1;
    static readonly Dictionary<string, long> _ownRevisions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Our own save made revision <paramref name="revision"/> of a project file: it is not "O-Draft changed something".</summary>
    public static void NoteOwnRevision(string path, long revision) => _ownRevisions[Path.GetFullPath(path)] = revision;

    /// <summary>Look at the project file once (the main window calls this every two seconds); a revision nobody here wrote raises <see cref="Changed"/>.</summary>
    public static async Task PollAsync(string? projectPath)
    {
        long n = -1;
        if (ProjectFiles.IsOdp(projectPath))
        {
            try { n = await Task.Run(() => { using var odp = OdpFile.Open(projectPath!); return odp.Revision(); }).ConfigureAwait(true); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException) { n = -1; }
        }
        long own = n >= 0 && _ownRevisions.TryGetValue(Path.GetFullPath(projectPath!), out var o) ? o : -1;
        bool outside = n >= 0 && _count >= 0 && n != _count && n != own;
        _count = n;
        if (outside) Changed?.Invoke();
    }
}
