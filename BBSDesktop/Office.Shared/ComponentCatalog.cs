// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace Office.Shared;

/// <summary>What a port carries. The colour is how the data model shows it: yellow linear, blue area, red volume.</summary>
public enum Measure
{
    /// <summary>Linear measure in metres (yellow): a length or a perimeter.</summary>
    Length,
    /// <summary>Area in square metres (blue).</summary>
    Area,
    /// <summary>Volume in cubic metres (red).</summary>
    Volume,
    /// <summary>A count of items (grey). Only doors and windows carry it.</summary>
    Count,
}

public static class MeasureInfo
{
    public static string Colour(Measure m) => m switch
    {
        Measure.Length => "yellow",
        Measure.Area => "blue",
        Measure.Volume => "red",
        _ => "grey",
    };

    public static string Unit(Measure m) => m switch
    {
        Measure.Length => "m",
        Measure.Area => "m²",
        Measure.Volume => "m³",
        _ => "nos",
    };

    /// <summary>The measure a link rule basis (<c>Area</c>, <c>Volume</c>, <c>Length</c>, <c>Perimeter</c>, <c>Count</c>) belongs to.</summary>
    public static Measure OfBasis(string? basis) => (basis ?? "").Trim().ToLowerInvariant() switch
    {
        "volume" => Measure.Volume,
        "length" or "perimeter" => Measure.Length,
        "count" => Measure.Count,
        _ => Measure.Area,
    };
}

/// <summary>
/// A connector on a component. An output's <see cref="Basis"/> is the link-rule basis it supplies; an input takes any output of
/// its <see cref="Measure"/> (its <c>Basis</c> is empty).
/// </summary>
public sealed record Port(string Name, Measure Measure, string Basis = "");

/// <summary>A main component: a family of sub components that follow the same rules.</summary>
public sealed record ComponentGroup(string Key, string Display, string Role);

/// <summary>
/// A sub component: one work item. <see cref="Derivable"/> items are trades the estimate can derive from other trades (they can be
/// linked); the others (RCC) are measured on their own pages and only supply quantities the model explains.
/// </summary>
public sealed record ComponentDef(
    string Key,
    string Display,
    string GroupKey,
    string Unit,
    bool Derivable,
    string IconKey,
    IReadOnlyList<Port> Inputs,
    IReadOnlyList<Port> Outputs,
    string Note);

/// <summary>The components of the data model, by main component, with their parameters (ports).</summary>
public static class ComponentCatalog
{
    public const string Rcc = "rcc", Formwork = "formwork", Masonry = "masonry", Earthwork = "earthwork",
        Finishes = "finishes", Protection = "protection", Openings = "openings";

    public static IReadOnlyList<ComponentGroup> Groups { get; } = new[]
    {
        new ComponentGroup(Rcc, "RCC structure", "Measured from the drawing. Supplies quantities; nothing feeds it."),
        new ComponentGroup(Formwork, "Formwork", "Follows the RCC automatically."),
        new ComponentGroup(Masonry, "Masonry", "Walls built of units or stone. Feeds finishes and protection."),
        new ComponentGroup(Earthwork, "Earthwork and bed", "Excavation and the bed under the structure."),
        new ComponentGroup(Finishes, "Finishes", "Applied to surfaces. Can feed later finishes and protection."),
        new ComponentGroup(Protection, "Protection and waterproofing", "End of the line: fed by masonry and finishes, feeds nothing."),
        new ComponentGroup(Openings, "Openings", "Counted from the drawing. Not linked."),
    };

    static Port In(Measure m) => new(m switch { Measure.Length => "Length", Measure.Area => "Area", Measure.Volume => "Volume", _ => "Count" }, m);
    static Port Out(string name, string basis) => new(name, MeasureInfo.OfBasis(basis), basis);
    static readonly Port OutLength = Out("Length", "Length"), OutPerimeter = Out("Perimeter", "Perimeter"), OutArea = Out("Area", "Area"),
        OutVolume = Out("Volume", "Volume"), OutCount = Out("Count", "Count");
    static readonly Port[] None = Array.Empty<Port>();

    static ComponentDef Rcc_(string key, string display, string icon, string note) =>
        new("rcc." + key, display, Rcc, "m³", false, icon, None, new[] { OutLength, OutArea, OutVolume }, note);

    public static IReadOnlyList<ComponentDef> All { get; } = new[]
    {
        Rcc_("columns", "Columns", "columns", "Concrete volume, formwork area and height from the Columns page."),
        Rcc_("beams", "Beams", "beams", "Concrete volume, soffit and side formwork from the Beams page."),
        Rcc_("pedestals", "Pedestals", "pedestals", "From the Pedestals page."),
        Rcc_("lintels", "Lintels", "lintels", "From the Lintels page."),
        Rcc_("slabs", "Slabs", "slabs", "Concrete volume and soffit area from the Slabs page."),
        Rcc_("footings", "Footings", "footings", "From the Footings page."),
        Rcc_("walls", "Retaining walls", "walls", "From the Retaining walls page."),
        Rcc_("stairs", "Stairs", "stairs", "From the Stairs page."),

        new("Shuttering", "Shuttering", Formwork, "m²", true, "shuttering", new[] { In(Measure.Area) }, new[] { OutArea },
            "Always rebuilt from the RCC pages; a link from RCC is not needed."),

        new("Masonry", "Masonry walls", Masonry, "m³", true, "masonry", new[] { In(Measure.Volume) }, new[] { OutLength, OutArea, OutVolume },
            "Brick, AAC or block masonry (chosen by the wall build). Length feeds skirting and DPC, area (both faces) feeds plaster and paint, volume prices the units."),
        new("SSM", "Size stone masonry", Masonry, "m³", true, "ssm", new[] { In(Measure.Volume) }, new[] { OutLength, OutArea, OutVolume },
            "Stone volume."),

        new("Earthwork", "Earthwork", Earthwork, "m³", true, "earthwork", None, new[] { OutArea, OutVolume },
            "Excavation measured from the plan."),
        new("PCC", "PCC bed", Earthwork, "m³", true, "pcc", new[] { In(Measure.Area) }, new[] { OutArea, OutVolume },
            "Bed under footings and floors; follows the excavated plan area."),

        new("Plaster", "Plastering", Finishes, "m²", true, "plaster", new[] { In(Measure.Area) }, new[] { OutArea },
            "Mortar finish; both faces of a wall."),
        new("Painting", "Painting", Finishes, "m²", true, "painting", new[] { In(Measure.Area) }, new[] { OutArea },
            "Follows the plastered area."),
        new("Flooring", "Flooring", Finishes, "m²", true, "flooring", new[] { In(Measure.Area) }, new[] { OutArea, OutPerimeter },
            "Floor area; its perimeter is the skirting length."),
        new("Screed", "Screed", Finishes, "m²", true, "screed", new[] { In(Measure.Area) }, new[] { OutArea },
            "Bedding under flooring."),
        new("Skirting", "Skirting", Finishes, "m", true, "skirting", new[] { In(Measure.Length) }, new[] { OutLength },
            "Running metres; takes a length or a perimeter."),
        new("VDF", "VDF", Finishes, "m²", true, "vdf", new[] { In(Measure.Area) }, new[] { OutArea },
            "Vacuum-dewatered flooring."),

        new("Waterproofing", "Waterproofing", Protection, "m²", true, "waterproofing", new[] { In(Measure.Area) }, new[] { OutArea },
            "Treatment over a surface."),
        new("DPC", "DPC", Protection, "m", true, "dpc", new[] { In(Measure.Length) }, new[] { OutLength },
            "Damp-proof course along the wall base."),
        new("Coping", "Coping", Protection, "m", true, "coping", new[] { In(Measure.Length) }, new[] { OutLength },
            "Along the top of a wall."),
        new("Parapet", "Parapet", Protection, "m", true, "parapet", new[] { In(Measure.Length) }, new[] { OutLength },
            "Running metres along the wall."),
        new("Plinth protection", "Plinth protection", Protection, "m²", true, "plinth_protection", new[] { In(Measure.Area) }, new[] { OutArea },
            "Apron round the plinth."),

        new("Doors", "Doors", Openings, "nos", true, "doors", None, new[] { OutCount }, "Counted from the drawing."),
        new("Windows", "Windows", Openings, "nos", true, "windows", None, new[] { OutCount }, "Counted from the drawing."),
    };

    public static ComponentDef? ByKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(c => c.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));

    public static ComponentGroup? Group(string? key) => Groups.FirstOrDefault(g => g.Key == key);

    public static IEnumerable<ComponentDef> InGroup(string groupKey) => All.Where(c => c.GroupKey == groupKey);

    /// <summary>The output of <paramref name="c"/> that supplies <paramref name="basis"/>, or null.</summary>
    public static Port? OutputFor(ComponentDef c, string? basis) =>
        c.Outputs.FirstOrDefault(p => p.Basis.Equals((basis ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The input of <paramref name="c"/> that takes <paramref name="m"/>, or null.</summary>
    public static Port? InputFor(ComponentDef c, Measure m) => c.Inputs.FirstOrDefault(p => p.Measure == m);
}

/// <summary>The rules for what may be connected to what.</summary>
public static class FlowRules
{
    // Which main component may feed which. A group feeding itself is listed where it makes sense (earthwork to its bed).
    static readonly HashSet<(string From, string To)> Allowed = new()
    {
        (ComponentCatalog.Masonry, ComponentCatalog.Finishes),
        (ComponentCatalog.Masonry, ComponentCatalog.Protection),
        (ComponentCatalog.Earthwork, ComponentCatalog.Earthwork),
        (ComponentCatalog.Finishes, ComponentCatalog.Finishes),
        (ComponentCatalog.Finishes, ComponentCatalog.Protection),
    };

    // Inside a main component only these chains exist: a finish follows the surface it is applied to, never the other way round.
    static readonly HashSet<(string From, string To)> Chains = new()
    {
        ("Earthwork", "PCC"),
        ("Plaster", "Painting"),
        ("Flooring", "Screed"),
        ("Flooring", "Skirting"),
    };

    public static bool GroupMayFeed(string fromGroup, string toGroup) => Allowed.Contains((fromGroup, toGroup));

    /// <summary>Why one main component cannot feed another, in a sentence a person can act on.</summary>
    public static string WhyGroupNot(string fromGroup, string toGroup)
    {
        string name(string k) => ComponentCatalog.Group(k)?.Display ?? k;
        if (fromGroup == ComponentCatalog.Rcc && toGroup == ComponentCatalog.Formwork)
            return "Formwork follows the RCC automatically; no link is needed.";
        if (fromGroup == ComponentCatalog.Rcc || toGroup == ComponentCatalog.Rcc)
            return fromGroup == ComponentCatalog.Masonry || toGroup == ComponentCatalog.Masonry
                ? "RCC and masonry cannot be linked: they are separate trades measured on their own, and neither is derived from the other."
                : toGroup == ComponentCatalog.Rcc
                    ? "RCC is measured from the drawing; nothing can feed it."
                    : "RCC quantities are measured, not derived; only formwork follows from RCC.";
        if (toGroup == ComponentCatalog.Masonry || toGroup == ComponentCatalog.Earthwork || toGroup == ComponentCatalog.Openings)
            return $"{name(toGroup)} is measured on its own; {name(fromGroup).ToLowerInvariant()} cannot feed it.";
        if (fromGroup == ComponentCatalog.Protection)
            return "Protection and waterproofing end the line; they feed nothing.";
        if (fromGroup == ComponentCatalog.Openings)
            return "Openings are counted, not linked.";
        return $"{name(fromGroup)} cannot feed {name(toGroup).ToLowerInvariant()}.";
    }

    /// <summary>Whether <paramref name="output"/> of <paramref name="source"/> may be connected to <paramref name="target"/>; if not, why.</summary>
    public static (bool Ok, string Reason) Check(ComponentDef source, Port output, ComponentDef target)
    {
        if (source.Key.Equals(target.Key, StringComparison.OrdinalIgnoreCase))
            return (false, "An item cannot feed itself.");
        if (!source.Derivable)
            return (false, WhyGroupNot(source.GroupKey, target.GroupKey));
        if (!target.Derivable)
            return (false, WhyGroupNot(source.GroupKey, target.GroupKey));
        if (!FlowRules.GroupMayFeed(source.GroupKey, target.GroupKey))
            return (false, WhyGroupNot(source.GroupKey, target.GroupKey));
        if (source.GroupKey == target.GroupKey && !Chains.Contains((source.Key, target.Key)))
            return (false, $"{target.Display} does not follow {source.Display}: a finish follows the surface it is applied to, in the order the work is done.");
        if (ComponentCatalog.InputFor(target, output.Measure) is null)
        {
            var takes = target.Inputs.Count == 0
                ? "takes nothing in"
                : "takes " + string.Join(" and ", target.Inputs.Select(p => MeasureInfo.Colour(p.Measure) + " " + p.Name.ToLowerInvariant()));
            return (false, $"{target.Display} {takes}; a {MeasureInfo.Colour(output.Measure)} {output.Name.ToLowerInvariant()} cannot connect there.");
        }
        return (true, "");
    }
}
