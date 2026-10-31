// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json;

namespace Office.Shared;

/// <summary>The drawing O-Draft has open and the project file that goes with it.</summary>
public sealed record ActiveProjectInfo(string App, string Drawing, string Project, DateTime UpdatedUtc);

/// <summary>
/// The pointer file O-Draft keeps current under <c>%LOCALAPPDATA%\O-Studio\active-project.json</c>, so O-CoreOp can open the project file of the drawing
/// on screen. O-Draft writes it when a drawing is opened, saved or closed (closed: the file is deleted or says <c>project: null</c>).
/// <code>{ "format": "studio-active-project", "version": 1, "app": "O-Draft", "drawing": "C:\\p\\house.osp", "project": "C:\\p\\house.osp", "updated_utc": "2026-10-29T09:00:00Z" }</code>
/// </summary>
public static class ActiveProject
{
    public const string Format = SuiteWire.ActiveProjectFormat;

    public static string PointerPath => SuiteWire.LocalPath("active-project.json");

    /// <summary>Null when the text is not a pointer, names no project file, or names one that is not an absolute <c>.osp</c> path.</summary>
    public static ActiveProjectInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("format", out var f) || !SuiteWire.IsActiveProjectFormat(f.GetString())) return null;
            if (!r.TryGetProperty("version", out var v) || !v.TryGetInt32(out var ver) || ver < 1) return null;
            string? db = r.TryGetProperty("project", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(db) || !Path.IsPathRooted(db) || !db.EndsWith(".osp", StringComparison.OrdinalIgnoreCase)) return null;
            string drawing = r.TryGetProperty("drawing", out var dr) && dr.ValueKind == JsonValueKind.String ? dr.GetString() ?? "" : "";
            string app = r.TryGetProperty("app", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "O-Draft" : "O-Draft";
            DateTime when = r.TryGetProperty("updated_utc", out var u) && u.ValueKind == JsonValueKind.String
                            && DateTime.TryParse(u.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                ? t : DateTime.MinValue;
            return new ActiveProjectInfo(app, drawing, db, when);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The pointer as it is now, or null when there is none or it names no usable file.</summary>
    public static ActiveProjectInfo? Read()
    {
        try { return File.Exists(PointerPath) ? Parse(File.ReadAllText(PointerPath)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static bool SamePath(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
