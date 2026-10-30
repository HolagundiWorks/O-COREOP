// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aqc.CadImport;

namespace BBSApp.Services.Odp;

/// <summary>Where the take-off is read from: a project file (<c>.osp</c>), the one O-Draft records into.</summary>
public abstract record TakeoffSource
{
    /// <summary>A project file, read directly. There is no server.</summary>
    public sealed record Project(string Path) : TakeoffSource;
}

/// <summary>
/// Reads the take-off O-Draft recorded in the project file. The records are read in the shape the importer takes (<see cref="TakeoffRecord"/>,
/// headers and rows with the id <c>kind &lt;&lt; 56 | slot &lt;&lt; 32 | n</c>, the annotated PDF in chunk records), so the importer, the preview and the
/// annotated-PDF check are the ones that always read a take-off; only where the records come from is the project file (<see cref="OdpTakeoffAdapter"/>).
/// </summary>
public static class OdpTakeoffService
{
    /// <summary>The drawings whose take-off is recorded in the project file.</summary>
    public static async Task<List<TakeoffDrawing>> ListDrawingsAsync(TakeoffSource source, CancellationToken ct = default) =>
        OspTakeoff.ListDrawings(await ReadFileAsync(source, ct).ConfigureAwait(false));

    /// <summary>The Studio project a take-off header names (company uuid and project id), from the first drawing that has one.</summary>
    public static async Task<(string? Company, long? ProjectId)> FirstProjectRefAsync(TakeoffSource source, CancellationToken ct = default)
    {
        var d = (await ListDrawingsAsync(source, ct).ConfigureAwait(false)).FirstOrDefault(x => x.ProjectRefId is not null);
        return (d?.ProjectRefCompany, d?.ProjectRefId);
    }

    /// <summary>Reads one drawing's take-off (rows only, never the annotated PDF).</summary>
    public static async Task<OspTakeoff.Result> ReadDrawingAsync(TakeoffSource source, uint slot, IProgress<string>? status = null, CancellationToken ct = default) =>
        OspTakeoff.Read((await ReadFileAsync(source, ct).ConfigureAwait(false)).Where(r => OspTakeoff.SlotOf(r.Id) == slot && OspTakeoff.IsTakeoffRecord(r.Id)), slot);

    /// <summary>Fetches and verifies the annotated PDF O-Draft recorded for a drawing.</summary>
    public static async Task<byte[]> FetchAnnotatedPdfAsync(TakeoffSource source, uint slot, IProgress<string>? status = null, CancellationToken ct = default) =>
        OspTakeoff.ReadAnnotatedPdf(await ReadFileAsync(source, ct).ConfigureAwait(false), slot);

    static Task<List<TakeoffRecord>> ReadFileAsync(TakeoffSource source, CancellationToken ct)
    {
        if (source is not TakeoffSource.Project f) throw new ArgumentException("Unknown take-off source.");
        return Task.Run(() =>
        {
            try { return OdpTakeoffAdapter.Records(f.Path); }
            catch (Exception e) when (e is InvalidDataException or IOException or Microsoft.Data.Sqlite.SqliteException)
            {
                throw new InvalidOperationException($"{Path.GetFileName(f.Path)}: {e.Message}", e);
            }
        }, ct);
    }
}
