// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
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
    public const string Extension = ".vdb";

    public static bool IsShilpi(string? path) =>
        !string.IsNullOrEmpty(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

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
            if (!File.Exists(path)) return new Opened(null, true);     // O-Draft has not recorded into it yet: a new project that Save creates the file for
            var json = ShilpiProjectFile.GetProject(ShilpiProjectFile.Parse(File.ReadAllBytes(path)));
            if (json is null) return new Opened(null, true);
            return JsonNode.Parse(json) is JsonObject root ? new Opened(root, false) : throw new InvalidDataException("The project in this file is not a JSON object.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
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
            ShilpiProjectFile.SaveProject(path, project.ToJsonString());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
