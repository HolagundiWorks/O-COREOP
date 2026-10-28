// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;

namespace BBSApp.Services;

/// <summary>
/// Propose plaster/paint areas from masonry (both faces) + RCC exposure.
/// Reconcile edits preserved by source_mark; Finalize writes Plaster.
/// Painting area always follows plaster (SyncPaintingFromPlaster).
/// </summary>
public static class FinishSurfacesCalculator
{
    public static void SyncPropose(ProjectStore store)
    {
        var prev = CaptureEdits(store.FinishPropose);
        store.FinishPropose.Clear();
        int i = 1;
        foreach (var row in Propose(store, prev))
        {
            if (string.IsNullOrWhiteSpace(Get(row, "mark")))
                row["mark"] = $"FN{i}";
            store.FinishPropose.Add(row);
            i++;
        }
    }

    public static void Finalize(ProjectStore store)
    {
        // Drop previous auto-finalized plaster; keep manual plaster rows.
        RemoveAuto(store.Plaster);

        string plasterThick = "12";
        string mortar = "1:4";

        foreach (var p in store.FinishPropose)
        {
            if (!Yes(Get(p, "include", "Yes"))) continue;
            double area = ParseD(Get(p, "area_m2"));
            if (area <= 0) continue;

            string src = Get(p, "source");
            string srcMark = Get(p, "source_mark");
            string level = Get(p, "level", "Lvl0");
            string notes = Get(p, "notes");

            store.Plaster.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mark"] = $"PL-{srcMark}",
                ["level"] = level,
                ["source"] = src,
                ["source_mark"] = srcMark,
                ["status"] = "final",
                ["area_m2"] = Inv(area),
                ["thickness"] = plasterThick,
                ["mortar_mix"] = mortar,
                ["faces"] = "1",
                ["length"] = "0",
                ["height"] = "0",
                ["deduct_rule"] = "None",
                ["notes"] = notes
            });
        }

        SyncPaintingFromPlaster(store);
        store.Notify();
    }

    /// <summary>
    /// Rebuild painting lines so area matches plaster qty (paint type/coats preserved).
    /// Manual painting rows (no auto/from_plaster source) are kept.
    /// </summary>
    public static void SyncPaintingFromPlaster(ProjectStore store)
    {
        var prefs = new Dictionary<string, (string PaintType, string Coats, string Location, string System)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var t in store.Painting)
        {
            string key = PaintLinkKey(t);
            if (key.Length == 0) continue;
            prefs[key] = (
                Get(t, "paint_type", "Emulsion"),
                Get(t, "coats", "2"),
                Get(t, "paint_location", "Inside walls"),
                Get(t, "paint_system", "2 coat primer + 3 coat putty + 2 coat paint"));
        }

        RemoveLinkedPainting(store.Painting);

        foreach (var p in store.Plaster)
        {
            double area = PlasterAreaM2(p);
            if (area <= 0) continue;

            string plasterMark = Get(p, "mark", "PL1");
            string srcMark = Get(p, "source_mark");
            if (srcMark.Length == 0) srcMark = plasterMark;
            string level = Get(p, "level", "Lvl0");
            string plasterSrc = Get(p, "source");
            string paintSrc = plasterSrc.StartsWith("auto_", StringComparison.OrdinalIgnoreCase)
                ? plasterSrc
                : "from_plaster";
            string key = $"{srcMark}|{level}";
            var (paintType, coats, location, system) = prefs.TryGetValue(key, out var pref)
                ? pref
                : ("Emulsion", "2", "Inside walls", "2 coat primer + 3 coat putty + 2 coat paint");

            store.Painting.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mark"] = $"PT-{srcMark}",
                ["level"] = level,
                ["source"] = paintSrc,
                ["source_mark"] = srcMark,
                ["status"] = "from_plaster",
                ["area_m2"] = Inv(Math.Round(area, 3)),
                ["paint_type"] = paintType,
                ["paint_location"] = location,
                ["paint_system"] = system,
                ["coats"] = coats,
                ["faces"] = "1",
                ["length"] = "0",
                ["height"] = "0",
                ["deduct_rule"] = "None",
                ["notes"] = $"From plaster {plasterMark}"
            });
        }
    }

    /// <summary>Same area rules used for plaster BOQ qty.</summary>
    public static double PlasterAreaM2(Dictionary<string, string> r)
    {
        double areaFromStore = ParseD(Get(r, "area_m2"));
        string src = Get(r, "source");
        if (areaFromStore > 0 && src.StartsWith("auto_", StringComparison.OrdinalIgnoreCase))
            return areaFromStore;
        if (areaFromStore > 0 && ParseD(Get(r, "length")) <= 0 && ParseD(Get(r, "height")) <= 0)
            return areaFromStore;

        double L = ParseD(Get(r, "length"));
        double H = ParseD(Get(r, "height"));
        // Geometry stored as mm in civil sheets.
        string rule = Get(r, "deduct_rule", "IS1200 plaster/paint");
        bool addJambs = Yes(Get(r, "add_jambs", "No"));
        var (_, _, netMm2, _) = CivilBoqCalculator.DeductFaceArea(L, H, r, rule, addJambs);
        double areaM2 = netMm2 / 1e6;
        int faces = ParseInt(Get(r, "faces"), 1);
        if (faces < 1) faces = 1;
        return areaM2 * faces;
    }

    private static string PaintLinkKey(Dictionary<string, string> t)
    {
        string srcMark = Get(t, "source_mark");
        string level = Get(t, "level", "Lvl0");
        if (srcMark.Length == 0) return "";
        return $"{srcMark}|{level}";
    }

    private static void RemoveLinkedPainting(ObservableCollection<Dictionary<string, string>> rows)
    {
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            string src = Get(rows[i], "source");
            string status = Get(rows[i], "status");
            if (src.StartsWith("auto_", StringComparison.OrdinalIgnoreCase)
                || src.Equals("from_plaster", StringComparison.OrdinalIgnoreCase)
                || status.Equals("from_plaster", StringComparison.OrdinalIgnoreCase))
                rows.RemoveAt(i);
        }
    }

    public static IEnumerable<Dictionary<string, string>> Propose(
        ProjectStore store,
        IReadOnlyDictionary<string, Dictionary<string, string>>? prevEdits = null)
    {
        var y = store.Yields;
        int wallFaces = Math.Max(1, y.WallPlasterFaces);
        int defSides = Clamp(y.DefaultColumnSidesExposed, 0, 4);
        bool defCeiling = y.DefaultPlasterCeiling;
        bool defBeamSoffit = y.DefaultBeamSoffit;

        foreach (var w in store.MasonryWalls)
        {
            string mark = Get(w, "mark", "MW");
            string level = MaterialsCalculator.RowLevel(w);
            string key = $"wall:{mark}:{level}";
            var prev = Prev(prevEdits, key, mark);

            double L = Mm(w, "length"), H = Mm(w, "height");
            string rule = Get(w, "deduct_rule", "IS1200 masonry");
            var (_, _, netMm2, note) = CivilBoqCalculator.DeductFaceArea(L, H, w, rule, addJambs: false);
            string wallType = Get(w, "wall_type");
            int fallbackFaces = wallType.Length > 0 ? 2 : wallFaces;
            int faces = ParseInt(Get(prev, "faces"), fallbackFaces);
            if (faces < 1) faces = fallbackFaces;
            string faceNote = wallType.Equals("external", StringComparison.OrdinalIgnoreCase) ? "1 external + 1 internal"
                : wallType.Equals("internal", StringComparison.OrdinalIgnoreCase) ? "2 internal faces"
                : $"{faces} face(s)";
            double netFace = netMm2 / 1e6;
            double area = netFace * faces;

            yield return BuildRow(
                mark: $"W-{mark}",
                level: level,
                source: "auto_wall",
                sourceMark: mark,
                area: area,
                notes: $"Wall · {faceNote} · {note}",
                prev: prev,
                extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["faces"] = faces.ToString(CultureInfo.InvariantCulture),
                    ["net_face_m2"] = Inv(netFace),
                    ["member_type"] = "Wall"
                });
        }

        foreach (var r in store.Columns.Concat(store.Pedestals))
        {
            string mark = Get(r, "mark", "C");
            string level = MaterialsCalculator.RowLevel(r);
            bool pedestal = store.Pedestals.Contains(r);
            string key = pedestal ? $"pedestal:{mark}:{level}" : $"column:{mark}:{level}";
            var prev = Prev(prevEdits, key, mark);

            double B = Mm(r, "width"), D = Mm(r, "depth"), H = Mm(r, "height");
            int sides = Clamp(ParseInt(Get(prev, "sides_exposed"), defSides), 0, 4);
            double full = ColumnPerimeterMm2(r, B, D, H);
            double area = full / 1e6 * (sides / 4.0);

            yield return BuildRow(
                mark: pedestal ? $"P-{mark}" : $"C-{mark}",
                level: level,
                source: pedestal ? "auto_pedestal" : "auto_column",
                sourceMark: mark,
                area: area,
                notes: $"Exposed {sides}/4 sides",
                prev: prev,
                extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sides_exposed"] = sides.ToString(CultureInfo.InvariantCulture),
                    ["member_type"] = pedestal ? "Pedestal" : "Column",
                    ["breadth"] = Inv(B),
                    ["depth"] = Inv(D),
                    ["height"] = Inv(H),
                    ["column_type"] = Get(r, "column_type", "Rectangular")
                });
        }

        foreach (var r in store.Beams.Concat(store.Lintels))
        {
            string mark = Get(r, "mark", "B");
            string level = MaterialsCalculator.RowLevel(r);
            bool lintel = store.Lintels.Contains(r);
            string key = lintel ? $"lintel:{mark}:{level}" : $"beam:{mark}:{level}";
            var prev = Prev(prevEdits, key, mark);

            double L = FirstMm(r, "span", "opening", "length");
            if (lintel && L <= 0)
            {
                double opening = Mm(r, "opening");
                double bearing = Mm(r, "bearing");
                if (opening > 0) L = opening + 2 * Math.Max(0, bearing);
            }
            double B = Mm(r, "width"), D = Mm(r, "depth");
            bool sides = Yes(Get(prev, "plaster_sides", "Yes"));
            bool soffit = Yes(Get(prev, "plaster_soffit", defBeamSoffit ? "Yes" : "No"));
            double areaMm2 = 0;
            if (sides && L > 0 && D > 0) areaMm2 += 2 * D * L;
            if (soffit && L > 0 && B > 0) areaMm2 += B * L;
            double area = areaMm2 / 1e6;

            yield return BuildRow(
                mark: lintel ? $"L-{mark}" : $"B-{mark}",
                level: level,
                source: lintel ? "auto_lintel" : "auto_beam",
                sourceMark: mark,
                area: area,
                notes: $"Sides {(sides ? "Y" : "N")} · Soffit {(soffit ? "Y" : "N")}",
                prev: prev,
                extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["plaster_sides"] = sides ? "Yes" : "No",
                    ["plaster_soffit"] = soffit ? "Yes" : "No",
                    ["member_type"] = lintel ? "Lintel" : "Beam",
                    ["length"] = Inv(L),
                    ["breadth"] = Inv(B),
                    ["depth"] = Inv(D)
                });
        }

        foreach (var r in store.Slabs)
        {
            string mark = Get(r, "mark", "S");
            string level = MaterialsCalculator.RowLevel(r);
            string key = $"slab:{mark}:{level}";
            var prev = Prev(prevEdits, key, mark);

            bool ceiling = Yes(Get(prev, "plaster_ceiling", defCeiling ? "Yes" : "No"));
            double Lx = Mm(r, "span_x"), Ly = Mm(r, "span_y");
            double area = ceiling && Lx > 0 && Ly > 0 ? (Lx * Ly) / 1e6 : 0;

            yield return BuildRow(
                mark: $"S-{mark}",
                level: level,
                source: "auto_slab",
                sourceMark: mark,
                area: area,
                notes: ceiling ? "Ceiling / soffit plaster" : "Ceiling plaster off",
                prev: prev,
                extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["plaster_ceiling"] = ceiling ? "Yes" : "No",
                    ["member_type"] = "Slab",
                    ["length"] = Inv(Lx),
                    ["breadth"] = Inv(Ly)
                });
        }
    }

    /// <summary>Recompute area_m2 on a propose row after exposure fields change.</summary>
    public static void RecalcArea(Dictionary<string, string> row)
    {
        string src = Get(row, "source");
        if (src is "auto_wall")
        {
            double netFace = ParseD(Get(row, "net_face_m2"));
            int faces = Math.Max(1, ParseInt(Get(row, "faces"), 2));
            row["area_m2"] = Inv(netFace * faces);
            row["notes"] = $"Wall · {faces} face(s)";
            return;
        }

        if (src is "auto_column" or "auto_pedestal")
        {
            double B = Mm(row, "breadth"), D = Mm(row, "depth"), H = Mm(row, "height");
            int sides = Clamp(ParseInt(Get(row, "sides_exposed"), 3), 0, 4);
            bool circular = Get(row, "column_type").Equals("Circular", StringComparison.OrdinalIgnoreCase);
            double full = circular
                ? (B > 0 || D > 0) && H > 0 ? Math.PI * (B > 0 ? B : D) * H : 0
                : B > 0 && D > 0 && H > 0 ? 2 * (B + D) * H : 0;
            double area = full / 1e6 * (sides / 4.0);
            row["area_m2"] = Inv(area);
            row["notes"] = $"Exposed {sides}/4 sides";
            return;
        }

        if (src is "auto_beam" or "auto_lintel")
        {
            double L = Mm(row, "length"), B = Mm(row, "breadth"), D = Mm(row, "depth");
            bool sides = Yes(Get(row, "plaster_sides", "Yes"));
            bool soffit = Yes(Get(row, "plaster_soffit", "No"));
            double areaMm2 = 0;
            if (sides && L > 0 && D > 0) areaMm2 += 2 * D * L;
            if (soffit && L > 0 && B > 0) areaMm2 += B * L;
            row["area_m2"] = Inv(areaMm2 / 1e6);
            row["notes"] = $"Sides {(sides ? "Y" : "N")} · Soffit {(soffit ? "Y" : "N")}";
            return;
        }

        if (src is "auto_slab")
        {
            bool ceiling = Yes(Get(row, "plaster_ceiling", "No"));
            double Lx = Mm(row, "length"), Ly = Mm(row, "breadth");
            double area = ceiling && Lx > 0 && Ly > 0 ? (Lx * Ly) / 1e6 : 0;
            row["area_m2"] = Inv(area);
            row["notes"] = ceiling ? "Ceiling / soffit plaster" : "Ceiling plaster off";
        }
    }

    private static Dictionary<string, string> BuildRow(
        string mark, string level, string source, string sourceMark,
        double area, string notes,
        Dictionary<string, string>? prev,
        Dictionary<string, string> extra)
    {
        string include = prev is not null ? Get(prev, "include", "Yes") : "Yes";
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mark"] = mark,
            ["level"] = level,
            ["source"] = source,
            ["source_mark"] = sourceMark,
            ["status"] = "proposed",
            ["include"] = include,
            ["area_m2"] = Inv(Math.Round(area, 3)),
            ["notes"] = notes
        };
        foreach (var kv in extra)
            row[kv.Key] = kv.Value;
        return row;
    }

    private static Dictionary<string, Dictionary<string, string>> CaptureEdits(
        ObservableCollection<Dictionary<string, string>> rows)
    {
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            string srcMark = Get(r, "source_mark");
            string src = Get(r, "source");
            string level = Get(r, "level");
            if (srcMark.Length == 0) continue;
            string key = $"{src}:{srcMark}:{level}";
            map[key] = r;
            map[srcMark] = r;
        }
        return map;
    }

    private static Dictionary<string, string>? Prev(
        IReadOnlyDictionary<string, Dictionary<string, string>>? map, string key, string mark)
    {
        if (map is null) return null;
        if (map.TryGetValue(key, out var r)) return r;
        if (map.TryGetValue(mark, out r)) return r;
        return null;
    }

    private static void RemoveAuto(ObservableCollection<Dictionary<string, string>> rows)
    {
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            string src = Get(rows[i], "source");
            if (src.StartsWith("auto_", StringComparison.OrdinalIgnoreCase))
                rows.RemoveAt(i);
        }
    }

    private static double ColumnPerimeterMm2(Dictionary<string, string> r, double B, double D, double H)
    {
        if (Get(r, "column_type", "Rectangular").Equals("Circular", StringComparison.OrdinalIgnoreCase))
        {
            double dia = B > 0 ? B : D;
            return dia > 0 && H > 0 ? Math.PI * dia * H : 0;
        }
        if (B > 0 && D > 0 && H > 0) return 2 * (B + D) * H;
        if (B > 0 && H > 0) return 4 * B * H;
        return 0;
    }

    private static string Get(Dictionary<string, string>? r, string key, string def = "")
    {
        if (r is null) return def;
        return r.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : def;
    }

    private static bool Yes(string v) =>
        v.Equals("Yes", StringComparison.OrdinalIgnoreCase)
        || v.Equals("Y", StringComparison.OrdinalIgnoreCase)
        || v.Equals("1", StringComparison.OrdinalIgnoreCase)
        || v.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static double Mm(Dictionary<string, string> r, string key) =>
        ParseD(Get(r, key));

    private static double FirstMm(Dictionary<string, string> r, params string[] keys)
    {
        foreach (var k in keys)
        {
            double v = Mm(r, k);
            if (v > 0) return v;
        }
        return 0;
    }

    private static double ParseD(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int ParseInt(string s, int def) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static int Clamp(int v, int lo, int hi) => Math.Min(hi, Math.Max(lo, v));

    private static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
