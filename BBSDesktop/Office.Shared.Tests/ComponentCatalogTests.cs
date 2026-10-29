// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Office.Shared;
using Xunit;

namespace Office.Shared.Tests;

public class ComponentCatalogTests
{
    static ComponentDef C(string key) => ComponentCatalog.ByKey(key)!;
    static Port Out(string key, string basis) => ComponentCatalog.OutputFor(C(key), basis)!;

    [Fact]
    public void Every_component_belongs_to_a_group_and_keys_are_unique()
    {
        Assert.Equal(ComponentCatalog.All.Count, ComponentCatalog.All.Select(c => c.Key.ToLowerInvariant()).Distinct().Count());
        Assert.All(ComponentCatalog.All, c => Assert.NotNull(ComponentCatalog.Group(c.GroupKey)));
        Assert.All(ComponentCatalog.Groups, g => Assert.NotEmpty(ComponentCatalog.InGroup(g.Key)));
    }

    [Fact]
    public void Colours_follow_the_measure()
    {
        Assert.Equal("yellow", MeasureInfo.Colour(Measure.Length));
        Assert.Equal("blue", MeasureInfo.Colour(Measure.Area));
        Assert.Equal("red", MeasureInfo.Colour(Measure.Volume));
        Assert.Equal(Measure.Length, MeasureInfo.OfBasis("Perimeter"));
        Assert.Equal(Measure.Length, MeasureInfo.OfBasis("Length"));
    }

    [Fact]
    public void Brick_masonry_takes_volume_in_and_gives_all_three_measures_out()
    {
        var m = C("Masonry");
        Assert.Equal(new[] { Measure.Volume }, m.Inputs.Select(p => p.Measure));
        Assert.Equal(new[] { Measure.Length, Measure.Area, Measure.Volume }, m.Outputs.Select(p => p.Measure));
    }

    [Theory]
    [InlineData("Masonry", "Area", "Plaster")]      // plaster on both faces
    [InlineData("Masonry", "Length", "Skirting")]   // skirting along the wall
    [InlineData("Masonry", "Length", "DPC")]
    [InlineData("Plaster", "Area", "Painting")]
    [InlineData("Flooring", "Perimeter", "Skirting")]
    [InlineData("Flooring", "Area", "Screed")]
    [InlineData("Earthwork", "Area", "PCC")]
    [InlineData("Screed", "Area", "Waterproofing")]
    [InlineData("rcc.slabs", "Area", "Plaster")]       // soffit plaster
    [InlineData("rcc.slabs", "Area", "Waterproofing")] // roof treatment
    [InlineData("rcc.beams", "Area", "Painting")]
    [InlineData("rcc.footings", "Area", "PCC")]        // bed under footings
    public void The_standard_relationships_are_allowed(string from, string basis, string to)
    {
        var (ok, reason) = FlowRules.Check(C(from), Out(from, basis), C(to));
        Assert.True(ok, reason);
    }

    [Theory]
    [InlineData("rcc.columns", "Volume", "Masonry")]
    [InlineData("Masonry", "Volume", "rcc.beams")]
    [InlineData("rcc.slabs", "Area", "Skirting")]
    [InlineData("rcc.columns", "Length", "Parapet")]
    [InlineData("rcc.slabs", "Area", "PCC")]
    [InlineData("Plaster", "Area", "rcc.slabs")]
    [InlineData("Plaster", "Area", "Masonry")]
    [InlineData("Painting", "Area", "Flooring")]
    [InlineData("DPC", "Length", "Skirting")]
    [InlineData("Doors", "Count", "Painting")]
    public void Rcc_cannot_be_joined_to_masonry_and_the_hierarchy_runs_one_way(string from, string basis, string to)
    {
        var (ok, reason) = FlowRules.Check(C(from), Out(from, basis), C(to));
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void The_reason_for_rcc_to_masonry_says_so()
    {
        var (ok, reason) = FlowRules.Check(C("rcc.columns"), Out("rcc.columns", "Volume"), C("Masonry"));
        Assert.False(ok);
        Assert.Contains("RCC and masonry", reason);
    }

    [Fact]
    public void Rcc_volume_cannot_go_where_only_area_is_taken()
    {
        Assert.False(FlowRules.Check(C("rcc.slabs"), Out("rcc.slabs", "Volume"), C("Plaster")).Ok);
    }

    [Fact]
    public void A_colour_must_match_the_input()
    {
        // Masonry to Painting is allowed between the groups, but painting takes area only.
        var (ok, reason) = FlowRules.Check(C("Masonry"), Out("Masonry", "Length"), C("Painting"));
        Assert.False(ok);
        Assert.Contains("area", reason);
        var (volOk, _) = FlowRules.Check(C("Masonry"), Out("Masonry", "Volume"), C("Plaster"));
        Assert.False(volOk);
    }

    [Fact]
    public void An_item_cannot_feed_itself()
    {
        Assert.False(FlowRules.Check(C("Plaster"), Out("Plaster", "Area"), C("Plaster")).Ok);
    }

    [Fact]
    public void Formwork_follows_rcc_without_a_link()
    {
        var (ok, reason) = FlowRules.Check(C("rcc.columns"), Out("rcc.columns", "Area"), C("Shuttering"));
        Assert.False(ok);
        Assert.Contains("automatically", reason);
    }
}

public class DsrCatalogTests
{
    [Fact]
    public void Every_component_has_a_dsr_reference_under_a_known_sub_head()
    {
        foreach (var c in ComponentCatalog.All)
        {
            var d = DsrCatalog.For(c.Key);
            Assert.True(d is not null, $"{c.Key} has no DSR reference");
            Assert.NotNull(DsrCatalog.SubHead(d!.SubHeadKey));
            Assert.False(string.IsNullOrWhiteSpace(d.ItemWording));
            Assert.False(string.IsNullOrWhiteSpace(d.MeasuredIn));
            Assert.False(string.IsNullOrWhiteSpace(d.MeasurementRule));
        }
    }

    [Fact]
    public void There_is_no_reference_for_a_component_that_does_not_exist()
    {
        Assert.All(DsrCatalog.All, d => Assert.NotNull(ComponentCatalog.ByKey(d.ComponentKey)));
        Assert.Equal(ComponentCatalog.All.Count, DsrCatalog.All.Count);
    }

    [Fact]
    public void Concrete_members_name_the_standards_for_their_materials()
    {
        var d = DsrCatalog.For("rcc.slabs")!;
        Assert.Contains(d.Materials, m => m.Material == "Reinforcement steel" && m.Standard.Contains("IS 1786"));
        Assert.Contains(d.Materials, m => m.Material == "Cement" && m.Standard.Contains("IS 269"));
        Assert.Contains(d.Materials, m => m.Standard.Contains("IS 383"));
    }

    [Fact]
    public void Masonry_is_measured_the_way_the_app_computes_it()
    {
        var d = DsrCatalog.For("Masonry")!;
        Assert.Contains("0.1", d.MeasurementRule);
        Assert.Contains("half", d.MeasurementRule, StringComparison.OrdinalIgnoreCase);
    }
}
