// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aorms.Connect;

/// <summary>
/// What the server is known to hold, as one hash per row, so that "what changed since the last sync" can be worked out from the open project
/// without the app having to report each edit. <see cref="Diff"/> compares the project's current rows with the baseline, returns the rows to
/// send (new or changed) and the rows to delete, and moves the baseline forward. It is saved beside the outbox, so a restart does not forget it.
/// Pure and platform-neutral.
/// </summary>
public sealed class RowBaseline
{
    readonly Dictionary<(string Section, string RowId), string> _hash = new();
    readonly string? _path;

    public RowBaseline(string? path = null)
    {
        _path = path;
        if (path is not null) TryLoad();
    }

    public int Count => _hash.Count;
    /// <summary>True when a baseline was loaded from disk (false for a new project: the caller decides what the baseline should be).</summary>
    public bool Loaded { get; private set; }

    public static string HashOf(SyncRow r) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.Fields.ToJsonString())));

    /// <summary>Makes <paramref name="rows"/> the baseline without sending anything (right after a push, or when adopting the server's state).</summary>
    public void Reset(IEnumerable<SyncRow> rows)
    {
        _hash.Clear();
        foreach (var r in rows) if (!r.Deleted) _hash[(r.Section, r.RowId)] = HashOf(r);
        Save();
    }

    /// <summary>
    /// The changes needed to bring the server in line with <paramref name="current"/>: rows that are new or changed (as upserts) and rows that
    /// have gone (as deletes). The baseline then equals <paramref name="current"/>. If sending fails the caller keeps the result in its outbox,
    /// which is why the baseline may move on at once.
    /// </summary>
    public (List<SyncRow> Upserts, List<SyncRow> Deletes) Diff(IEnumerable<SyncRow> current)
    {
        var upserts = new List<SyncRow>();
        var seen = new HashSet<(string, string)>();
        foreach (var r in current)
        {
            if (r.Deleted) continue;
            var key = (r.Section, r.RowId);
            seen.Add(key);
            var h = HashOf(r);
            if (!_hash.TryGetValue(key, out var old) || old != h)
            {
                upserts.Add(r);
                _hash[key] = h;
            }
        }
        var deletes = new List<SyncRow>();
        foreach (var key in _hash.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            deletes.Add(new SyncRow(key.Section, key.RowId, new JsonObject(), true));
            _hash.Remove(key);
        }
        if (upserts.Count > 0 || deletes.Count > 0) Save();
        return (upserts, deletes);
    }

    /// <summary>Notes rows the server sent us (a catch-up that was applied), so they do not look like local edits afterwards.</summary>
    public void Adopt(IEnumerable<SyncRow> fromServer)
    {
        foreach (var r in fromServer)
        {
            var key = (r.Section, r.RowId);
            if (r.Deleted) _hash.Remove(key); else _hash[key] = HashOf(r);
        }
        Save();
    }

    public void Delete()
    {
        _hash.Clear();
        Loaded = false;
        if (_path is not null) { try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { /* in use */ } }
    }

    void Save()
    {
        if (_path is null) return;
        var o = new JsonObject();
        foreach (var kv in _hash) o[$"{kv.Key.Section}\t{kv.Key.RowId}"] = kv.Value;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }), new UTF8Encoding(false));
        if (File.Exists(_path)) File.Replace(tmp, _path, null); else File.Move(tmp, _path);
    }

    void TryLoad()
    {
        try
        {
            if (!File.Exists(_path!)) return;
            if (JsonNode.Parse(File.ReadAllText(_path!)) is not JsonObject o) return;
            foreach (var kv in o)
            {
                var i = kv.Key.IndexOf('\t');
                if (i > 0 && kv.Value is JsonValue v) _hash[(kv.Key[..i], kv.Key[(i + 1)..])] = v.ToString();
            }
            Loaded = true;
        }
        catch (Exception e) when (e is JsonException or IOException) { _hash.Clear(); Loaded = false; }
    }
}
