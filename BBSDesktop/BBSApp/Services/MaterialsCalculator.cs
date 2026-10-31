// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BBSApp.Services;

/// <summary>One storey: height = slab-top to next slab-top (mm).</summary>
public sealed class LevelDef : INotifyPropertyChanged
{
    private string _id = "Lvl0";
    private string _name = "Plinth";
    private double _heightMm = 3200;
    private double _slabThicknessMm = 150;
    private double _beamDepthMm = 450;
    private bool _showMarker = true;

    public string Id { get => _id; set { if (_id == value) return; _id = value; OnPropertyChanged(); } }
    public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(); } }
    public double HeightMm
    {
        get => _heightMm;
        set { if (Math.Abs(_heightMm - value) < 0.01) return; _heightMm = value; OnPropertyChanged(); OnPropertyChanged(nameof(ColumnHeightMm)); }
    }
    public double SlabThicknessMm
    {
        get => _slabThicknessMm;
        set { if (Math.Abs(_slabThicknessMm - value) < 0.01) return; _slabThicknessMm = value; OnPropertyChanged(); OnPropertyChanged(nameof(ColumnHeightMm)); }
    }
    public double BeamDepthMm
    {
        get => _beamDepthMm;
        set { if (Math.Abs(_beamDepthMm - value) < 0.01) return; _beamDepthMm = value; OnPropertyChanged(); OnPropertyChanged(nameof(ColumnHeightMm)); }
    }

    /// <summary>When false, the storey is omitted from level markers and estimate level pickers.</summary>
    public bool ShowMarker
    {
        get => _showMarker;
        set { if (_showMarker == value) return; _showMarker = value; OnPropertyChanged(); }
    }

    /// <summary>Clear column shaft height for this storey.</summary>
    public double ColumnHeightMm => Math.Max(0, HeightMm - SlabThicknessMm - BeamDepthMm);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MixParts
{
    public string Grade { get; init; } = "M25";
    public double Cement { get; init; } = 1;
    public double Sand { get; init; } = 1;
    public double Aggregate { get; init; } = 2;
    public string Note { get; init; } = "";
}

public sealed class ConcreteLine
{
    public string Element { get; set; } = "";
    public string Mark { get; set; } = "";
    public string Level { get; set; } = "";
    public string Grade { get; set; } = "";
    public double VolumeM3 { get; set; }
    public double CementBags { get; set; }
    public double SandM3 { get; set; }
    public double AggregateM3 { get; set; }
}

public sealed class PoLine
{
    public string Category { get; set; } = "";
    public string Item { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Qty { get; set; }
    public string Notes { get; set; } = "";
}

public static class MaterialsCalculator
{
    // Dry volume factor for site-batched concrete (common practice).
    public const double DryFactor = 1.54;
    public const double CementBagKg = 50;
    public const double CementDensityKgPerM3 = 1440; // loose approx for volumetric

    public static MixParts MixFor(string grade) => grade switch
    {
        "M15" => new MixParts { Grade = "M15", Cement = 1, Sand = 2, Aggregate = 4, Note = "IS 456 nominal" },
        "M20" => new MixParts { Grade = "M20", Cement = 1, Sand = 1.5, Aggregate = 3, Note = "IS 456 nominal" },
        "M25" => new MixParts { Grade = "M25", Cement = 1, Sand = 1, Aggregate = 2, Note = "Nominal-style estimate" },
        "M30" => new MixParts { Grade = "M30", Cement = 1, Sand = 1, Aggregate = 2, Note = "Estimate — use design mix on site" },
        "M35" => new MixParts { Grade = "M35", Cement = 1, Sand = 0.75, Aggregate = 1.5, Note = "Estimate — design mix required" },
        "M40" => new MixParts { Grade = "M40", Cement = 1, Sand = 0.75, Aggregate = 1.5, Note = "Estimate — design mix required" },
        _ => new MixParts { Grade = grade, Cement = 1, Sand = 1, Aggregate = 2, Note = "Default estimate" }
    };

    public static (double bags, double sandM3, double aggM3) Split(double wetVolumeM3, string grade)
    {
        if (wetVolumeM3 <= 0) return (0, 0, 0);
        var m = MixFor(grade);
        double dry = wetVolumeM3 * DryFactor;
        double parts = m.Cement + m.Sand + m.Aggregate;
        if (parts <= 0) return (0, 0, 0);
        double cementM3 = dry * (m.Cement / parts);
        double sandM3 = dry * (m.Sand / parts);
        double aggM3 = dry * (m.Aggregate / parts);
        double bags = cementM3 * CementDensityKgPerM3 / CementBagKg;
        return (Round3(bags), Round3(sandM3), Round3(aggM3));
    }

    public static double Round3(double x) => Math.Round(x, 3);

    public static double Mm3ToM3(double mm3) => mm3 / 1e9;

    /// <summary>Legacy rows without level are treated as Lvl0.</summary>
    public static string RowLevel(Dictionary<string, string> r) =>
        r.TryGetValue("level", out var v) && !string.IsNullOrWhiteSpace(v) ? v : "Lvl0";

    [ThreadStatic] private static string? _phaseScope;

    /// <summary>
    /// Runs <paramref name="build"/> with only the rows whose <c>phase</c> (set in O-Draft) is <paramref name="phase"/>.
    /// A null or empty phase means every row. Every quantity calculator takes its rows through
    /// <see cref="FilterByLevels"/>, so one scope narrows them all; rows with no phase (including the derived finishes)
    /// are left out of a single phase.
    /// </summary>
    public static T WithPhase<T>(string? phase, Func<T> build)
    {
        var previous = _phaseScope;
        _phaseScope = string.IsNullOrWhiteSpace(phase) ? null : phase.Trim();
        try { return build(); }
        finally { _phaseScope = previous; }
    }

    private static bool InPhase(Dictionary<string, string> r) =>
        _phaseScope is null || (r.TryGetValue("phase", out var p) && string.Equals(p.Trim(), _phaseScope, StringComparison.OrdinalIgnoreCase));

    public static bool MatchesLevels(Dictionary<string, string> r, IReadOnlySet<string>? levels) =>
        (levels is null || levels.Count == 0 || levels.Contains(RowLevel(r))) && InPhase(r);

    public static IEnumerable<Dictionary<string, string>> FilterByLevels(
        IEnumerable<Dictionary<string, string>> rows, IReadOnlySet<string>? levels) =>
        levels is null || levels.Count == 0
            ? Array.Empty<Dictionary<string, string>>()
            : rows.Where(r => levels.Contains(RowLevel(r)) && InPhase(r));

    public static List<ConcreteLine> BuildConcreteBoq(ProjectStore store, IReadOnlySet<string>? levels = null)
    {
        var lines = new List<ConcreteLine>();
        // null → all project levels; empty set → nothing.
        if (levels is { Count: 0 }) return lines;
        var filter = levels ?? store.LevelIds().ToHashSet();

        foreach (var r in FilterByLevels(store.Columns, filter))
        {
            double w = F(r, "width"), d = F(r, "depth"), h = F(r, "height");
            double ph = F(r, "pedestal_h"), pw = F(r, "pedestal_w"), pd = F(r, "pedestal_d");
            string g = S(r, "concrete_grade", "M25");
            double vol = Mm3ToM3(w * d * h + (ph > 0 ? pw * pd * ph : 0));
            AddConcrete(lines, "Column", S(r, "mark"), RowLevel(r), g, vol);
        }
        foreach (var r in FilterByLevels(store.Beams, filter))
        {
            double vol = Mm3ToM3(F(r, "width") * F(r, "depth") * F(r, "span"));
            AddConcrete(lines, "Beam", S(r, "mark"), RowLevel(r), S(r, "concrete_grade", "M25"), vol);
        }
        foreach (var r in FilterByLevels(store.Slabs, filter))
        {
            double vol = Mm3ToM3(F(r, "span_x") * F(r, "span_y") * F(r, "thickness"));
            AddConcrete(lines, "Slab", S(r, "mark"), RowLevel(r), S(r, "concrete_grade", "M25"), vol);
        }
        foreach (var r in FilterByLevels(store.Footings, filter))
        {
            double vol = Mm3ToM3(F(r, "length_l") * F(r, "width_b") * F(r, "depth"));
            AddConcrete(lines, "Footing", S(r, "mark"), RowLevel(r), S(r, "concrete_grade", "M25"), vol);
        }
        foreach (var r in FilterByLevels(store.Walls, filter))
        {
            double heel = F(r, "heel"), toe = F(r, "toe");
            if (S(r, "include_toe", "Yes") != "Yes") toe = 0;
            double stem = F(r, "wall_length") * F(r, "stem_h") * F(r, "stem_t");
            double baseW = heel + toe + F(r, "stem_t");
            double bas = F(r, "wall_length") * baseW * F(r, "base_t");
            AddConcrete(lines, "Wall", S(r, "mark"), RowLevel(r), S(r, "concrete_grade", "M25"), Mm3ToM3(stem + bas));
        }
        return lines;
    }

    private static void AddConcrete(List<ConcreteLine> lines, string el, string mark, string level, string grade, double vol)
    {
        if (vol <= 0) return;
        var (bags, sand, agg) = Split(vol, grade);
        lines.Add(new ConcreteLine
        {
            Element = el, Mark = mark, Level = level, Grade = grade, VolumeM3 = Round3(vol),
            CementBags = bags, SandM3 = sand, AggregateM3 = agg
        });
    }

    public static List<PoLine> SteelPurchaseOrder(GenTable? summary)
    {
        var po = new List<PoLine>();
        if (summary?.Rows is null) return po;
        foreach (var row in summary.Rows)
        {
            if (row.Count < 4) continue;
            if (string.Equals(row[0], "TOTAL", StringComparison.OrdinalIgnoreCase)) continue;
            if (!double.TryParse(row[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var kg)) continue;
            po.Add(new PoLine
            {
                Category = "Steel",
                Item = $"TMT Ø{row[0]} mm",
                Unit = "kg",
                Qty = Math.Round(kg, 2),
                Notes = $"Nos {row[1]}, Length {row[2]} m"
            });
        }
        return po;
    }

    /// <summary>RCC / RMC concrete totals grouped by grade (m³).</summary>
    public static List<PoLine> ConcreteByGrade(IEnumerable<ConcreteLine> concrete, bool fromRmc = true)
    {
        var po = new List<PoLine>();
        foreach (var g in concrete.GroupBy(c => c.Grade).OrderBy(x => x.Key))
        {
            double vol = g.Sum(x => x.VolumeM3);
            if (vol <= 0) continue;
            po.Add(new PoLine
            {
                Category = fromRmc ? "RMC" : "Concrete",
                Item = fromRmc ? $"RMC {g.Key}" : $"Concrete {g.Key}",
                Unit = "m³",
                Qty = Round3(vol),
                Notes = fromRmc ? "Procure as ready-mix — no site batching split" : "Site / design mix volume"
            });
        }
        return po;
    }

    /// <summary>
    /// Cement / sand / aggregate from RCC volumes × grade mix.
    /// Pass <paramref name="includeConcreteSplit"/> = false when concrete is procured as RMC.
    /// </summary>
    public static List<PoLine> MaterialPurchaseOrder(IEnumerable<ConcreteLine> concrete, bool includeConcreteSplit = true)
    {
        var po = new List<PoLine>();
        if (!includeConcreteSplit) return po;

        foreach (var g in concrete.GroupBy(c => c.Grade))
        {
            double bags = g.Sum(x => x.CementBags);
            double sand = g.Sum(x => x.SandM3);
            double agg = g.Sum(x => x.AggregateM3);
            double vol = g.Sum(x => x.VolumeM3);
            var mix = MixFor(g.Key);
            po.Add(new PoLine { Category = "Cement", Item = $"OPC / PPC ({g.Key})", Unit = "bags (50kg)", Qty = Round3(bags), Notes = mix.Note });
            po.Add(new PoLine { Category = "Sand", Item = $"Fine aggregate ({g.Key})", Unit = "m³", Qty = sand, Notes = $"For {vol} m³ concrete" });
            po.Add(new PoLine { Category = "Aggregate", Item = $"Coarse aggregate ({g.Key})", Unit = "m³", Qty = agg, Notes = mix.Note });
        }
        return po;
    }

    private static double F(Dictionary<string, string> r, string k) =>
        r.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static string S(Dictionary<string, string> r, string k, string def = "") =>
        r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;
}
