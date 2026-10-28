// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aqc.CadImport;

/// <summary>Reads a hcw-aqc-bridge v1 file (written by hcwCAD-KIT, MIT). Contract only; no shared code.</summary>
public sealed class BridgeFile
{
    public string DrawingId { get; init; } = "";
    public string Drawing { get; init; } = "";
    /// <summary>Company uuid from <c>source.project_ref</c> when O-Draft linked the drawing to a company-file project (same as ShilpiDB headers).</summary>
    public string? ProjectRefCompany { get; init; }
    /// <summary>Project id from <c>source.project_ref</c> when present and numeric.</summary>
    public long? ProjectRefId { get; init; }
    public Dictionary<string, string> Project { get; init; } = new();
    public List<BridgeLevel> Levels { get; init; } = new();
    public List<JsonObject> Walls { get; init; } = new();
    public List<JsonObject> Openings { get; init; } = new();
    public List<JsonObject> OpeningSchedule { get; init; } = new();
    public List<JsonObject> Columns { get; init; } = new();
    public List<JsonObject> Lintels { get; init; } = new();
    public List<JsonObject> Rooms { get; init; } = new();
    public List<JsonObject> Slabs { get; init; } = new();
    public List<JsonObject> Electrical { get; init; } = new();
    public List<JsonObject> ElectricalCircuits { get; init; } = new();

    public static BridgeFile Parse(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw new BridgeFormatException("Not a JSON object."); }
        catch (JsonException ex) { throw new BridgeFormatException("Invalid JSON: " + ex.Message); }

        if (Str(root, "format") != "hcw-aqc-bridge")
            throw new BridgeFormatException("Not an hcw-aqc-bridge file.");
        if (Num(root, "version") != 1)
            throw new BridgeFormatException($"Unsupported bridge version {Str(root, "version")} (this AQC reads version 1).");
        if (!string.Equals(Str(root, "units"), "mm", StringComparison.OrdinalIgnoreCase))
            throw new BridgeFormatException($"Unsupported units '{Str(root, "units")}' (expected mm).");

        var src = root["source"] as JsonObject;
        var projectRef = src?["project_ref"] as JsonObject;
        string? refCompany = null;
        long? refId = null;
        if (projectRef is not null)
        {
            refCompany = NullIfEmpty(Str(projectRef, "company_uuid"));
            if (projectRef["project_id"] is JsonValue pid && pid.TryGetValue<long>(out var pidValue))
                refId = pidValue;
        }
        var f = new BridgeFile
        {
            DrawingId = src is null ? "" : Str(src, "drawing_id"),
            Drawing = src is null ? "" : Str(src, "drawing"),
            ProjectRefCompany = refCompany,
            ProjectRefId = refId,
        };
        if (string.IsNullOrWhiteSpace(f.DrawingId))
            throw new BridgeFormatException("source.drawing_id is missing.");

        if (root["project"] is JsonObject p)
            foreach (var kv in p)
                if (kv.Value is JsonValue v) f.Project[kv.Key] = v.ToString();

        if (root["levels"] is JsonArray la)
            foreach (var o in la.OfType<JsonObject>())
                f.Levels.Add(new BridgeLevel(Str(o, "name"), Num(o, "height_mm"), Num(o, "slab_thickness_mm"), Num(o, "beam_depth_mm")));

        Fill(root, "walls", f.Walls);
        Fill(root, "openings", f.Openings);
        Fill(root, "opening_schedule", f.OpeningSchedule);
        Fill(root, "columns", f.Columns);
        Fill(root, "lintels", f.Lintels);
        Fill(root, "rooms", f.Rooms);
        Fill(root, "slabs", f.Slabs);
        Fill(root, "electrical", f.Electrical);
        Fill(root, "electrical_circuits", f.ElectricalCircuits);
        return f;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// One-line caption for Import-from-CAD preview: whether this bridge file's <c>source.project_ref</c>
    /// matches the open company-file project (same rules as the ShilpiDB Take-off list Mine/Other).
    /// </summary>
    public string ProjectRefCaption(string? openCompanyUuid, long? openProjectId)
    {
        var hasRef = ProjectRefId is not null || !string.IsNullOrEmpty(ProjectRefCompany);
        if (!hasRef)
            return "This drawing has no company-file project_ref; it will not be marked as this project.";
        if (!string.IsNullOrEmpty(openCompanyUuid) && openProjectId is long pid
            && ProjectRefCompany == openCompanyUuid && ProjectRefId == pid)
            return "This drawing names this company-file project (project_ref).";
        return "This drawing names another company-file project (project_ref).";
    }

    static void Fill(JsonObject root, string key, List<JsonObject> into)
    {
        if (root[key] is JsonArray a) into.AddRange(a.OfType<JsonObject>());
    }

    internal static string Str(JsonObject o, string key) =>
        o[key] is JsonValue v ? v.ToString() : "";

    internal static double? Num(JsonObject o, string key)
    {
        if (o[key] is not JsonValue v) return null;
        return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}

public sealed record BridgeLevel(string Name, double? HeightMm, double? SlabThicknessMm, double? BeamDepthMm);

public sealed class BridgeFormatException : Exception
{
    public BridgeFormatException(string message) : base(message) { }
}
