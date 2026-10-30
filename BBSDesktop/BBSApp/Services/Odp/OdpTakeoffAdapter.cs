// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aqc.CadImport;
using Office.Shared;

namespace BBSApp.Services.Odp;

/// <summary>
/// The take-off in a project file (<c>.osp</c>) in the record form the importer reads: headers and rows with the id <c>kind &lt;&lt; 56 | slot &lt;&lt; 32 | n</c>
/// and the annotated PDF of a drawing cut into chunk records.
/// </summary>
static class OdpTakeoffAdapter
{
    const int PdfChunk = 512 * 1024;

    public static List<TakeoffRecord> Records(string path)
    {
        using var odp = OdpFile.Open(path);
        var list = new List<TakeoffRecord>();
        static ulong Id(int kind, uint slot, uint n) => ((ulong)kind << 56) | ((ulong)slot << 32) | n;
        foreach (var h in odp.TakeoffHeaders())
        {
            list.Add(new TakeoffRecord(Id(1, h.Slot, 0), 0, 0, 0, 0, h.Json));
            foreach (var r in odp.TakeoffRows(h.Slot))
                list.Add(new TakeoffRecord(Id(r.Kind, r.Slot, r.N), 0, 0, 0, 0, r.Json));
        }
        foreach (var id in odp.AssetIds().Where(i => i.StartsWith("annotated-", StringComparison.Ordinal)))
        {
            var slotText = id["annotated-".Length..].Replace(".pdf", "", StringComparison.Ordinal);
            if (!uint.TryParse(slotText, out var slot) || odp.Asset(id) is not { } asset) continue;
            for (int n = 0, off = 0; off < asset.Bytes.Length; n++, off += PdfChunk)
            {
                var chunk = asset.Bytes.AsSpan(off, Math.Min(PdfChunk, asset.Bytes.Length - off));
                list.Add(new TakeoffRecord(Id(9, slot, (uint)n), 0, 0, 0, 0, null, Convert.ToHexString(chunk)));
            }
        }
        return list;
    }
}
