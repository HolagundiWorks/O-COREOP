// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;
using System.Text.Json;

namespace Aorms.Connect;

/// <summary>
/// An <see cref="IOutboxStore"/> backed by one JSON file, so unsent edits survive a restart or a crash (handoff item 4.1).
/// The file holds project rows only, never a token or password. A save writes a temporary file and swaps it in, so a crash
/// mid-save leaves the previous complete file. A file that cannot be read is moved aside (never deleted, never silently
/// discarded) and <see cref="QuarantinedPath"/> says where, so the app can tell the user.
/// </summary>
public sealed class FileOutboxStore : IOutboxStore
{
    readonly string _path;
    readonly object _gate = new();

    public FileOutboxStore(string path) => _path = path;

    public string Path => _path;

    /// <summary>Set when <see cref="Load"/> found an unreadable file and moved it aside.</summary>
    public string? QuarantinedPath { get; private set; }

    public string? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            string text;
            try { text = File.ReadAllText(_path, Encoding.UTF8); }
            catch (IOException) { return Quarantine(); }
            catch (UnauthorizedAccessException) { return Quarantine(); }
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return Quarantine();
            }
            catch (JsonException) { return Quarantine(); }
            return text;
        }
    }

    public void Save(string json)
    {
        lock (_gate)
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!;
            Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
    }

    /// <summary>Removes the file once the outbox is empty and acknowledged (an empty array is kept instead if the file cannot be deleted).</summary>
    public void Delete()
    {
        lock (_gate)
        {
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (IOException) { Save("[]"); }
        }
    }

    string? Quarantine()
    {
        var aside = $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
        try { File.Move(_path, aside); QuarantinedPath = aside; }
        catch (IOException) { QuarantinedPath = _path; }
        return null;
    }
}
