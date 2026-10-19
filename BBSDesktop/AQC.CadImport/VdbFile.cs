// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;

namespace Aqc.CadImport;

/// <summary>
/// Reads a ShilpiDB <c>.vdb</c> file directly (no server): <c>"SHILPIDB"</c>, <c>u32</c> version, <c>u32</c> record count, then per record
/// <c>u64</c> id, four <c>f64</c> box values (min x, min y, max x, max y) and a <c>u32</c>-length payload, all little-endian. Anything after the records
/// (O-Draft appends a spatial-grid index) is ignored.
/// <para>
/// Two programs write this layout with different version numbers: <c>shilpid</c> writes version 1 and refuses anything else, O-Draft's own file writer
/// writes version 2 (<c>aad-vdb</c>, record layout unchanged for take-off records, which are JSON text). So a take-off file O-Draft saved cannot be
/// opened by <c>shilpid</c>, but this reader takes both (found 2026-10-19 by reading a file O-Draft wrote).
/// </para>
/// </summary>
public static class VdbFile
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("SHILPIDB");
    public const int MinVersion = 1, MaxVersion = 2;
    public const int MaxRecords = 1_000_000;
    public const int MaxPayload = 16 * 1024 * 1024;

    public static List<ShilpiRecord> Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"The take-off database was not found: {path}");
        return Parse(File.ReadAllBytes(path));
    }

    public static List<ShilpiRecord> Parse(byte[] bytes)
    {
        int p = 0;
        if (bytes.Length < Magic.Length + 8 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a ShilpiDB database file (it does not start with SHILPIDB).");
        p += Magic.Length;
        uint version = U32(bytes, ref p);
        if (version < MinVersion || version > MaxVersion)
            throw new InvalidDataException($"Unsupported ShilpiDB file version {version} (this O-CoreOp reads versions {MinVersion} and {MaxVersion}).");
        uint count = U32(bytes, ref p);
        if (count > MaxRecords) throw new InvalidDataException($"The file claims {count} records, more than the {MaxRecords} allowed.");

        var list = new List<ShilpiRecord>((int)count);
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        for (uint i = 0; i < count; i++)
        {
            if (bytes.Length - p < 8 + 32 + 4) throw new InvalidDataException($"The file ends inside record {i + 1} of {count}.");
            ulong id = BitConverter.ToUInt64(bytes, p); p += 8;
            double minX = BitConverter.ToDouble(bytes, p), minY = BitConverter.ToDouble(bytes, p + 8),
                   maxX = BitConverter.ToDouble(bytes, p + 16), maxY = BitConverter.ToDouble(bytes, p + 24);
            p += 32;
            uint len = U32(bytes, ref p);
            if (len > MaxPayload || bytes.Length - p < len) throw new InvalidDataException($"The file ends inside the payload of record {i + 1} of {count}.");
            string? text = null, hex = null;
            var span = bytes.AsSpan(p, (int)len);
            try { text = utf8.GetString(span); }
            catch (DecoderFallbackException) { /* binary payload */ }
            // the hex form is only needed for what is not text (the annotated PDF chunks)
            if (text is null || ShilpiTakeoff.KindOf(id) == ShilpiTakeoff.KindAnnotatedChunk) hex = Convert.ToHexString(span);
            list.Add(new ShilpiRecord(id, minX, minY, maxX, maxY, text is not null && ShilpiTakeoff.KindOf(id) != ShilpiTakeoff.KindAnnotatedChunk ? text : null, hex));
            p += (int)len;
        }
        return list;
    }

    static uint U32(byte[] b, ref int p)
    {
        if (b.Length - p < 4) throw new InvalidDataException("The file is truncated.");
        var v = BitConverter.ToUInt32(b, p); p += 4; return v;
    }
}
