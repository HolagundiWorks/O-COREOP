// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace BBSApp.Services;

/// <summary>
/// An annotated drawing PDF O-Draft generated once a take-off was complete (quantity lines, measurements, hatches) and recorded in
/// ShilpiDB. O-CoreOp keeps a verified copy beside the project (docs/TAKEOFF-SHILPIDB.md, "The annotated drawing").
/// </summary>
public sealed class StoredAnnotatedDrawing
{
    public string DrawingId { get; set; } = "";
    public string Drawing { get; set; } = "";
    /// <summary>Absolute path of the stored PDF.</summary>
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Generated { get; set; } = "";
    /// <summary>The drawing revision (SHA-256) the take-off was measured from, when O-Draft gave it.</summary>
    public string Revision { get; set; } = "";
    public string Fetched { get; set; } = "";

    public JsonObject ToJson() => new()
    {
        ["drawing_id"] = DrawingId, ["drawing"] = Drawing, ["file"] = File, ["sha256"] = Sha256,
        ["generated"] = Generated, ["revision"] = Revision, ["fetched"] = Fetched,
    };

    public static StoredAnnotatedDrawing FromJson(JsonObject o) => new()
    {
        DrawingId = o["drawing_id"]?.GetValue<string>() ?? "", Drawing = o["drawing"]?.GetValue<string>() ?? "",
        File = o["file"]?.GetValue<string>() ?? "", Sha256 = o["sha256"]?.GetValue<string>() ?? "",
        Generated = o["generated"]?.GetValue<string>() ?? "", Revision = o["revision"]?.GetValue<string>() ?? "",
        Fetched = o["fetched"]?.GetValue<string>() ?? "",
    };

    public bool Exists => !string.IsNullOrEmpty(File) && System.IO.File.Exists(File);
}

public static class AnnotatedStore
{
    /// <summary>Folder for the project's annotated drawings: beside the saved project (<c>&lt;name&gt;.assets\annotated</c>), or under %LocalAppData%\O-CoreOp until it is saved.</summary>
    public static string FolderFor(ProjectStore store)
    {
        if (!string.IsNullOrWhiteSpace(store.FilePath))
        {
            var dir = Path.GetDirectoryName(store.FilePath)!;
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(store.FilePath) + ".assets", "annotated");
        }
        return Path.Combine(Branding.AppDataDirectory, "annotated");
    }

    /// <summary>Stores a verified PDF and records it on the project, replacing the earlier copy for the same drawing.</summary>
    public static StoredAnnotatedDrawing Save(ProjectStore store, string drawingId, string drawingName, byte[] pdf, string? generated, string? revision)
    {
        var folder = FolderFor(store);
        Directory.CreateDirectory(folder);
        var sha = Convert.ToHexString(SHA256.HashData(pdf));
        var safe = string.Concat(drawingName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safe)) safe = "drawing";
        var path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(safe)}-{sha[..8]}.pdf");
        File.WriteAllBytes(path, pdf);

        var list = store.Info.AnnotatedDrawings;
        var old = list.FirstOrDefault(a => a.DrawingId == drawingId);
        if (old is not null)
        {
            list.Remove(old);
            if (!string.Equals(old.File, path, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(old.File); } catch { /* in use: leave it */ } }
        }
        var entry = new StoredAnnotatedDrawing
        {
            DrawingId = drawingId, Drawing = drawingName, File = path, Sha256 = sha,
            Generated = generated ?? "", Revision = revision ?? "", Fetched = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        };
        list.Add(entry);
        store.Notify();
        return entry;
    }
}
