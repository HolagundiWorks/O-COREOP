// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BBSApp.Services;

/// <summary>One page of an annotated drawing PDF, rendered to an image for the report.</summary>
public sealed record RenderedPage(byte[] PngBytes, float WidthPx, float HeightPx);

/// <summary>An annotated drawing fetched from ShilpiDB, with its pages rendered for the reports.</summary>
public sealed record RenderedAnnotatedPdf(StoredAnnotatedDrawing Entry, IReadOnlyList<RenderedPage> Pages, int TotalPages);

/// <summary>
/// The annotated drawing PDFs O-Draft generated (quantity lines, measurements, hatches) and O-CoreOp stored, as report appendices.
/// This replaces the page the removed PDF take-off panel drew. Each stored PDF is rendered page by page (Windows.Data.Pdf) to a PNG
/// and drawn on its own report page; nothing is redrawn or re-measured here.
/// </summary>
public static class StoredAnnotatedPdf
{
    /// <summary>At most this many pages of one PDF go in a report; the rest are named in a note.</summary>
    public const int MaxPagesPerPdf = 8;
    const double MaxPixels = 3200;

    /// <summary>Renders every stored annotated PDF of the project. A file that is missing or cannot be rendered is skipped (and absent from the result).</summary>
    public static async Task<List<RenderedAnnotatedPdf>> RenderAllAsync(ProjectStore store)
    {
        var result = new List<RenderedAnnotatedPdf>();
        foreach (var entry in store.Info.AnnotatedDrawings)
        {
            if (!entry.Exists) continue;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(entry.File);
                var pdf = await PdfDocument.LoadFromFileAsync(file);
                if (pdf.PageCount == 0) continue;
                var pages = new List<RenderedPage>();
                for (uint i = 0; i < Math.Min(pdf.PageCount, (uint)MaxPagesPerPdf); i++)
                {
                    using var page = pdf.GetPage(i);
                    double scale = Math.Min(2.0, MaxPixels / Math.Max(page.Size.Width, page.Size.Height));
                    var w = (uint)Math.Max(64, Math.Round(page.Size.Width * scale));
                    var h = (uint)Math.Max(64, Math.Round(page.Size.Height * scale));
                    using var stream = new InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = w, DestinationHeight = h });
                    stream.Seek(0);
                    var reader = new DataReader(stream.GetInputStreamAt(0));
                    await reader.LoadAsync((uint)stream.Size);
                    var bytes = new byte[stream.Size];
                    reader.ReadBytes(bytes);
                    reader.Dispose();
                    pages.Add(new RenderedPage(bytes, w, h));
                }
                result.Add(new RenderedAnnotatedPdf(entry, pages, (int)pdf.PageCount));
            }
            catch
            {
                // unreadable PDF: leave it out of the report; the Take-off page still lists it
            }
        }
        return result;
    }

    /// <summary>Draws the stored annotated drawings, each page on its own report page. Draws nothing when there are none.</summary>
    public static void Draw(IContainer container, IReadOnlyList<RenderedAnnotatedPdf>? drawings)
    {
        if (drawings is null || drawings.Count == 0) return;
        container.Column(col =>
        {
            col.Spacing(10);
            bool first = true;
            foreach (var d in drawings)
            {
                for (int i = 0; i < d.Pages.Count; i++)
                {
                    if (!first) col.Item().PageBreak();
                    first = false;
                    var page = d.Pages[i];
                    var entry = d.Entry;
                    col.Item().Column(c =>
                    {
                        c.Spacing(4);
                        c.Item().Text($"Annotated drawing: {entry.Drawing}").SemiBold().FontSize(11);
                        var bits = new List<string> { $"page {i + 1} of {d.TotalPages}" };
                        if (!string.IsNullOrWhiteSpace(entry.Generated)) bits.Add($"recorded by O-Draft {entry.Generated}");
                        if (!string.IsNullOrWhiteSpace(entry.Revision)) bits.Add($"drawing revision {entry.Revision[..Math.Min(8, entry.Revision.Length)]}");
                        c.Item().Text(string.Join(" · ", bits)).FontSize(7).FontColor(Colors.Grey.Darken1);
                        if (d.TotalPages > d.Pages.Count && i == d.Pages.Count - 1)
                            c.Item().Text($"Only the first {d.Pages.Count} of {d.TotalPages} pages are shown here; the full PDF is stored with the project.")
                                .FontSize(7).FontColor(Colors.Grey.Darken1);
                        float aspect = page.HeightPx / Math.Max(1f, page.WidthPx);
                        c.Item().Height(Math.Clamp(740f * aspect, 220f, 470f)).Image(page.PngBytes).FitArea();
                    });
                }
            }
        });
    }
}
