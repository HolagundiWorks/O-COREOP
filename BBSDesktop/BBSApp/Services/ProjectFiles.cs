// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using BBSApp.Services.Shilpi;
using Office.Shared;

namespace BBSApp.Services;

/// <summary>
/// Opening and saving a project. A project is a ShilpiDB file (<c>.vdb</c>), the one that sits beside the drawing and holds the
/// take-off O-Draft recorded: the project itself (estimate, running bills, tasks, quantities, purchase orders, the rest) is stored in it
/// by <see cref="ShilpiProjectFile"/>. A project file from before (<c>.bbsproj</c>, <c>.json</c>) still opens, and saves into a ShilpiDB file
/// with Save as.
/// </summary>
public static class ProjectFiles
{
    /// <summary>The project file: one SQLite file with the drawing, the take-off and the project (<see cref="OdpFile"/>).</summary>
    public const string Extension = ".osp";
    /// <summary>A ShilpiDB take-off file as used before the project file; it still opens and saves.</summary>
    public const string LegacyExtension = ".vdb";

    public static bool IsOdp(string? path) =>
        !string.IsNullOrEmpty(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a project file or a ShilpiDB file: a file that holds a take-off and the project.</summary>
    public static bool IsShilpi(string? path) =>
        IsOdp(path) || (!string.IsNullOrEmpty(path) && path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase));

    public sealed record Opened(JsonObject? Project, bool FromDrawingOnly);

    /// <summary>
    /// Read the project in a file. A ShilpiDB file that holds a take-off but no project yet (O-Draft made it) opens as a new project
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
            if (IsOdp(path))
            {
                if (!File.Exists(path)) return new Opened(null, true);
                using var odp = OdpFile.Open(path);
                var doc = odp.ProjectDoc();
                if (doc is null) return new Opened(null, true);
                return JsonNode.Parse(doc) is JsonObject proj ? new Opened(proj, false) : throw new InvalidDataException("The project in this file is not a JSON object.");
            }
            // O-Draft serves the file: ask the server, the file on disk may be a few seconds behind it
            var gateway = ServedProject.GatewayFor(path);
            if (gateway is null && !File.Exists(path)) return new Opened(null, true);   // not recorded yet: a new project that Save creates the file for
            var json = gateway is not null
                ? ServedProject.ReadProject(gateway)
                : ShilpiProjectFile.GetProject(ShilpiProjectFile.Parse(File.ReadAllBytes(path)));
            if (json is null) return new Opened(null, true);
            return JsonNode.Parse(json) is JsonObject root ? new Opened(root, false) : throw new InvalidDataException("The project in this file is not a JSON object.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException or HttpRequestException or Microsoft.Data.Sqlite.SqliteException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Write the project into a ShilpiDB file, keeping every other record already in it.</summary>
    public static bool Save(string path, JsonObject project, out string? error)
    {
        error = null;
        try
        {
            if (IsOdp(path))
            {
                using var odp = File.Exists(path) ? OdpFile.Open(path) : OdpFile.Create(path, Path.GetFileNameWithoutExtension(path));
                ServedProject.NoteOwnRevision(path, odp.SetProjectDoc(project.ToJsonString()));
                return true;
            }
            // While O-Draft serves the file the server owns it (a write to the file would be overwritten at its next autosave): save through it
            if (ServedProject.GatewayFor(path) is { } gateway) ServedProject.SaveProject(gateway, project.ToJsonString());
            else ShilpiProjectFile.SaveProject(path, project.ToJsonString());
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
            if (stem.EndsWith(".takeoff", StringComparison.OrdinalIgnoreCase)) stem = stem[..^".takeoff".Length];
            if (Path.GetExtension(stem).ToLowerInvariant() is ".dwg" or ".dxf") stem = Path.GetFileNameWithoutExtension(stem);
            store.Name = stem;                                                                          // house.dwg.takeoff.vdb -> house
            store.IsDirty = true;
        }
        store.FilePath = path;
        store.Shared = null;
        if (IsShilpi(path)) store.Info.TakeoffVdbPath = path;
        return true;
    }
}
