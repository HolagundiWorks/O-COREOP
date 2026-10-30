// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Office.Shared;

public sealed record OdpMeta(string ProjectId, string Name, string Created, long Revision);
public sealed record OdpDrawing(string Name, string Ext, string Sha256, byte[] Bytes);
public sealed record OdpTakeoffHeader(uint Slot, string State, string Json);
public sealed record OdpTakeoffRow(uint Slot, int Kind, uint N, string Json);

/// <summary>
/// A project file, <c>.odp</c> (format <c>osas-project</c>, version 1): one SQLite file with the drawing, the take-off O-Draft recorded, the O-CoreOp project,
/// attachments and markups. This is the .NET side of <c>aad-odp</c> (O-Draft's Rust crate); both read and write the same file. Every committed change raises
/// <c>meta.revision</c> by one and leaves <c>change</c> rows (what sync sends). The file is in WAL mode: another program may have it open, one writes while
/// the other reads, and a second writer waits up to five seconds.
/// Spec: <c>docs/ODP-PROJECT-FILE-AND-SYNC.md</c> in the OSAS repository.
/// </summary>
public sealed class OdpFile : IDisposable
{
    public const string Format = "osas-project";
    public const int Version = 1;
    public const string Extension = ".odp";

    // The same tables as aad-odp's SCHEMA; a test opens a file the Rust side wrote and checks they match.
    const string Schema = @"
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS drawing (
    name TEXT PRIMARY KEY, ext TEXT NOT NULL, sha256 TEXT NOT NULL, bytes BLOB NOT NULL, revision INTEGER NOT NULL, created TEXT NOT NULL
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS drawing_state (drawing TEXT PRIMARY KEY, json TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS takeoff_header (slot INTEGER PRIMARY KEY, state TEXT NOT NULL, json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS takeoff_row (
    slot INTEGER NOT NULL, kind INTEGER NOT NULL, n INTEGER NOT NULL, json TEXT NOT NULL, PRIMARY KEY (slot, kind, n)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS project_doc (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS asset (
    id TEXT PRIMARY KEY, slot INTEGER, name TEXT NOT NULL, mime TEXT NOT NULL, sha256 TEXT NOT NULL, bytes BLOB NOT NULL
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS markup (
    id TEXT PRIMARY KEY, drawing TEXT NOT NULL, author TEXT NOT NULL, created TEXT NOT NULL, kind TEXT NOT NULL,
    json TEXT NOT NULL, text TEXT NOT NULL DEFAULT '', resolved INTEGER NOT NULL DEFAULT 0
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS history (id INTEGER PRIMARY KEY, created TEXT NOT NULL, label TEXT NOT NULL, bytes BLOB NOT NULL);
CREATE TABLE IF NOT EXISTS change (revision INTEGER NOT NULL, tbl TEXT NOT NULL, key TEXT NOT NULL, op TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS change_revision ON change (revision);";

    readonly SqliteConnection _db;

    OdpFile(SqliteConnection db) => _db = db;

    static string ConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 5 }.ToString();

    /// <summary>True when the file starts like SQLite (cheap check before <see cref="Open"/>).</summary>
    public static bool LooksLikeOdp(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            Span<byte> head = stackalloc byte[15];
            return f.Read(head) == 15 && System.Text.Encoding.ASCII.GetString(head) == "SQLite format 3";
        }
        catch { return false; }
    }

    /// <summary>Open an existing project file. Throws <see cref="InvalidDataException"/> when it is not one or is a newer version.</summary>
    public static OdpFile Open(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The project file was not found.", path);
        var db = new SqliteConnection(ConnectionString(path, SqliteOpenMode.ReadWrite));
        try
        {
            db.Open();
            var file = new OdpFile(db);
            string? format;
            try { format = file.MetaValue("format"); }
            catch (SqliteException) { throw new InvalidDataException($"{Path.GetFileName(path)} is not a project file (no meta table)."); }
            if (format != Format) throw new InvalidDataException($"{Path.GetFileName(path)} is not a project file (its format is {format ?? "missing"}).");
            if (int.TryParse(file.MetaValue("version"), out var v) && v > Version)
                throw new InvalidDataException($"{Path.GetFileName(path)} is project file version {v}; this program reads version {Version}.");
            file.Exec(Schema);                    // a file from an older version gets the tables it lacks
            return file;
        }
        catch (SqliteException ex)
        {
            db.Dispose();
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a project file: {ex.Message}");
        }
        catch { db.Dispose(); throw; }
    }

    /// <summary>Create a new project file (it must not exist).</summary>
    public static OdpFile Create(string path, string name)
    {
        if (File.Exists(path)) throw new IOException($"{path} already exists.");
        var db = new SqliteConnection(ConnectionString(path, SqliteOpenMode.ReadWriteCreate));
        db.Open();
        var file = new OdpFile(db);
        file.Exec("PRAGMA journal_mode=WAL;");
        file.Exec("PRAGMA synchronous=NORMAL;");
        file.Exec(Schema);
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{name}|{now}|{Environment.ProcessId}|{path}")))[..32].ToLowerInvariant();
        foreach (var (k, v) in new[] { ("format", Format), ("version", Version.ToString(CultureInfo.InvariantCulture)), ("project_id", id), ("name", name), ("created", now), ("revision", "0") })
            file.Exec("INSERT INTO meta (key, value) VALUES ($k, $v)", ("$k", k), ("$v", v));
        return file;
    }

    // ---- reads ----

    public OdpMeta Meta() => new(MetaValue("project_id") ?? "", MetaValue("name") ?? "", MetaValue("created") ?? "", Revision());

    /// <summary>The revision now. Cheap: poll this to learn that O-Draft (or another device) changed something.</summary>
    public long Revision() => long.TryParse(MetaValue("revision"), out var r) ? r : 0;

    public IReadOnlyList<string> DrawingNames() => Column("SELECT name FROM drawing ORDER BY created, name");

    public OdpDrawing? Drawing(string name)
    {
        using var cmd = Command("SELECT name, ext, sha256, bytes FROM drawing WHERE name = $n", ("$n", name));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new OdpDrawing(r.GetString(0), r.GetString(1), r.GetString(2), (byte[])r["bytes"]) : null;
    }

    public IReadOnlyList<OdpTakeoffHeader> TakeoffHeaders()
    {
        var list = new List<OdpTakeoffHeader>();
        using var cmd = Command("SELECT slot, state, json FROM takeoff_header ORDER BY slot");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new OdpTakeoffHeader((uint)r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public IReadOnlyList<OdpTakeoffRow> TakeoffRows(uint slot)
    {
        var list = new List<OdpTakeoffRow>();
        using var cmd = Command("SELECT kind, n, json FROM takeoff_row WHERE slot = $s ORDER BY kind, n", ("$s", (long)slot));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new OdpTakeoffRow(slot, (int)r.GetInt64(0), (uint)r.GetInt64(1), r.GetString(2)));
        return list;
    }

    /// <summary>The O-CoreOp project document, or null when the file has none yet (O-Draft made it).</summary>
    public string? ProjectDoc()
    {
        using var cmd = Command("SELECT json FROM project_doc WHERE id = 1");
        return cmd.ExecuteScalar() as string;
    }

    public (string Name, string Mime, byte[] Bytes)? Asset(string id)
    {
        using var cmd = Command("SELECT name, mime, bytes FROM asset WHERE id = $i", ("$i", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1), (byte[])r["bytes"]) : null;
    }

    public IReadOnlyList<string> AssetIds() => Column("SELECT id FROM asset ORDER BY id");

    /// <summary>What changed after revision <paramref name="since"/>: (revision, table, key, op), oldest first.</summary>
    public IReadOnlyList<(long Revision, string Table, string Key, string Op)> ChangesSince(long since)
    {
        var list = new List<(long, string, string, string)>();
        using var cmd = Command("SELECT revision, tbl, key, op FROM change WHERE revision > $s ORDER BY revision, rowid", ("$s", since));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    // ---- writes (each one revision) ----

    /// <summary>
    /// Bring a ShilpiDB take-off file (<c>.vdb</c>) into this project as one revision: headers and rows keep their slot, kind and number, the
    /// annotated PDF chunks of a drawing are joined into one asset, and O-CoreOp's project record becomes the project document.
    /// </summary>
    public long ImportVdb(string vdbPath)
    {
        var contents = ShilpiProjectFile.Parse(File.ReadAllBytes(vdbPath));
        return Write(rev =>
        {
            var pdf = new SortedDictionary<uint, SortedDictionary<uint, byte[]>>();
            foreach (var r in contents.Records)
            {
                var kind = r.Kind;
                var slot = (uint)((r.Id >> 32) & 0x00FF_FFFF);
                var n = (uint)(r.Id & 0xFFFF_FFFF);
                if (kind == 1)
                {
                    var json = System.Text.Encoding.UTF8.GetString(r.Payload);
                    var state = JsonNode.Parse(json)?["state"]?.GetValue<string>() ?? "complete";
                    Exec("INSERT INTO takeoff_header (slot, state, json) VALUES ($s, $st, $j) ON CONFLICT(slot) DO UPDATE SET state = $st, json = $j",
                        ("$s", (long)slot), ("$st", state), ("$j", json));
                    Log(rev, "takeoff_header", slot.ToString(CultureInfo.InvariantCulture), "put");
                }
                else if (kind is 3 or 4 or 5 or 6 or 7 or 8 or 10 or 11)
                {
                    Exec("INSERT INTO takeoff_row (slot, kind, n, json) VALUES ($s, $k, $n, $j) ON CONFLICT(slot, kind, n) DO UPDATE SET json = $j",
                        ("$s", (long)slot), ("$k", (long)kind), ("$n", (long)n), ("$j", System.Text.Encoding.UTF8.GetString(r.Payload)));
                    Log(rev, "takeoff_row", $"{slot}/{kind}/{n}", "put");
                }
                else if (kind == 9)
                {
                    if (!pdf.TryGetValue(slot, out var chunks)) pdf[slot] = chunks = new SortedDictionary<uint, byte[]>();
                    chunks[n] = r.Payload;
                }
            }
            foreach (var (slot, chunks) in pdf)
            {
                var bytes = chunks.Values.SelectMany(c => c).ToArray();
                var id = $"annotated-{slot}.pdf";
                Exec("INSERT INTO asset (id, slot, name, mime, sha256, bytes) VALUES ($i, $s, $i, 'application/pdf', $h, $b) " +
                     "ON CONFLICT(id) DO UPDATE SET slot = $s, sha256 = $h, bytes = $b",
                    ("$i", id), ("$s", (long)slot), ("$h", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()), ("$b", bytes));
                Log(rev, "asset", id, "put");
            }
            var project = ShilpiProjectFile.GetProject(contents);
            if (project is not null)
            {
                Exec("INSERT INTO project_doc (id, json) VALUES (1, $j) ON CONFLICT(id) DO UPDATE SET json = $j", ("$j", project));
                Log(rev, "project_doc", "1", "put");
            }
        });
    }

    /// <summary>Replace the project document; returns the new revision. Everything else in the file (the drawing, the take-off) is untouched.</summary>
    public long SetProjectDoc(string json)
    {
        _ = JsonNode.Parse(json) ?? throw new InvalidDataException("The project is not valid JSON.");
        return Write(rev =>
        {
            Exec("INSERT INTO project_doc (id, json) VALUES (1, $j) ON CONFLICT(id) DO UPDATE SET json = $j", ("$j", json));
            Log(rev, "project_doc", "1", "put");
        });
    }

    /// <summary>Store (or replace) a drawing's bytes as it is.</summary>
    public long SetDrawing(string name, string ext, byte[] bytes) => Write(rev =>
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Exec("INSERT INTO drawing (name, ext, sha256, bytes, revision, created) VALUES ($n, $e, $h, $b, $r, $c) " +
             "ON CONFLICT(name) DO UPDATE SET ext = $e, sha256 = $h, bytes = $b, revision = $r",
            ("$n", name), ("$e", ext.TrimStart('.').ToLowerInvariant()), ("$h", hash), ("$b", bytes), ("$r", rev),
            ("$c", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        Log(rev, "drawing", name, "put");
    });

    long Write(Action<long> body)
    {
        Exec("BEGIN IMMEDIATE");                  // takes the write lock now; another writer makes this wait, up to the timeout
        try
        {
            var rev = Revision() + 1;
            body(rev);
            Exec("UPDATE meta SET value = $v WHERE key = 'revision'", ("$v", rev.ToString(CultureInfo.InvariantCulture)));
            Exec("COMMIT");
            return rev;
        }
        catch
        {
            try { Exec("ROLLBACK"); } catch (SqliteException) { }
            throw;
        }
    }

    void Log(long rev, string table, string key, string op) =>
        Exec("INSERT INTO change (revision, tbl, key, op) VALUES ($r, $t, $k, $o)", ("$r", rev), ("$t", table), ("$k", key), ("$o", op));

    string? MetaValue(string key)
    {
        using var cmd = Command("SELECT value FROM meta WHERE key = $k", ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    IReadOnlyList<string> Column(string sql)
    {
        var list = new List<string>();
        using var cmd = Command(sql);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    SqliteCommand Command(string sql, params (string Name, object Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    void Exec(string sql, params (string Name, object Value)[] args)
    {
        using var cmd = Command(sql, args);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
