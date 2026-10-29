// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace Office.Shared;

/// <summary>
/// A sub-head (chapter) of a CPWD-style Schedule of Rates. Only the name is held: the chapter numbers differ between
/// editions of the DSR and the Specifications, so they are not asserted here. See docs/CPWD-DSR-CASE-STUDY.md.
/// </summary>
public sealed record DsrSubHead(string Key, string Name);

/// <summary>A material a component consumes, with the standard its specification names. Empty <see cref="Standard"/> = "as the specification clause says".</summary>
public sealed record MaterialSpec(string Material, string Standard, string Note = "");

/// <summary>
/// How a component is described, specified and measured in a DSR-style schedule: the sub-head it is priced under, the shape of the item
/// wording, the unit and measurement rule, and the materials with their standards.
/// </summary>
public sealed record DsrReference(
    string ComponentKey,
    string SubHeadKey,
    string ItemWording,
    string MeasuredIn,
    string MeasurementRule,
    IReadOnlyList<MaterialSpec> Materials);

public static class DsrCatalog
{
    public const string Earth = "earth", Concrete = "concrete", Rcc = "rcc", Brick = "brick", Stone = "stone", Flooring = "flooring",
        Finishing = "finishing", WaterProofing = "waterproofing", Joinery = "joinery";

    public static IReadOnlyList<DsrSubHead> SubHeads { get; } = new[]
    {
        new DsrSubHead(Earth, "Earth work"),
        new DsrSubHead(Concrete, "Concrete work"),
        new DsrSubHead(Rcc, "Reinforced cement concrete work"),
        new DsrSubHead(Brick, "Brick work"),
        new DsrSubHead(Stone, "Stone work"),
        new DsrSubHead(Flooring, "Flooring"),
        new DsrSubHead(Finishing, "Finishing"),
        new DsrSubHead(WaterProofing, "Water proofing"),
        new DsrSubHead(Joinery, "Wood, steel and aluminium work"),
    };

    // Standards the CPWD specifications name for the common materials. Each is a Bureau of Indian Standards code.
    static MaterialSpec Cement => new("Cement", "IS 269 (OPC) or IS 1489 (Part 1) (PPC)", "Grade and brand as the specification states.");
    static MaterialSpec Sand => new("Fine aggregate (sand)", "IS 383", "Zone and silt limit as the specification states.");
    static MaterialSpec Coarse => new("Coarse aggregate", "IS 383", "Graded; nominal size as the item states.");
    static MaterialSpec Water => new("Water", "IS 456 (Cl. 5.4)", "Clean and free from harmful matter.");
    static MaterialSpec Steel => new("Reinforcement steel", "IS 1786 (Fe 500D)", "Paid by weight, apart from the concrete.");
    static MaterialSpec ConcreteSpec => new("Concrete", "IS 456", "Grade (M15 to M40) as the item states.");

    static readonly string[] ConcreteRule =
    {
        "Concrete in m³ from the drawing dimensions (IS 1200 Part 2).",
        "Centering and shuttering in m² of surface in contact with concrete, paid separately.",
        "Reinforcement by weight from the bar bending schedule, paid separately.",
    };

    static DsrReference RccMember(string key, string member) => new(
        key, Rcc,
        $"Reinforced cement concrete work in {member}, of the stated grade, laid, vibrated and cured; centering, shuttering and reinforcement paid separately.",
        "m³", string.Join(" ", ConcreteRule),
        new[] { ConcreteSpec, Cement, Sand, Coarse, Water, Steel });

    public static IReadOnlyList<DsrReference> All { get; } = new[]
    {
        RccMember("rcc.columns", "columns"),
        RccMember("rcc.beams", "beams and lintel beams"),
        RccMember("rcc.pedestals", "pedestals"),
        RccMember("rcc.lintels", "lintels"),
        RccMember("rcc.slabs", "slabs"),
        RccMember("rcc.footings", "footings"),
        RccMember("rcc.walls", "retaining walls"),
        RccMember("rcc.stairs", "stairs (waist slab and steps)"),

        new("Shuttering", Rcc,
            "Centering and shuttering including strutting, propping and removal, for the member stated.",
            "m²", "Contact surface of formwork with the concrete; derived from the RCC members, never entered separately.",
            new[] { new MaterialSpec("Formwork material", "", "Steel or plywood as the specification states.") }),

        new("Masonry", Brick,
            "Brick work with bricks of the stated class in cement mortar of the stated mix, in the stated storey, including raking out joints and curing.",
            "m³ (m² for a half-brick wall)",
            "Walls thicker than half a brick are measured in m³ and half-brick walls (up to 120 mm) in m² (IS 1200 Part 3). Openings under 0.1 m² are not deducted; larger ones are. "
            + "O-CoreOp applies exactly this under the rule \"IS1200 masonry\".",
            new[]
            {
                new MaterialSpec("Bricks (modular)", "IS 1077", "Class designation as the item states; modular size 190 x 90 x 90 mm."),
                new MaterialSpec("AAC blocks", "IS 2185 (Part 3)", "Where AAC block masonry is the item."),
                new MaterialSpec("Concrete blocks", "IS 2185 (Part 1)", "Where concrete block masonry is the item."),
                new MaterialSpec("Cement mortar 1:6 (cement : sand)", "", "Mix as the item states."),
                Cement, Sand,
            }),
        new("SSM", Stone,
            "Size stone masonry in cement mortar of the stated mix, with stone of the stated quality, including dressing, bedding and curing.",
            "m³", "Measured by volume of the finished masonry (IS 1200 Part 4).",
            new[] { new MaterialSpec("Size stone", "IS 1597 (Part 1)", "Construction of stone masonry."), Cement, Sand }),

        new("Earthwork", Earth,
            "Excavation in foundation trenches or pits in the stated soil, to the stated depth, including dressing, disposal and refilling as the item states.",
            "m³", "By volume of the excavation from the drawing dimensions, in the depth ranges the schedule gives (IS 1200 Part 1).",
            Array.Empty<MaterialSpec>()),
        new("PCC", Concrete,
            "Plain cement concrete of the stated mix in foundations or as a bed, laid, vibrated and cured.",
            "m³", "By volume from the drawing dimensions (IS 1200 Part 2); no reinforcement or formwork unless the item says so.",
            new[] { new MaterialSpec("Cement concrete 1:4:8", "IS 456", "Mix as the item states."), Cement, Sand, Coarse, Water }),

        new("Plaster", Finishing,
            "Cement plaster of the stated mix and thickness on the stated surface, including curing and scaffolding as the item states.",
            "m²", "Area of the plastered surface; both faces of a wall are two measures. O-CoreOp deducts openings in full under the rule \"IS1200 plaster/paint\" and can add the reveals.",
            new[] { new MaterialSpec("Cement mortar 1:3 (cement : sand)", "", "Mix as the item states."), Cement, Sand }),
        new("Painting", Finishing,
            "Painting with the stated paint, number of coats and primer on the stated surface.",
            "m²", "Area of the painted surface after plaster; deductions as for plaster.",
            new[] { new MaterialSpec("Paint and primer", "", "Type, make and coats as the specification states.") }),
        new("Flooring", Flooring,
            "Flooring of the stated kind (tiles, stone or granolithic) laid on the stated bedding, including jointing and cleaning.",
            "m²", "Area of the laid floor; skirting is measured separately in metres.",
            new[]
            {
                new MaterialSpec("Vitrified tiles", "IS 15622"),
                new MaterialSpec("Ceramic tiles", "IS 13755"),
                Cement, Sand,
            }),
        new("Screed", Flooring,
            "Cement mortar or concrete bedding under flooring, of the stated thickness and mix.",
            "m²", "Area of the floor bedded.",
            new[] { Cement, Sand }),
        new("Skirting", Flooring,
            "Skirting of the stated material and height along the wall base.",
            "m", "Running length along the wall base, net of door openings.",
            new[] { Cement, Sand }),
        new("VDF", Flooring,
            "Vacuum-dewatered concrete flooring of the stated grade and thickness, finished.",
            "m²", "Area of the floor laid.",
            new[] { ConcreteSpec, Cement, Sand, Coarse }),

        new("Waterproofing", WaterProofing,
            "Waterproofing treatment of the stated system over the stated surface, including protective screed where the item says so.",
            "m²", "Area treated, with the upturn at walls as the item states.",
            new[] { new MaterialSpec("Waterproofing compound or membrane", "", "System as the specification states.") }),
        new("DPC", WaterProofing,
            "Damp-proof course of cement concrete of the stated mix and thickness with an integral waterproofing compound, laid on the wall base.",
            "m² or m (as the schedule states)", "Area (length x width) or running length, as the item is written.",
            new[] { new MaterialSpec("Integral waterproofing compound", "IS 2645"), Cement, Sand, Coarse }),
        new("Coping", Concrete,
            "Coping of the stated section along the top of a wall or parapet, finished.",
            "m", "Running length.",
            new[] { Cement, Sand, Coarse }),
        new("Parapet", Brick,
            "Parapet wall of the stated thickness and height, with plaster and coping paid as their own items.",
            "m", "Running length for the stated height; the wall is measured as masonry where the schedule has no separate item.",
            new[] { Cement, Sand }),
        new("Plinth protection", Concrete,
            "Apron of cement concrete of the stated mix and width round the plinth, finished.",
            "m²", "Area of the apron.",
            new[] { Cement, Sand, Coarse }),

        new("Doors", Joinery,
            "Doors and shutters of the stated material and size, complete with frame, hardware and finish as the item states.",
            "nos or m² (as the schedule states)", "Counted, or by the area of the opening.",
            new[] { new MaterialSpec("Frame and shutter", "", "Material and section as the specification states.") }),
        new("Windows", Joinery,
            "Windows of the stated material and size, complete with frame, glazing, hardware and finish as the item states.",
            "nos or m² (as the schedule states)", "Counted, or by the area of the opening.",
            new[] { new MaterialSpec("Frame, glass and fittings", "", "Material and section as the specification states.") }),
    };

    public static DsrReference? For(string? componentKey) =>
        string.IsNullOrWhiteSpace(componentKey) ? null : All.FirstOrDefault(r => r.ComponentKey.Equals(componentKey.Trim(), StringComparison.OrdinalIgnoreCase));

    public static DsrSubHead? SubHead(string? key) => SubHeads.FirstOrDefault(s => s.Key == key);
}
