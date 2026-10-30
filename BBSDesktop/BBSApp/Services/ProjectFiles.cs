// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using BBSApp.Services.Shilpi;
using Office.Shared;

namespace BBSApp.Services;

/// <summary>
/// Opening and saving a project. A project is a project file (<c>.osp</c>): one SQLite file that holds the drawing, the take-off O-Draft recorded
/// and the project itself (estimate, running bills, tasks, quantities, purchase orders, the rest). A project file from before (<c>.bbsproj</c>,
/// <c>.json</c>) still opens, and Save as puts it in a project file.
/// </summary>
public static class ProjectFiles
{
    /// <summary>The project file: one SQLite file with the drawing, the take-off and the project (<see cref="OdpFile"/>).</summary>
    public const string Extension = ".osp";
    public static bool IsOdp(string? path) =>
        !string.IsNullOrEmpty(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a project file.</summary>
    public static bool IsShilpi(string? path) => IsOdp(path);

    public sealed record Opened(JsonObject? Project, bool FromDrawingOnly);

    /// <summary>
    /// Read the project in a file. A project file that holds a take-off but no project yet (O-Draft made it) opens as a new project
    /// named after the file; <see cref="Opened.FromDrawingOnly"/> says so.
    /// </summary>
    public static Opened? Open(string path, out string? error)
    {
        error = null;
        if (!IsShilpi(path))
        {
            var legacy = EngineClient.LoadProject(path, out error);
            return legacy is null ? null : new Opened(legacy, false);
        }
        try
        {
            if (!File.Exists(path)) return new Opened(null, true);
            using var odp = OdpFile.Open(path);
            var doc = odp.ProjectDoc();
            if (doc is null) return new Opened(null, true);
            return JsonNode.Parse(doc) is JsonObject proj ? new Opened(proj, false) : throw new InvalidDataException("The project in this file is not a JSON object.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException or HttpRequestException or Microsoft.Data.Sqlite.SqliteException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Write the project into a project file, keeping the drawing and take-off already in it.</summary>
    public static bool Save(string path, JsonObject project, out string? error)
    {
        error = null;
        try
        {
            using var odp = File.Exists(path) ? OdpFile.Open(path) : OdpFile.Create(path, Path.GetFileNameWithoutExtension(path));
            ServedProject.NoteOwnRevision(path, odp.SetProjectDoc(project.ToJsonString()));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or Microsoft.Data.Sqlite.SqliteException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Make a project file the open project, as File > Open and "open O-Draft's project" both do. Returns false and says why when the file
    /// cannot be read.
    /// </summary>
    public static bool OpenInto(ProjectStore store, string path, out string? error)
    {
        var opened = Open(path, out error);
        if (opened is null) return false;
        if (opened.Project is not null) store.LoadFrom(opened.Project);
        else
        {
            store.Reset();
            var stem = Path.GetFileNameWithoutExtension(path);
            store.Name = stem;                                                                       
            store.IsDirty = true;
        }
        store.FilePath = path;
        store.Shared = null;
        if (IsShilpi(path)) store.Info.TakeoffVdbPath = path;
        return true;
    }
}
