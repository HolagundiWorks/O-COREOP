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
    public void The_standard_relationships_are_allowed(string from, string basis, string to)
    {
        var (ok, reason) = FlowRules.Check(C(from), Out(from, basis), C(to));
        Assert.True(ok, reason);
    }

    [Theory]
    [InlineData("rcc.columns", "Volume", "Masonry")]
    [InlineData("Masonry", "Volume", "rcc.beams")]
    [InlineData("rcc.slabs", "Area", "Plaster")]
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
