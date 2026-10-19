// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BBSApp.Services;

/// <summary>Renders the imported takeoff PDF page with mark overlays for report appendices.</summary>
public sealed record AnnotatedDrawing(byte[] PngBytes, float WidthPx, float HeightPx);

public static class TakeoffAnnotatedPdf
{
    public static string? ResolvePdfPath(ProjectStore store)
    {
        var stored = store.Takeoff.PdfPath;
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (Path.IsPathRooted(stored) && File.Exists(stored)) return stored;
        if (!string.IsNullOrWhiteSpace(store.FilePath))
        {
            var projDir = Path.GetDirectoryName(store.FilePath);
            if (!string.IsNullOrEmpty(projDir))
            {
                var combined = Path.GetFullPath(Path.Combine(projDir, stored));
                if (File.Exists(combined)) return combined;
            }
        }
        return File.Exists(stored) ? stored : null;
    }

    public static async Task<AnnotatedDrawing?> TryCaptureAsync(ProjectStore store)
    {
        var path = ResolvePdfPath(store);
        if (path is null) return null;
        if (store.Takeoff.Items.Count == 0 && string.IsNullOrWhiteSpace(store.Takeoff.PdfPath))
            return null;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var pdf = await PdfDocument.LoadFromFileAsync(file);
            if (pdf.PageCount == 0) return null;

            uint pageIndex = (uint)Math.Clamp(store.Takeoff.Page, 0, (int)pdf.PageCount - 1);
            using var pdfPage = pdf.GetPage(pageIndex);
            float destW = (float)(pdfPage.Size.Width * 2);
            float destH = (float)(pdfPage.Size.Height * 2);
            if (destW < 64 || destH < 64) return null;

            var opts = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(destW),
                DestinationHeight = (uint)Math.Round(destH)
            };

            using var stream = new InMemoryRandomAccessStream();
            await pdfPage.RenderToStreamAsync(stream, opts);
            stream.Seek(0);
            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            reader.Dispose();
            return new AnnotatedDrawing(bytes, destW, destH);
        }
        catch
        {
            return null;
        }
    }

    public static void Draw(IContainer container, ProjectStore store, AnnotatedDrawing? drawing)
    {
        if (drawing is null || drawing.PngBytes.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(store.Takeoff.PdfPath))
            {
                container.Column(col =>
                {
                    col.Item().Text("Annotated drawing").SemiBold().FontSize(11);
                    col.Item().Text("Imported PDF could not be rendered (file missing or unreadable). This drawing came from the old PDF take-off; take-off is now measured in O-Draft.")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            }
            return;
        }

        var items = store.Takeoff.Items.Where(i => i.Points.Count > 0).ToList();
        string svg = BuildMarksSvg(items, drawing.WidthPx, drawing.HeightPx);

        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("Annotated drawing (imported PDF + marks)").SemiBold().FontSize(11);
            col.Item().Text(
                    $"Page {store.Takeoff.Page + 1} · {items.Count} mark(s) — columns, beams, and all other takeoff elements.")
                .FontSize(7).FontColor(Colors.Grey.Darken1);
            col.Item().Text(LegendLine()).FontSize(6.5f).FontColor(Colors.Grey.Darken1);

            float aspect = drawing.HeightPx / Math.Max(1f, drawing.WidthPx);
            float boxH = Math.Clamp(480f * aspect, 220f, 520f);

            col.Item().Height(boxH).Layers(layers =>
            {
                layers.Layer().Image(drawing.PngBytes).FitUnproportionally();
                if (!string.IsNullOrEmpty(svg))
                    layers.Layer().Svg(size => ScaleSvg(svg, size.Width, size.Height, drawing.WidthPx, drawing.HeightPx));
            });
        });
    }

    private static string LegendLine() =>
        "Legend: Column · Beam · Pedestal · Lintel · Slab · Footing · Wall · finishes / civil (colour by category)";

    private static string ScaleSvg(string bodyInner, float outW, float outH, float srcW, float srcH)
    {
        // bodyInner is already a full <svg>...</svg> in source pixel space — rebuild with output size
        return bodyInner
            .Replace($"width='{Inv(srcW)}'", $"width='{Inv(outW)}'", StringComparison.Ordinal)
            .Replace($"height='{Inv(srcH)}'", $"height='{Inv(outH)}'", StringComparison.Ordinal);
    }

    private static string BuildMarksSvg(IReadOnlyList<TakeoffItem> items, float w, float h)
    {
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{Inv(w)}' height='{Inv(h)}' viewBox='0 0 {Inv(w)} {Inv(h)}'>");
        foreach (var it in items)
        {
            string stroke = ColorHex(it.Category, it.Committed);
            bool area = IsAreaTool(it.Tool);
            bool point = it.Tool.Equals("Point", StringComparison.OrdinalIgnoreCase)
                         || (it.Points.Count == 1 && !area);

            if (point)
            {
                var p = it.Points[0];
                sb.Append($"<circle cx='{Inv(p.X)}' cy='{Inv(p.Y)}' r='10' fill='{stroke}' stroke='#FFFFFF' stroke-width='1.5'/>");
                string shortMark = ShortMark(it.Mark);
                sb.Append($"<text x='{Inv(p.X)}' y='{Inv(p.Y + 3.5)}' text-anchor='middle' font-size='9' font-weight='600' fill='#FFFFFF' font-family='Calibri,sans-serif'>{Esc(shortMark)}</text>");
                sb.Append($"<text x='{Inv(p.X + 14)}' y='{Inv(p.Y - 2)}' font-size='11' fill='{stroke}' font-family='Calibri,sans-serif'>{Esc(it.Mark)}</text>");
                continue;
            }

            if (it.Points.Count < 2) continue;

            if (area)
            {
                if (it.Points.Count >= 3 && it.Tool.Equals("Area", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append($"<polygon points='{PointsAttr(it.Points)}' fill='rgba(0,120,215,0.14)' stroke='{stroke}' stroke-width='1.5'/>");
                }
                else
                {
                    var a = it.Points[0];
                    var b = it.Points[^1];
                    double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
                    double rw = Math.Max(1, Math.Abs(b.X - a.X)), rh = Math.Max(1, Math.Abs(b.Y - a.Y));
                    sb.Append($"<rect x='{Inv(x)}' y='{Inv(y)}' width='{Inv(rw)}' height='{Inv(rh)}' fill='rgba(0,120,215,0.11)' stroke='{stroke}' stroke-width='1.5'/>");
                }
                var mid = it.Points[it.Points.Count / 2];
                string areaNote = it.Fields.TryGetValue("area_m2", out var am) ? $" · {am} m²" : "";
                sb.Append($"<text x='{Inv(mid.X + 4)}' y='{Inv(mid.Y + 12)}' font-size='11' fill='{stroke}' font-family='Calibri,sans-serif'>{Esc(it.Mark + areaNote)}</text>");
            }
            else
            {
                sb.Append($"<polyline points='{PointsAttr(it.Points)}' fill='none' stroke='{stroke}' stroke-width='2'/>");
                var mid = it.Points[it.Points.Count / 2];
                sb.Append($"<text x='{Inv(mid.X + 4)}' y='{Inv(mid.Y - 4)}' font-size='11' fill='{stroke}' font-family='Calibri,sans-serif'>{Esc($"{it.Mark} · {it.LengthMm:0.#} mm")}</text>");
            }
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string PointsAttr(IEnumerable<TakeoffPoint> pts) =>
        string.Join(" ", pts.Select(p => $"{Inv(p.X)},{Inv(p.Y)}"));

    private static string ShortMark(string mark)
    {
        int last = mark.LastIndexOf('-');
        return last >= 0 && last < mark.Length - 1 ? mark[(last + 1)..] : mark;
    }

    private static bool IsAreaTool(string tool) =>
        tool.Equals("Area", StringComparison.OrdinalIgnoreCase)
        || tool.Equals("Rectangle", StringComparison.OrdinalIgnoreCase)
        || tool.Equals("Opening", StringComparison.OrdinalIgnoreCase);

    private static string ColorHex(string category, bool committed)
    {
        // Match TakeoffCanvas.BrushFor
        string hex = category.ToLowerInvariant() switch
        {
            "columns" or "column" or "rcc" => "#DC5050",
            "beams" or "beam" => "#C87828",
            "pedestals" or "pedestal" => "#DC5050",
            "lintels" or "lintel" => "#C87828",
            "slabs" or "slab" => "#508CDC",
            "footings" or "footing" => "#8C5A3C",
            "masonry" => "#B43C3C",
            "plaster" => "#64A064",
            "pcc" => "#787878",
            "earthwork" or "earth" => "#A0783C",
            "ssm" => "#64648C",
            "shuttering" => "#28A0B4",
            "flooring" => "#A050A0",
            "painting" or "paint" => "#5050C8",
            "waterproofing" => "#2878A0",
            "dpc" => "#786450",
            "screed" => "#8C8C64",
            "vdf" => "#64788C",
            "skirting" => "#A06478",
            "parapet" => "#648C78",
            "plinth_protection" => "#787864",
            "coping" => "#8C7864",
            "scale" => "#00C8C8",
            _ => "#0078D7"
        };
        if (!committed && hex.Length == 7)
            return hex + "C8"; // ~80% alpha
        return hex;
    }

    private static string Inv(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}