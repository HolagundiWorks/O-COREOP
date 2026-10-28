// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace BBSApp.Models;

public enum FieldKind { Text, Combo, Dia, Section, BarList }

public enum ExtraKind { Fixed, SpanFrac, Mesh }

public sealed class FieldDef
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public FieldKind Kind { get; init; } = FieldKind.Text;
    public string Default { get; init; } = "";
    public string[]? Options { get; init; }
    public bool OptionalDia { get; init; }
    public string? ShowWhenKey { get; init; }
    public string[]? ShowWhenValues { get; init; }
    public string? Hint { get; init; }
    /// <summary>Sheet group: "entry" (default) or "deductions".</summary>
    public string SheetTab { get; init; } = "entry";
}

public sealed class ExtraPanelDef
{
    public ExtraKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string StoreKey { get; init; } = "";
    public string? Hint { get; init; }
}

public sealed class ElementSpec
{
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string? TypeKey { get; init; }
    public List<FieldDef> Fields { get; init; } = new();
    public List<ExtraPanelDef> Extras { get; init; } = new();
    public string[] InputKeys { get; init; } = Array.Empty<string>();
    public bool HasChecks { get; init; }
    /// <summary>Civil quantity take-off (no BBS steel engine).</summary>
    public bool IsCivilBoq { get; init; }
    /// <summary>Sheet is derived from RCC concrete members — no manual geometry entry.</summary>
    public bool IsComputedFromRcc { get; init; }
    /// <summary>Finish reconcile sheet (walls + RCC exposure) before Finalize to plaster/paint.</summary>
    public bool IsFinishReconcile { get; init; }
}

/// <summary>Field labels follow IS 456 : 2000 nomenclature (and IS 2502 for detailing).</summary>
public static class ElementSpecs
{
    public static FieldDef Sec(string title) => new() { Key = "_sec_" + title, Label = title, Kind = FieldKind.Section };
    public static FieldDef Text(string key, string label, string def, string? hint = null) =>
        new() { Key = key, Label = label, Kind = FieldKind.Text, Default = def, Hint = hint };
    public static FieldDef Combo(string key, string label, string[] opts, string def, string? hint = null) =>
        new() { Key = key, Label = label, Kind = FieldKind.Combo, Options = opts, Default = def, Hint = hint };
    public static FieldDef Dia(string key, string label, string def, bool optional = false, string? hint = null) =>
        new() { Key = key, Label = label, Kind = FieldKind.Dia, Default = def, OptionalDia = optional, Hint = hint };
    public static FieldDef BarList(string key, string label, string def, string? hint = null) =>
        new() { Key = key, Label = label, Kind = FieldKind.BarList, Default = def, Hint = hint };
    public static FieldDef When(FieldDef f, string key, params string[] values)
    {
        f = new FieldDef
        {
            Key = f.Key, Label = f.Label, Kind = f.Kind, Default = f.Default,
            Options = f.Options, OptionalDia = f.OptionalDia, Hint = f.Hint,
            SheetTab = f.SheetTab,
            ShowWhenKey = key, ShowWhenValues = values
        };
        return f;
    }

    public static FieldDef Tab(FieldDef f, string sheetTab) => new()
    {
        Key = f.Key, Label = f.Label, Kind = f.Kind, Default = f.Default,
        Options = f.Options, OptionalDia = f.OptionalDia, Hint = f.Hint,
        ShowWhenKey = f.ShowWhenKey, ShowWhenValues = f.ShowWhenValues,
        SheetTab = sheetTab
    };

    public static ElementSpec Columns() => new()
    {
        Kind = "columns", Title = "Columns",
        Subtitle = "IS 456 Cl. 26.5.3.2 — Circular / Square / Rectangular sections with matching ties.",
        TypeKey = "column_type",
        InputKeys = new[] { "mark", "nos", "level", "column_type", "width", "depth", "bars", "tie_type" },
        HasChecks = true,
        Extras = new()
        {
            new() { Kind = ExtraKind.Fixed, Title = "Additional bars (fixed length)", StoreKey = "extra_fixed",
                Hint = "Extra / curtailment bars — each group is its own BBS line (φ · nos · length)." },
        },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "C1", "Member mark — C1, C2…"),
            Text("nos", "Nos", "1", "Identical columns with this section — Generate expands to C1,C2…"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0", "Floor / storey of the member"),
            Combo("column_type", "Column type",
                new[] { "Circular", "Square", "Rectangular" }, "Rectangular",
                "Section shape — controls available tie arrangements"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25", "Characteristic compressive strength"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe415", "Fe500", "Fe550" }, "Fe500", "For Ld / lap (IS 456 Cl. 26.2)"),
            Sec("Cross-section (mm)"),
            Text("width", "Breadth b / Ø", "300", "b for rect/square · diameter Ø for circular"),
            Text("depth", "Overall depth D", "450", "Square: same as b · Circular: unused"),
            Text("height", "Clear height ℓ (mm)", "2600", "From Levels (storey − slab − beam) — not edited on sheet"),
            Text("cover", "Nominal cover (mm)", "40", "From project defaults (IS 456 Cl. 26.4)"),
            Sec("Lateral ties (Cl. 26.5.3.2)"),
            Dia("stirrup_dia", "Tie dia. φ", "8", false, "Diameter of lateral ties"),
            Text("spacing", "Spacing of ties (mm)", "150", "Pitch — closer near ends for confinement"),
            Combo("hook_angle", "Standard hook", new[] { "90", "135", "180" }, "135", "IS 2502 hook angle"),
            Combo("tie_type", "Tie arrangement",
                new[]
                {
                    "Auto", "Closed", "Open Ties", "U-Ties", "Group Ties",
                    "Cross Ties", "Diagonal Ties"
                },
                "Auto",
                "Options filtered by column type"),
            Sec("Longitudinal reinforcement"),
            BarList("bars", "Main bars (add each φ separately)", "16:8",
                "One row per diameter — e.g. Ø20×4 and Ø16×4 as two lines (laid out symmetrically)"),
            Sec("Lap splice (IS 456 Cl. 26.2.5)"),
            Combo("provide_lap", "Provide compression lap", new[] { "No", "Yes" }, "No",
                "Default from Settings — length = max(Ld_c, 24φ)"),
            When(Text("lap_nos", "Lap nos (0 = all bars)", "0"), "provide_lap", "Yes"),
            Sec("Phases (set in O-Draft)"),
            Text("phase", "Est. phase", "", "Estimation phase, picked in O-Draft while measuring"),
            Text("bill_phase", "Bill phase", "", "Running bill phase, picked in O-Draft while measuring"),
        }
    };

    public static ElementSpec Pedestals() => new()
    {
        Kind = "pedestals", Title = "Pedestals",
        Subtitle = "Plinth pedestals — separate from storey columns (IS 456 practice).",
        TypeKey = "column_type",
        InputKeys = new[] { "mark", "nos", "level", "width", "depth", "height", "bars" },
        HasChecks = true,
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "P1", "P1, P2…"),
            Text("nos", "Nos", "1", "Identical pedestals — Generate expands marks"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("column_type", "Section", new[] { "Square", "Rectangular", "Circular" }, "Square"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe415", "Fe500", "Fe550" }, "Fe500"),
            Sec("Geometry (mm)"),
            Text("width", "Breadth b / Ø", "600"),
            Text("depth", "Depth D", "600"),
            Text("height", "Height H", "600"),
            Text("cover", "Nominal cover (mm)", "50"),
            Sec("Ties"),
            Dia("stirrup_dia", "Tie dia. φ", "8"),
            Text("spacing", "Tie spacing (mm)", "150"),
            Combo("hook_angle", "Standard hook", new[] { "90", "135", "180" }, "135"),
            Combo("tie_type", "Tie arrangement", new[] { "Auto", "Closed" }, "Auto"),
            Sec("Main bars"),
            BarList("bars", "Main bars", "12:4"),
            Combo("provide_lap", "Provide compression lap", new[] { "No", "Yes" }, "No"),
        }
    };

    public static ElementSpec Lintels() => new()
    {
        Kind = "lintels", Title = "Lintels",
        Subtitle = "Opening lintels — span = clear opening + bearings (detailed as short beams).",
        InputKeys = new[] { "mark", "nos", "level", "opening", "bearing", "width", "depth" },
        HasChecks = true,
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "L1", "L1, L2…"),
            Text("nos", "Nos", "1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe415", "Fe500", "Fe550" }, "Fe500"),
            Sec("Geometry (mm)"),
            Text("opening", "Clear opening (mm)", "900"),
            Text("bearing", "Bearing each side (mm)", "150"),
            Text("span", "Effective span L (mm)", "1200", "Usually opening + 2×bearing — auto-filled if blank"),
            Text("width", "Width b (mm)", "230"),
            Text("depth", "Overall depth D (mm)", "150"),
            Text("cover", "Nominal cover (mm)", "25"),
            Sec("Stirrups"),
            Dia("stirrup_dia", "Stirrup dia. φ", "8"),
            Text("spacing_support", "Spacing s1 (mm)", "100"),
            Text("spacing_middle", "Spacing s2 (mm)", "150"),
            Combo("legs", "No. of legs", new[] { "2", "4" }, "2"),
            Combo("hook_angle", "Standard hook", new[] { "90", "135", "180" }, "135"),
            Sec("Main steel"),
            BarList("hanger_bars", "Hanger bars", "8:2"),
            Combo("top_bar_type", "Top main layout", new[] { "Full Span", "At Support" }, "Full Span"),
            BarList("top_bars", "Top bars", "10:2"),
            BarList("bottom_bars", "Bottom bars", "12:2"),
            Combo("end_anchorage", "End anchorage",
                new[] { "Straight Ld", "90 Hook", "180 Hook" }, "90 Hook"),
            Combo("provide_lap", "Provide tension lap", new[] { "None", "Tension" }, "None"),
            Sec("Phases (set in O-Draft)"),
            Text("phase", "Est. phase", "", "Estimation phase, picked in O-Draft while measuring"),
            Text("bill_phase", "Bill phase", "", "Running bill phase, picked in O-Draft while measuring"),
        }
    };

    public static ElementSpec Beams() => new()
    {
        Kind = "beams", Title = "Beams",
        Subtitle = "IS 456 Cl. 26.5.1 — top/bottom main bars, stirrups (2/4-leg, s1/s2), side-face Cl. 26.5.1.3.",
        InputKeys = new[] { "mark", "nos", "beam_type", "level", "span", "width", "depth", "hanger_bars", "top_bars", "bottom_bars" },
        HasChecks = true,
        Extras = new()
        {
            new() { Kind = ExtraKind.Fixed, Title = "Additional bars (fixed length)", StoreKey = "extra_fixed",
                Hint = "Each add is its own BBS line — φ · nos · cutting length (mm)." },
            new() { Kind = ExtraKind.SpanFrac, Title = "Additional bars (fraction of span)", StoreKey = "extra_span",
                Hint = "Each add is its own BBS line — length = fraction × span." },
        },
        Fields = new()
        {
            Sec("Identification & geometry"),
            Text("mark", "Mark", "RB1", "RB# floor beams · PB# plinth beams"),
            Text("nos", "Nos", "1", "Identical beams — Generate expands RB1,RB2… / PB1,PB2…"),
            Combo("beam_type", "Beam type", new[] { "RB", "PB" }, "RB",
                "RB = regular/floor beam · PB = plinth beam"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("span", "Effective span L (mm)", "4000", "Clear span between supports (estimate)"),
            Text("width", "Width b (mm)", "230"),
            Text("depth", "Overall depth D (mm)", "450", "d ≈ D − cover (effective depth)"),
            Text("cover", "Nominal cover / d′ (mm)", "25", "From project defaults"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe415", "Fe500", "Fe550" }, "Fe500"),
            Sec("Stirrups (shear reinforcement)"),
            Dia("stirrup_dia", "Stirrup dia. φ", "8"),
            Text("spacing_support", "Spacing s1 near support (mm)", "100", "Closer spacing in support shear zone (~2d each end)"),
            Text("spacing_middle", "Spacing s2 at mid-span (mm)", "150", "Wider spacing in middle zone"),
            Combo("legs", "No. of legs", new[] { "2", "4" }, "2", "2-leg closed · 4-leg = closed + crosstie"),
            Combo("hook_angle", "Standard hook", new[] { "90", "135", "180" }, "135", "IS 2502 — 135° hooks typically 10d"),
            Sec("Main flexural reinforcement"),
            BarList("hanger_bars", "Hanger bars", "12:2",
                "One row per φ — corner bars that support stirrups (full span)"),
            Combo("top_bar_type", "Top main layout", new[] { "At Support", "Full Span" }, "At Support",
                "Hogging / negative moment steel"),
            BarList("top_bars", "Top main bars", "16:2",
                "One row per φ — additional top steel (excl. hangers)"),
            BarList("bottom_bars", "Bottom main bars", "16:3, 20:2",
                "One row per φ — e.g. Ø16×3 and Ø20×2 as two lines"),
            Sec("End anchorage & lap (IS 456 Cl. 26.2)"),
            Combo("end_anchorage", "End anchorage",
                new[] { "Straight Ld", "90 Hook", "180 Hook" }, "Straight Ld",
                "Straight embedment reduced by Cl. 26.2.2 hook credit when hooked"),
            Combo("provide_lap", "Provide tension lap", new[] { "None", "Tension" }, "None",
                "Opt-in — length = max(Ld, 30φ) on bottom steel"),
            When(Text("lap_nos", "Lap nos (0 = all bottom)", "0"), "provide_lap", "Tension"),
            Sec("Side-face / distributor (Cl. 26.5.1.3)"),
            Dia("skin_dia", "Side-face bar φ", "", true, "When D > 750 mm — crack control"),
            Text("skin_nos", "Bars per face (nos)", "", "Distributor bars each side face"),
            Text("skin_spacing", "Side-face spacing (mm)", "", "Used if nos blank — pitch along depth"),
        }
    };

    public static ElementSpec Stairs() => new()
    {
        Kind = "stairs", Title = "Staircase",
        Subtitle = "Waist slab + landing steel — going, riser, main bars along slope (IS 456 practice).",
        InputKeys = new[] { "mark", "level", "n_risers", "going", "riser", "waist_t", "flight_width" },
        HasChecks = true,
        Extras = new()
        {
            new() { Kind = ExtraKind.Fixed, Title = "Additional bars (fixed length)", StoreKey = "extra_fixed",
                Hint = "Extra bars with fixed cutting length (mm)." },
        },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "ST1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe415", "Fe500", "Fe550" }, "Fe500"),
            Sec("Flight geometry (mm)"),
            Text("n_risers", "No. of risers", "12", "Risers in one flight"),
            Text("going", "Going / tread (mm)", "250", "Horizontal tread depth"),
            Text("riser", "Riser (mm)", "150", "Vertical rise per step"),
            Text("waist_t", "Waist thickness (mm)", "150", "Inclined slab thickness"),
            Text("flight_width", "Flight width (mm)", "1200"),
            Text("cover", "Nominal cover (mm)", "20", "IS 456 Cl. 26.4"),
            Text("n_flights", "No. of identical flights", "1", "Dog-legged: typically 2"),
            Sec("Landings (mm)"),
            Text("landing_len", "Landing length (mm)", "1200", "Along run — each landing"),
            Text("landing_width", "Landing width (mm)", "", "Blank = flight width"),
            Text("landing_t", "Landing thickness (mm)", "150"),
            Sec("Waist main steel (along slope)"),
            Dia("main_dia", "Main bar φ", "12", false, "Bottom tension steel in waist"),
            Text("main_spacing", "Main bar spacing (mm)", "150", "Across flight width"),
            Sec("Distribution steel (across slope)"),
            Dia("dist_dia", "Distribution φ", "8"),
            Text("dist_spacing", "Distribution spacing (mm)", "200", "Along inclined length"),
            Sec("Landing mesh (optional)"),
            Dia("landing_dia", "Landing bar φ", "10", true),
            Text("landing_spacing", "Landing spacing (mm)", "150", "Both ways if φ given"),
        }
    };

    public static ElementSpec Slabs() => new()
    {
        Kind = "slabs", Title = "Slabs",
        Subtitle = "IS 456 Cl. 26.5.2 — main & distribution steel; bent-up bars per IS 2502.",
        TypeKey = "slab_type",
        InputKeys = new[] { "mark", "level", "span_x", "span_y", "thickness", "slab_type" },
        HasChecks = true,
        Extras = new()
        {
            new() { Kind = ExtraKind.Fixed, Title = "Additional bars (fixed length)", StoreKey = "extra_fixed",
                Hint = "Extra bars with fixed cutting length (mm)." },
            new() { Kind = ExtraKind.Mesh, Title = "Additional mesh (length × spacing)", StoreKey = "extra_mesh",
                Hint = "Strip of bars: length along strip × spacing." },
        },
        Fields = new()
        {
            Sec("Identification & type"),
            Text("mark", "Mark", "S1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("slab_type", "Slab type", new[] { "One-Way", "Two-Way" }, "Two-Way", "ly/lx ≤ 2 → one-way; else two-way"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe250", "Fe415", "Fe500", "Fe550" }, "Fe415"),
            Sec("Geometry (mm)"),
            Text("span_x", "Shorter span ℓx", "3000", "Shorter clear / effective span"),
            Text("span_y", "Longer span ℓy", "4500", "Longer clear / effective span"),
            Text("thickness", "Overall thickness D", "125"),
            Text("cover", "Nominal cover (mm)", "20", "IS 456 Cl. 26.4"),
            Sec("Main reinforcement — shorter span (ℓx)"),
            Dia("dia_x", "Main bar φ (ℓx)", "10"),
            Text("spacing_x", "Spacing of main bars (mm)", "150"),
            When(Sec("Main reinforcement — longer span (ℓy, two-way)"), "slab_type", "Two-Way"),
            When(Sec("Distribution reinforcement (ℓy, one-way)"), "slab_type", "One-Way"),
            Dia("dia_y", "Bar φ (ℓy)", "10"),
            Text("spacing_y", "Spacing (ℓy) (mm)", "150"),
            Sec("Bent-up bars (IS 2502)"),
            Combo("crank_count", "No. of bent-ups / bar", new[] { "0", "1", "2" }, "0"),
            Text("crank_rise", "Rise of bent-up (mm)", "", "Blank = D − 2 × nominal cover"),
            Sec("Phases (set in O-Draft)"),
            Text("phase", "Est. phase", "", "Estimation phase, picked in O-Draft while measuring"),
            Text("bill_phase", "Bill phase", "", "Running bill phase, picked in O-Draft while measuring"),
        }
    };

    public static ElementSpec Footings() => new()
    {
        Kind = "footings", Title = "Footings",
        Subtitle = "IS 456 Sec. 34 — isolated / combined / raft footing reinforcement.",
        TypeKey = "footing_type",
        InputKeys = new[] { "mark", "level", "footing_type", "length_l", "width_b", "depth" },
        HasChecks = true,
        Extras = new()
        {
            new() { Kind = ExtraKind.Fixed, Title = "Additional bars (fixed length)", StoreKey = "extra_fixed" },
        },
        Fields = new()
        {
            Sec("Identification & type"),
            Text("mark", "Mark", "F1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("footing_type", "Type of footing",
                new[] { "Isolated", "Stepped", "Double", "Strip", "Raft" }, "Isolated"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe415", "Fe500", "Fe550" }, "Fe500"),
            Sec("Plan & thickness (mm)"),
            Text("length_l", "Length L", "2000"),
            Text("width_b", "Breadth B", "2000"),
            Text("depth", "Overall thickness D", "500"),
            Text("cover", "Nominal cover (mm)", "50", "IS 456 Table 16 — footing"),
            When(Sec("Column size on footing"), "footing_type", "Isolated", "Stepped", "Double"),
            When(Text("col_dim_l", "Column size along L (mm)", "400"), "footing_type", "Isolated", "Stepped", "Double"),
            When(Text("col_dim_b", "Column size along B (mm)", "400"), "footing_type", "Isolated", "Stepped", "Double"),
            When(Sec("Second column (combined footing)"), "footing_type", "Double"),
            When(Text("col2_dim_l", "Col. 2 size along L (mm)", ""), "footing_type", "Double"),
            When(Text("col2_dim_b", "Col. 2 size along B (mm)", ""), "footing_type", "Double"),
            When(Sec("Stepped footing"), "footing_type", "Stepped"),
            When(Text("n_steps", "Number of steps", "2"), "footing_type", "Stepped"),
            When(Text("step_height", "Step height (mm)", "", "Blank = D ÷ n"), "footing_type", "Stepped"),
            When(Text("top_length", "Top plan L (mm)", "", "Blank = column L"), "footing_type", "Stepped"),
            When(Text("top_width", "Top plan B (mm)", "", "Blank = column B"), "footing_type", "Stepped"),
            Sec("Bottom reinforcement"),
            Dia("dia_l", "Bottom bars φ (along L)", "12"),
            Text("spacing_l", "Spacing along L (mm)", "150"),
            Dia("dia_b", "Bottom bars φ (along B)", "12"),
            Text("spacing_b", "Spacing along B (mm)", "150"),
            Sec("Top reinforcement (optional)"),
            Dia("top_dia_l", "Top bars φ (along L)", "", true),
            Text("top_spacing_l", "Top spacing along L (mm)", ""),
            Dia("top_dia_b", "Top bars φ (along B)", "", true),
            Text("top_spacing_b", "Top spacing along B (mm)", ""),
        }
    };

    public static ElementSpec Walls() => new()
    {
        Kind = "walls", Title = "Retaining walls",
        Subtitle = "Stem & base slab reinforcement — tension steel on selected face (IS 456).",
        TypeKey = "include_toe",
        InputKeys = new[] { "mark", "level", "wall_length", "stem_h", "stem_t" },
        HasChecks = true,
        Extras = new()
        {
            new() { Kind = ExtraKind.Fixed, Title = "Additional bars (fixed length)", StoreKey = "extra_fixed" },
        },
        Fields = new()
        {
            Sec("Identification & geometry"),
            Text("mark", "Mark", "RW1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("wall_length", "Length of wall (mm)", "5000"),
            Text("stem_h", "Stem height (mm)", "3000"),
            Text("stem_t", "Stem thickness (mm)", "250"),
            Text("heel", "Heel projection (mm)", "1500"),
            Combo("include_toe", "Provide toe", new[] { "Yes", "No" }, "Yes"),
            When(Text("toe", "Toe projection (mm)", "600"), "include_toe", "Yes"),
            Text("base_t", "Base slab thickness (mm)", "400"),
            Text("cover", "Nominal cover (mm)", "50"),
            Sec("Materials & tension face"),
            Combo("concrete_grade", "Grade of concrete (fck)", new[] { "M20", "M25", "M30", "M35", "M40" }, "M25"),
            Combo("steel_grade", "Grade of steel (fy)", new[] { "Fe415", "Fe500", "Fe550" }, "Fe500"),
            Combo("tension_face", "Tension face (stem)", new[] { "Front", "Back" }, "Front", "Face with main vertical steel"),
            Sec("Stem reinforcement"),
            Dia("stem_v_dia", "Main vertical φ (stem)", "12"),
            Text("stem_v_spacing", "Spacing of main vertical (mm)", "150"),
            Dia("stem_v_back_dia", "Secondary face vertical φ", "10", true),
            Text("stem_v_back_spacing", "Sec. vertical spacing (mm)", "200"),
            Dia("stem_h_dia", "Horizontal (distrib.) φ", "10"),
            Text("stem_h_spacing", "Horizontal spacing (mm)", "200"),
            Sec("Base slab reinforcement"),
            Dia("base_l_dia", "Longl. bars φ (base)", "12"),
            Text("base_l_spacing", "Longl. spacing (mm)", "150"),
            Dia("base_b_dia", "Transverse bars φ (base)", "12"),
            Text("base_b_spacing", "Transverse spacing (mm)", "150"),
            Sec("Links (optional)"),
            Dia("link_dia", "Link φ", "", true),
            Text("link_spacing", "Link spacing (mm)", ""),
            Combo("link_legs", "No. of legs", new[] { "2", "4" }, "2"),
        }
    };

    // ——— Civil BOQ (quantity take-off, no rates) ———

    public static ElementSpec MasonryWalls() => new()
    {
        Kind = "masonry", Title = "Masonry walls",
        Subtitle = "Wall build sets unit + thickness (+ block size). Openings on Deductions tab (or takeoff Commit). Doors/Windows with wall_mark also deduct. ≤120 mm → m²; thicker → m³.",
        TypeKey = "wall_build",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "length", "height", "wall_build" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "MW1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Sec("Geometry (mm)"),
            Text("length", "Length L", "5000"),
            Text("height", "Height H", "3000"),
            Combo("wall_build", "Wall build",
                new[]
                {
                    "Brick · 230 mm", "Brick · 110 mm",
                    "ACC · 100 mm", "ACC · 150 mm", "ACC · 200 mm",
                    "Cement block · 100 mm", "Cement block · 150 mm", "Cement block · 200 mm"
                },
                "Brick · 230 mm",
                "Combines unit type + wall thickness; block size is set automatically"),
            Combo("mortar_mix", "Mortar mix (CM)", new[] { "1:4", "1:5", "1:6", "1:8" }, "1:6"),
            Combo("deduct_rule", "Deduction rule",
                new[] { "None", "Openings full", "IS1200 masonry" }, "IS1200 masonry",
                "IS1200 masonry ignores openings < 0.1 m² — openings listed on Deductions tab"),
            // Derived — hidden on sheet
            Combo("unit_type", "Unit type", new[] { "Brick", "ACC Block", "Cement Block" }, "Brick"),
            Combo("thickness", "Wall thickness", new[] { "230", "110", "100", "150", "200" }, "230"),
            Combo("block_size", "Block size (mm)",
                new[] { "600x200x100", "600x200x150", "600x200x200", "400x200x200", "400x200x150" },
                "600x200x150"),
            Sec("Phases (set in O-Draft)"),
            Text("phase", "Est. phase", "", "Estimation phase, picked in O-Draft while measuring"),
            Text("bill_phase", "Bill phase", "", "Running bill phase, picked in O-Draft while measuring"),
        }
    };

    public static ElementSpec Plaster() => new()
    {
        Kind = "plaster", Title = "Plastering",
        Subtitle = "Reconcile wall (both faces) + RCC exposed surfaces, then Finalize. Painting qty follows plaster.",
        TypeKey = "member_type",
        IsCivilBoq = true,
        IsFinishReconcile = true,
        InputKeys = new[] { "mark", "member_type", "area_m2", "include" },
        Fields = new()
        {
            Sec("Reconcile (from walls & RCC)"),
            Text("mark", "Mark", "FN1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("member_type", "Source",
                new[] { "Wall", "Column", "Pedestal", "Beam", "Lintel", "Slab" }, "Wall"),
            Text("source_mark", "Source mark", ""),
            Combo("include", "Include", new[] { "Yes", "No" }, "Yes"),
            Text("area_m2", "Plaster area (m²)", "0"),
            Combo("faces", "Wall faces", new[] { "1", "2" }, "2", "Masonry walls only"),
            Combo("sides_exposed", "Column sides exposed", new[] { "0", "1", "2", "3", "4" }, "3",
                "0–4; typical 3 when one face against wall"),
            Combo("plaster_sides", "Beam sides", new[] { "Yes", "No" }, "Yes"),
            Combo("plaster_soffit", "Beam soffit", new[] { "Yes", "No" }, "No"),
            Combo("plaster_ceiling", "Slab ceiling", new[] { "Yes", "No" }, "No"),
            Text("notes", "Notes", ""),
            // Manual final rows (Final tab / form)
            Sec("Manual plaster (Final sheet)"),
            Text("length", "Length L (manual)", "0"),
            Text("height", "Height H (manual)", "0"),
            Combo("thickness", "Plaster thickness", new[] { "6", "12", "15", "20" }, "12"),
            Combo("mortar_mix", "Mortar mix (CM)", new[] { "1:3", "1:4", "1:5", "1:6" }, "1:4"),
        }
    };

    public static ElementSpec PccBeds() => new()
    {
        Kind = "pcc", Title = "PCC bed",
        Subtitle = "Plain cement concrete bed — L × B × thickness → m³ + cement / sand / aggregate.",
        TypeKey = "mix",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "breadth", "thickness", "mix" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "PCC1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Sec("Geometry (mm)"),
            Text("length", "Length L", "3000"),
            Text("breadth", "Breadth B", "2000"),
            Text("thickness", "Thickness", "100"),
            Sec("Mix"),
            Combo("mix", "Nominal mix", new[] { "1:3:6", "1:4:8", "1:5:10" }, "1:4:8"),
        }
    };

    public static ElementSpec Earthwork() => new()
    {
        Kind = "earthwork", Title = "Earthwork",
        Subtitle = "Excavation / filling — L × B × depth → m³ (quantity only).",
        TypeKey = "work_type",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "breadth", "depth", "work_type" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "EW1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("work_type", "Work type", new[] { "Excavation", "Filling", "Backfilling" }, "Excavation"),
            Sec("Geometry (mm)"),
            Text("length", "Length L", "10000"),
            Text("breadth", "Breadth B", "3000"),
            Text("depth", "Depth / height", "1500"),
        }
    };

    public static ElementSpec SizeStone() => new()
    {
        Kind = "ssm", Title = "Size stone masonry",
        Subtitle = "SSM — L × B × H → m³ + cement & sand for mortar.",
        TypeKey = "mortar_mix",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "breadth", "height" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "SSM1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Sec("Geometry (mm)"),
            Text("length", "Length L", "5000"),
            Text("breadth", "Breadth / thickness B", "450"),
            Text("height", "Height H", "1500"),
            Sec("Mortar"),
            Combo("mortar_mix", "Mortar mix (CM)", new[] { "1:4", "1:5", "1:6", "1:8" }, "1:6"),
        }
    };

    public static ElementSpec Shuttering() => new()
    {
        Kind = "shuttering", Title = "Shuttering / formwork",
        Subtitle = "Auto from RCC concrete members (columns, beams, slabs, footings, walls, stairs). No manual entry. Unit m².",
        TypeKey = "member_type",
        IsCivilBoq = true,
        IsComputedFromRcc = true,
        InputKeys = new[] { "mark", "level", "member_type", "area_m2" },
        Fields = new()
        {
            Sec("From RCC (read-only)"),
            Text("mark", "Mark", "SH1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("member_type", "Member type",
                new[] { "Column", "Beam", "Slab", "Footing", "Wall", "Stairs" }, "Column"),
            Text("rcc_mark", "RCC source mark", ""),
            Combo("include", "Include in BOQ", new[] { "Yes", "No" }, "Yes",
                "Set No to skip this member’s formwork without deleting the RCC element"),
            Sec("Geometry snapshot (mm)"),
            Text("length", "Length / span L", "0"),
            Text("breadth", "Breadth / width B", "0"),
            Text("depth", "Depth / thickness D", "0"),
            Text("height", "Height H", "0"),
            Sec("Computed area"),
            Text("area_m2", "Formwork area (m²)", "0", "Net contact area before wastage"),
            Text("notes", "Formula", ""),
        }
    };

    public static ElementSpec Flooring() => new()
    {
        Kind = "flooring", Title = "Flooring / wall tiles",
        Subtitle = "Floor or wall tiles — vitrified / ceramic sizes, granite, marble. L × B → m² with opening deducts.",
        TypeKey = "finish_type",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "surface_kind", "finish_type", "length", "breadth" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "FL1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("surface_kind", "Surface", FinishCatalog.SurfaceKinds, "Floor"),
            Combo("finish_type", "Finish", FinishCatalog.FloorFinishTypes, "Vitrified tiles",
                "Wall: use Ceramic / Vitrified / Granite / Marble — same tile sizes as floor"),
            Combo("tile_size", "Tile size", FinishCatalog.TileSizes, "600×600",
                "For vitrified & ceramic (floor or wall)"),
            Sec("Plan / face (mm)"),
            Text("length", "Length L", "4000"),
            Text("breadth", "Breadth / height B", "3000", "Floor plan B, or wall height for wall tiles"),
            Sec("Openings (deduct)"),
            Combo("deduct_rule", "Deduction rule",
                new[] { "None", "Openings full", "IS1200 masonry" }, "Openings full"),
            Text("opening_nos", "No. of openings", "0"),
            Text("opening_l", "Opening width (mm)", "0"),
            Text("opening_h", "Opening height (mm)", "0"),
        }
    };

    public static ElementSpec Painting() => new()
    {
        Kind = "painting", Title = "Painting",
        Subtitle = "Area from Plastering. Spec: primer / putty / paint coats · emulsion / distemper · inside / outside.",
        TypeKey = "paint_type",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "area_m2", "paint_type", "paint_location" },
        Fields = new()
        {
            Sec("From plastering"),
            Text("mark", "Mark", "PT1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("paint_location", "Location", FinishCatalog.PaintLocations, "Inside walls"),
            Combo("paint_type", "Paint type", FinishCatalog.PaintTypes, "Emulsion"),
            Combo("paint_system", "Paint system", FinishCatalog.PaintSystems,
                "2 coat primer + 3 coat putty + 2 coat paint"),
            Combo("coats", "Finish coats (note)", new[] { "1", "2", "3" }, "2"),
            Text("area_m2", "Paint area (m²)", "0", "Synced from plaster — edit plaster to change qty"),
            Text("source_mark", "Source mark", ""),
            Text("notes", "Notes", ""),
            Sec("Extra (manual only)"),
            Text("length", "Length L (manual extra)", "0"),
            Text("height", "Height H (manual extra)", "0"),
            Combo("faces", "No. of faces", new[] { "1", "2" }, "1"),
        }
    };

    public static ElementSpec Waterproofing() => new()
    {
        Kind = "waterproofing", Title = "Waterproofing",
        Subtitle = "Area m² or periphery band (running length × height).",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "work_mode", "length", "breadth", "height" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "WP1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("work_mode", "Measure", new[] { "Area", "Periphery band" }, "Area",
                "Area = L×B · Periphery band = running length × height"),
            Text("length", "Length / running L (mm)", "10000"),
            Text("breadth", "Breadth B (mm)", "8000", "Used for Area mode"),
            Text("height", "Band height H (mm)", "300", "Used for Periphery band"),
            Text("notes", "Notes", "", "Membrane / chemical / brickbat coba…"),
        }
    };

    public static ElementSpec Dpc() => new()
    {
        Kind = "dpc", Title = "Damp-proof course",
        Subtitle = "Horizontal DPC — length × width (m²) or with thickness → m³ mortar note.",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "width", "thickness" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "DPC1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("length", "Length L (mm)", "20000"),
            Text("width", "Width (mm)", "230"),
            Text("thickness", "Thickness (mm)", "20"),
            Combo("mortar_mix", "Mortar / mix note", new[] { "1:3", "1:4", "Bitumen", "Other" }, "1:3"),
        }
    };

    public static ElementSpec Coping() => new()
    {
        Kind = "coping", Title = "Coping",
        Subtitle = "Wall-top coping — running length × breadth × depth.",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "width", "depth" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "CP1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("length", "Length L (mm)", "15000"),
            Text("width", "Breadth b (mm)", "300"),
            Text("depth", "Depth / thickness (mm)", "50"),
            Combo("concrete_grade", "Concrete / finish", new[] { "PCC", "RCC", "Stone", "Other" }, "PCC"),
        }
    };

    public static ElementSpec Screed() => new()
    {
        Kind = "screed", Title = "Screed concrete",
        Subtitle = "Floor screed — area × thickness → m³.",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "breadth", "thickness" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "SC1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("length", "Length L (mm)", "5000"),
            Text("breadth", "Breadth B (mm)", "4000"),
            Text("thickness", "Thickness (mm)", "40"),
            Combo("mix", "Mix", new[] { "1:3:6", "1:4:8", "1:5:10", "Other" }, "1:4:8"),
        }
    };

    public static ElementSpec Vdf() => new()
    {
        Kind = "vdf", Title = "VDF flooring",
        Subtitle = "Vacuum dewatered flooring — area m².",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "breadth", "thickness" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "VDF1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("length", "Length L (mm)", "20000"),
            Text("breadth", "Breadth B (mm)", "15000"),
            Text("thickness", "Slab thickness (mm)", "150"),
            Text("notes", "Notes", "", "Panel size / joints…"),
        }
    };

    public static ElementSpec Skirting() => new()
    {
        Kind = "skirting", Title = "Skirting",
        Subtitle = "Running length × height → m².",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "height" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "SK1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("length", "Running length L (mm)", "25000"),
            Text("height", "Height H (mm)", "100"),
            Combo("finish_type", "Finish", new[] { "Tile", "Cement", "Stone", "Other" }, "Tile"),
        }
    };

    public static ElementSpec Parapet() => new()
    {
        Kind = "parapet", Title = "Parapet",
        Subtitle = "Parapet wall — L × H × thickness (masonry volume / face area).",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "height", "thickness" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "PR1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("length", "Length L (mm)", "20000"),
            Text("height", "Height H (mm)", "900"),
            Combo("thickness", "Thickness", new[] { "230", "115", "100" }, "230"),
            Combo("unit_type", "Unit type", new[] { "Brick", "ACC Block", "Concrete" }, "Brick"),
            Combo("mortar_mix", "Mortar mix", new[] { "1:4", "1:5", "1:6" }, "1:6"),
        }
    };

    public static ElementSpec PlinthProtection() => new()
    {
        Kind = "plinth_protection", Title = "Plinth protection",
        Subtitle = "Plinth protection apron — area m² (optional thickness).",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "length", "breadth", "thickness" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "PP1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Text("length", "Length L (mm)", "30000"),
            Text("breadth", "Width of apron (mm)", "600"),
            Text("thickness", "Thickness (mm)", "50"),
            Combo("finish_type", "Finish", new[] { "PCC", "Brick", "Tile", "Other" }, "PCC"),
        }
    };

    public static ElementSpec Doors() => new()
    {
        Kind = "doors", Title = "Doors",
        Subtitle = "MS or wood door schedule — Nos × opening area (m²). Link wall_mark to deduct from masonry.",
        TypeKey = "door_type",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "door_type", "wall_mark", "nos", "width", "height" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "D1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("door_type", "Door type", DoorWindowCatalog.DoorTypes, "Wood door"),
            Combo("wall_mark", "On wall (deduct)", new[] { "" }, "",
                "Masonry wall mark — opening deducted from that wall’s qty"),
            Combo("deduct_from_wall", "Deduct from wall", new[] { "Yes", "No" }, "Yes"),
            Text("nos", "Nos", "1"),
            Sec("Opening (mm)"),
            Text("width", "Width W", "900"),
            Text("height", "Height H", "2100"),
            Sec("Wood frame & shutter"),
            When(Combo("frame_size", "Wood frame", DoorWindowCatalog.WoodFrames, "110×150"),
                "door_type", "Wood door"),
            When(Combo("shutter_thick", "Shutter thickness", DoorWindowCatalog.ShutterThicknesses, "32 mm"),
                "door_type", "Wood door"),
            When(Combo("shutter_type", "Shutter type", DoorWindowCatalog.ShutterTypeNames, "Block Board"),
                "door_type", "Wood door"),
            Combo("wood_finish", "Wood finish", FinishCatalog.WoodFinishes, "Varnish",
                "Varnish / polish / paint — included with door schedule"),
            Text("notes", "Notes", ""),
        }
    };

    public static ElementSpec Windows() => new()
    {
        Kind = "windows", Title = "Windows",
        Subtitle = "System aluminium / UPVC / wooden — Nos × opening area (m²). Wood finish (varnish/polish/paint) on wooden windows.",
        TypeKey = "window_system",
        IsCivilBoq = true,
        InputKeys = new[] { "mark", "level", "window_system", "wall_mark", "nos", "width", "height" },
        Fields = new()
        {
            Sec("Identification"),
            Text("mark", "Mark", "W1"),
            Combo("level", "Storey", new[] { "Lvl0" }, "Lvl0"),
            Combo("window_system", "System", DoorWindowCatalog.WindowSystems, "System Aluminium"),
            Combo("wall_mark", "On wall (deduct)", new[] { "" }, "",
                "Masonry wall mark — opening deducted from that wall’s qty"),
            Combo("deduct_from_wall", "Deduct from wall", new[] { "Yes", "No" }, "Yes"),
            Text("nos", "Nos", "1"),
            Sec("Opening (mm)"),
            Text("width", "Width W", "1200"),
            Text("height", "Height H", "1200"),
            Sec("Aluminium / UPVC"),
            When(Combo("track", "Track", DoorWindowCatalog.Tracks, "2.5 Track"),
                "window_system", "System Aluminium", "UPVC"),
            Sec("Wooden"),
            When(Combo("wood_opening", "Opening type", DoorWindowCatalog.WoodOpenings,
                    "Single shutter — open outside"),
                "window_system", "Wooden"),
            When(Combo("wood_finish", "Wood finish", FinishCatalog.WoodFinishes, "Varnish",
                    "Varnish / polish / paint — included with wooden window"),
                "window_system", "Wooden"),
            Text("notes", "Notes", ""),
        }
    };
}
