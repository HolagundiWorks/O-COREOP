// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;

namespace Office.Shared;

/// <summary>One record of a ShilpiDB file: id, bounding box and payload bytes.</summary>
public sealed record VdbRecord(ulong Id, double MinX, double MinY, double MaxX, double MaxY, byte[] Payload)
{
    public byte Kind => (byte)(Id >> 56);
}

/// <summary>The records of a <c>.vdb</c> file and the version it was written with.</summary>
public sealed class VdbContents
{
    public uint Version { get; set; } = ShilpiProjectFile.NewFileVersion;
    public List<VdbRecord> Records { get; } = new();
}

/// <summary>
/// An O-CoreOp project kept in a ShilpiDB file, the file that sits beside the drawing and that O-Draft records its take-off in.
/// The project (estimate, running bills, tasks, quantities, purchase orders, everything the project holds) is one JSON document
/// stored as records of kind <see cref="KindProject"/>, chunk <c>n</c> in the low 32 bits of the id. Every other record (the
/// take-off, the annotated drawing, anything another program wrote) is carried through a save untouched.
/// <para>
/// The layout is the one <c>shilpid</c> and O-Draft's <c>aad-vdb</c> read: <c>"SHILPIDB"</c>, <c>u32</c> version, <c>u32</c> count, then per record
/// <c>u64</c> id, four <c>f64</c> (min x, min y, max x, max y) and a <c>u32</c>-length payload, little-endian. A trailing spatial index written by
/// O-Draft is dropped on save: O-Draft rebuilds it when the file is loaded.
/// </para>
/// </summary>
public static class ShilpiProjectFile
{
    /// <summary>Kind of the O-CoreOp project record. 1 and 3 to 11 are take-off kinds, 0x40 to 0x45 are O-Draft's history, 12 to 255 otherwise reserved.</summary>
    public const byte KindProject = 0x60;
    /// <summary>A new file is written as version 1, which <c>shilpid</c> and O-Draft both read.</summary>
    public const uint NewFileVersion = 1;
    public const int MaxRecords = 1_000_000;
    public const int MaxPayload = 16 * 1024 * 1024;
    /// <summary>One project chunk; a project larger than this is split.</summary>
    public const int ChunkBytes = 4 * 1024 * 1024;

    static readonly byte[] Magic = Encoding.ASCII.GetBytes("SHILPIDB");

    public static ulong ProjectId(int chunk) => ((ulong)KindProject << 56) | (uint)chunk;

    public static VdbContents Parse(byte[] bytes)
    {
        if (bytes.Length < Magic.Length + 8 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a ShilpiDB file (it does not start with SHILPIDB).");
        int p = Magic.Length;
        uint version = BitConverter.ToUInt32(bytes, p); p += 4;
        if (version is < 1 or > 2) throw new InvalidDataException($"Unsupported ShilpiDB file version {version} (versions 1 and 2 are read).");
        uint count = BitConverter.ToUInt32(bytes, p); p += 4;
        if (count > MaxRecords) throw new InvalidDataException($"The file claims {count} records, more than the {MaxRecords} allowed.");
        var contents = new VdbContents { Version = version };
        for (uint i = 0; i < count; i++)
        {
            if (bytes.Length - p < 8 + 32 + 4) throw new InvalidDataException($"The file ends inside record {i + 1} of {count}.");
            ulong id = BitConverter.ToUInt64(bytes, p);
            double a = BitConverter.ToDouble(bytes, p + 8), b = BitConverter.ToDouble(bytes, p + 16),
                   c = BitConverter.ToDouble(bytes, p + 24), d = BitConverter.ToDouble(bytes, p + 32);
            p += 40;
            uint len = BitConverter.ToUInt32(bytes, p); p += 4;
            if (len > MaxPayload || bytes.Length - p < len) throw new InvalidDataException($"The file ends inside the payload of record {i + 1} of {count}.");
            contents.Records.Add(new VdbRecord(id, a, b, c, d, bytes.AsSpan(p, (int)len).ToArray()));
            p += (int)len;
        }
        return contents;
    }

    public static byte[] Serialize(VdbContents contents)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Magic);
        w.Write(contents.Version);
        w.Write((uint)contents.Records.Count);
        foreach (var r in contents.Records)
        {
            w.Write(r.Id); w.Write(r.MinX); w.Write(r.MinY); w.Write(r.MaxX); w.Write(r.MaxY);
            w.Write((uint)r.Payload.Length);
            w.Write(r.Payload);
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>True when the bytes start like a ShilpiDB file.</summary>
    public static bool LooksLikeVdb(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            Span<byte> head = stackalloc byte[8];
            return f.Read(head) == 8 && head.SequenceEqual(Magic);
        }
        catch { return false; }
    }

    /// <summary>The project document in the file, or null when the file holds none (a take-off file O-Draft has just made).</summary>
    public static string? GetProject(VdbContents contents)
    {
        var chunks = contents.Records.Where(r => r.Kind == KindProject).OrderBy(r => (uint)r.Id).ToList();
        if (chunks.Count == 0) return null;
        using var ms = new MemoryStream();
        foreach (var c in chunks) ms.Write(c.Payload);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Replace the project document, leaving every other record as it was.</summary>
    public static void SetProject(VdbContents contents, string json)
    {
        contents.Records.RemoveAll(r => r.Kind == KindProject);
        var bytes = Encoding.UTF8.GetBytes(json);
        int n = 0;
        for (int off = 0; off < bytes.Length || n == 0; off += ChunkBytes, n++)
        {
            int len = Math.Min(ChunkBytes, bytes.Length - off);
            contents.Records.Add(new VdbRecord(ProjectId(n), 0, 0, 0, 0, bytes.AsSpan(off, len).ToArray()));
        }
    }

    /// <summary>Read a file. A file that does not exist is an empty one.</summary>
    public static VdbContents Load(string path) => File.Exists(path) ? Parse(File.ReadAllBytes(path)) : new VdbContents();

    /// <summary>
    /// Write the file so a reader never sees half of it: to a temporary file beside it, then over it. The records are those of the
    /// file as it is now with the project replaced, so a take-off O-Draft recorded since this project was opened is kept.
    /// </summary>
    public static void SaveProject(string path, string json)
    {
        var contents = Load(path);
        SetProject(contents, json);
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp-" + Environment.ProcessId);
        File.WriteAllBytes(tmp, Serialize(contents));
        File.Move(tmp, path, overwrite: true);
    }
}
