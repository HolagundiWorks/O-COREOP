// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BBSApp.Services;

/// <summary>Which measure of a source trade drives the linked quantity.</summary>
public enum LinkBasis
{
    /// <summary>Face / surface area (m²).</summary>
    Area,
    /// <summary>Solid volume (m³).</summary>
    Volume,
    /// <summary>Running length (m).</summary>
    Length,
    /// <summary>Plan perimeter 2·(L+B) of the source item (m).</summary>
    Perimeter,
    /// <summary>Item count (nos).</summary>
    Count
}

public static class LinkBasisInfo
{
    public static readonly LinkBasis[] All =
        { LinkBasis.Area, LinkBasis.Volume, LinkBasis.Length, LinkBasis.Perimeter, LinkBasis.Count };

    public static string Label(LinkBasis b) => b switch
    {
        LinkBasis.Area => "Area",
        LinkBasis.Volume => "Volume",
        LinkBasis.Length => "Length",
        LinkBasis.Perimeter => "Perimeter",
        LinkBasis.Count => "Count",
        _ => "Area"
    };

    /// <summary>Unit the driving measure is expressed in.</summary>
    public static string Unit(LinkBasis b) => b switch
    {
        LinkBasis.Area => "m²",
        LinkBasis.Volume => "m³",
        LinkBasis.Length => "m",
        LinkBasis.Perimeter => "m",
        LinkBasis.Count => "nos",
        _ => "m²"
    };

    public static LinkBasis Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return LinkBasis.Area;
        foreach (var b in All)
            if (Label(b).Equals(s.Trim(), StringComparison.OrdinalIgnoreCase))
                return b;
        return LinkBasis.Area;
    }
}

/// <summary>
/// A directed derivation edge: quantity of <see cref="TargetTrade"/> follows
/// <see cref="Factor"/> × the <see cref="Basis"/> measure of <see cref="SourceTrade"/>.
/// e.g. Plaster = 2 × masonry face area; Painting = 1 × plaster area.
/// </summary>
public sealed class LinkRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Source trade key — matches <see cref="CivilLine.Element"/> / <see cref="TradeDef.Key"/>.</summary>
    public string SourceTrade { get; set; } = "";
    public string TargetTrade { get; set; } = "";
    public LinkBasis Basis { get; set; } = LinkBasis.Area;
    public double Factor { get; set; } = 1.0;
    /// <summary>Unit label for the produced target quantity.</summary>
    public string TargetUnit { get; set; } = "m²";
    /// <summary>True: one linked line per source item (keeps mark/level). False: one aggregate line.</summary>
    public bool PerItem { get; set; } = true;
    /// <summary>Optional rate code the derived lines price on. Empty = the target trade's canonical
    /// code (<see cref="EstimateCalculator.CodeForElement"/>). Set to pin a specific rate item.</summary>
    public string RateCodeOverride { get; set; } = "";
    /// <summary>Optional manual unit rate (₹) for the derived lines. When &gt; 0 it wins over any
    /// rate-code lookup, so the derived quantity prices even with no rate-book entry.</summary>
    public double RateOverride { get; set; }
    public string Notes { get; set; } = "";

    public LinkRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        SourceTrade = SourceTrade,
        TargetTrade = TargetTrade,
        Basis = Basis,
        Factor = Factor,
        TargetUnit = TargetUnit,
        PerItem = PerItem,
        RateCodeOverride = RateCodeOverride,
        RateOverride = RateOverride,
        Notes = Notes
    };

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["name"] = Name,
        ["enabled"] = Enabled ? 1 : 0,
        ["source"] = SourceTrade,
        ["target"] = TargetTrade,
        ["basis"] = LinkBasisInfo.Label(Basis),
        ["factor"] = Factor,
        ["target_unit"] = TargetUnit,
        ["per_item"] = PerItem ? 1 : 0,
        ["rate_code"] = RateCodeOverride,
        ["rate_override"] = RateOverride,
        ["notes"] = Notes
    };

    public static LinkRule FromJson(JsonObject o)
    {
        double Num(string key, double def)
            => o[key] is JsonValue jv && jv.TryGetValue<double>(out var d) ? d : def;
        string Str(string key, string def = "")
            => o[key] is JsonValue jv && jv.TryGetValue<string>(out var s) && s is not null ? s : def;

        string id = Str("id");
        return new LinkRule
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..8] : id,
            Name = Str("name"),
            Enabled = Num("enabled", 1) != 0,
            SourceTrade = Str("source"),
            TargetTrade = Str("target"),
            Basis = LinkBasisInfo.Parse(Str("basis", "Area")),
            Factor = Num("factor", 1.0),
            TargetUnit = Str("target_unit", "m²"),
            PerItem = Num("per_item", 1) != 0,
            RateCodeOverride = Str("rate_code"),
            RateOverride = Num("rate_override", 0),
            Notes = Str("notes")
        };
    }
}

/// <summary>A linkable trade (graph node) — one civil / finish work item.</summary>
public sealed class TradeDef
{
    public TradeDef(string key, string display, string unit, LinkBasis defaultBasis, bool sourceOnly = false)
    {
        SourceOnly = sourceOnly;
        Key = key;
        Display = display;
        Unit = unit;
        DefaultBasis = defaultBasis;
    }

    /// <summary>Stable key, matches <see cref="CivilLine.Element"/> (case-insensitive).</summary>
    public string Key { get; }
    public string Display { get; }
    public string Unit { get; }
    public LinkBasis DefaultBasis { get; }
    /// <summary>True for RCC members: their quantities are measured on their own pages, so a rule can read them but never write them.</summary>
    public bool SourceOnly { get; }
}

/// <summary>The set of trades that can appear as a source or target of a link rule.</summary>
public static class LinkTradeRegistry
{
    public static readonly IReadOnlyList<TradeDef> All = new[]
    {
        new TradeDef("Masonry", "Masonry", "m³", LinkBasis.Area),
        new TradeDef("Plaster", "Plastering", "m²", LinkBasis.Area),
        new TradeDef("Painting", "Painting", "m²", LinkBasis.Area),
        new TradeDef("Flooring", "Flooring", "m²", LinkBasis.Area),
        new TradeDef("Skirting", "Skirting", "m", LinkBasis.Length),
        new TradeDef("Screed", "Screed", "m²", LinkBasis.Area),
        new TradeDef("VDF", "VDF", "m²", LinkBasis.Area),
        new TradeDef("Waterproofing", "Waterproofing", "m²", LinkBasis.Area),
        new TradeDef("DPC", "DPC", "m", LinkBasis.Length),
        new TradeDef("Coping", "Coping", "m", LinkBasis.Length),
        new TradeDef("Parapet", "Parapet", "m", LinkBasis.Length),
        new TradeDef("PCC", "PCC bed", "m³", LinkBasis.Volume),
        new TradeDef("SSM", "Size stone", "m³", LinkBasis.Volume),
        new TradeDef("Earthwork", "Earthwork", "m³", LinkBasis.Volume),
        new TradeDef("Shuttering", "Shuttering", "m²", LinkBasis.Area),
        new TradeDef("Plinth protection", "Plinth protection", "m²", LinkBasis.Area),
        new TradeDef("Doors", "Doors", "nos", LinkBasis.Count),
        new TradeDef("Windows", "Windows", "nos", LinkBasis.Count)
    };

    /// <summary>RCC members. A rule can use one as its source (plaster on a soffit, waterproofing over a slab, a PCC bed under footings) but never as its target.</summary>
    public static readonly IReadOnlyList<TradeDef> Rcc = new[]
    {
        new TradeDef("rcc.columns", "Columns", "m³", LinkBasis.Volume, true),
        new TradeDef("rcc.beams", "Beams", "m³", LinkBasis.Volume, true),
        new TradeDef("rcc.pedestals", "Pedestals", "m³", LinkBasis.Volume, true),
        new TradeDef("rcc.lintels", "Lintels", "m³", LinkBasis.Volume, true),
        new TradeDef("rcc.slabs", "Slabs", "m³", LinkBasis.Volume, true),
        new TradeDef("rcc.footings", "Footings", "m³", LinkBasis.Volume, true),
        new TradeDef("rcc.walls", "Retaining walls", "m³", LinkBasis.Volume, true),
        new TradeDef("rcc.stairs", "Stairs", "m³", LinkBasis.Volume, true)
    };

    /// <summary>Everything a rule can read: the trades plus the RCC members.</summary>
    public static readonly IReadOnlyList<TradeDef> Sources = All.Concat(Rcc).ToList();

    public static TradeDef? ByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (var t in Sources)
            if (t.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    public static string Display(string? key) => ByKey(key)?.Display ?? (key ?? "");

    public static string Unit(string? key) => ByKey(key)?.Unit ?? "";
}

/// <summary>Editable, persisted collection of link rules with a seedable standard library.</summary>
public sealed class LinkRuleBook
{
    public ObservableCollection<LinkRule> Rules { get; } = new();

    public JsonArray ToJson()
    {
        var arr = new JsonArray();
        foreach (var r in Rules) arr.Add(r.ToJson());
        return arr;
    }

    public void LoadFrom(JsonArray? arr)
    {
        Rules.Clear();
        if (arr is null) return;
        foreach (var n in arr)
            if (n is JsonObject o) Rules.Add(LinkRule.FromJson(o));
    }

    /// <summary>Seed the India-practice defaults only when no rules are stored yet.</summary>
    public void SeedIfEmpty()
    {
        if (Rules.Count == 0)
            foreach (var r in Defaults()) Rules.Add(r);
    }

    public void ResetToDefaults()
    {
        Rules.Clear();
        foreach (var r in Defaults()) Rules.Add(r);
    }

    /// <summary>Standard linked-item chains for typical RCC-framed masonry buildings.</summary>
    public static IEnumerable<LinkRule> Defaults() => new[]
    {
        new LinkRule
        {
            Id = "msn-plaster",
            Name = "Plaster from masonry (both faces)",
            SourceTrade = "Masonry",
            TargetTrade = "Plaster",
            Basis = LinkBasis.Area,
            Factor = 2.0,
            TargetUnit = "m²",
            PerItem = true,
            Notes = "12 mm plaster on both faces of masonry walls."
        },
        new LinkRule
        {
            Id = "plaster-paint",
            Name = "Painting from plaster",
            SourceTrade = "Plaster",
            TargetTrade = "Painting",
            Basis = LinkBasis.Area,
            Factor = 1.0,
            TargetUnit = "m²",
            PerItem = true,
            Notes = "Paint area follows plastered area 1:1."
        },
        new LinkRule
        {
            Id = "floor-skirting",
            Name = "Skirting from flooring perimeter",
            SourceTrade = "Flooring",
            TargetTrade = "Skirting",
            Basis = LinkBasis.Perimeter,
            Factor = 1.0,
            TargetUnit = "m",
            PerItem = true,
            Notes = "Skirting run ≈ room perimeter (2·(L+B)). Adjust factor for door gaps."
        },
        new LinkRule
        {
            Id = "floor-screed",
            Name = "Screed bedding under flooring",
            Enabled = false,
            SourceTrade = "Flooring",
            TargetTrade = "Screed",
            Basis = LinkBasis.Area,
            Factor = 1.0,
            TargetUnit = "m²",
            PerItem = true,
            Notes = "Cement bedding/screed below floor finish. Enable if measured via this link."
        },
        new LinkRule
        {
            Id = "msn-dpc",
            Name = "DPC along masonry base",
            Enabled = false,
            SourceTrade = "Masonry",
            TargetTrade = "DPC",
            Basis = LinkBasis.Length,
            Factor = 1.0,
            TargetUnit = "m",
            PerItem = true,
            Notes = "Damp-proof course along wall length at plinth. Enable for plinth-level walls only."
        }
    };
}
