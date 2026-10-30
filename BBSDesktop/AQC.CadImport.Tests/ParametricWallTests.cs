using System.Text.Json.Nodes;
using Aqc.CadImport;
using Xunit;

namespace AQC.CadImport.Tests;

/// <summary>
/// The wall rows O-Draft emits for parametric walls (its hand-off HANDOFF-O-COREOP-PARAMETRIC-TAKEOFF.md, 2026-10-19), next to the brick take-off rows
/// it already wrote. The row shapes below are copied from that document.
/// </summary>
public class ParametricWallTests
{
    const string Parametric = """{"ref":"PW-wall-1","level":"Ground","length_mm":5000.0,"height_mm":3000.0,"thickness_mm":230.0,"layer":"A-WALL"}""";
    const string BrickFull = """{"ref":"W1A2","level":"Ground","length_mm":5000.0,"height_mm":2850.0,"layer":"MEASURE-FULLBRICK"}""";
    const string BrickHalf = """{"ref":"W1B0","level":"Ground","length_mm":2000.0,"height_mm":2850.0,"layer":"MEASURE-HALFBRICK"}""";

    static TakeoffRecord Header() => new(OspTakeoff.HeaderId(7), 0, 0, 0, 0,
        """{"format":"hcw-takeoff-shilpi","version":1,"units":"mm","state":"complete","source":{"app":"O-Draft","drawing":"p.dxf","drawing_id":"pp-1"},"levels":[{"name":"Ground","height_mm":3150,"slab_thickness_mm":150,"beam_depth_mm":450}]}""");

    static TakeoffRecord Wall(uint n, string payload, double minX = 0, double minY = -115, double maxX = 5000, double maxY = 115) =>
        new(OspTakeoff.MakeId(OspTakeoff.KindWall, 7, n), minX, minY, maxX, maxY, payload);

    static ImportModel Empty()
    {
        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        return m;
    }

    static (ImportPlan plan, ImportModel model) Import(params TakeoffRecord[] walls)
    {
        var m = Empty();
        var plan = CadImporter.Plan(OspTakeoff.Read(new[] { Header() }.Concat(walls)).File, m);
        CadImporter.Apply(plan, m);
        return (plan, m);
    }

    [Fact]
    public void A_parametric_wall_is_imported_with_its_own_thickness_and_no_warning_about_it()
    {
        var (plan, m) = Import(Wall(1, Parametric));
        var row = Assert.Single(m.Sheet("masonry"));
        Assert.Equal("230", row["thickness"]);
        Assert.Equal("5000", row["length"]);
        Assert.Equal("3000", row["height"]);
        Assert.Equal("PW-wall-1", row["source_ref"]);   // the PW- ref is the stable identity, not parsed as W + handle
        Assert.Equal("", plan.Items.Single(i => i.Section == "masonry").Reason);
    }

    [Fact]
    public void Parametric_and_brick_walls_in_one_drawing_each_keep_their_own_thickness()
    {
        var (_, m) = Import(Wall(1, Parametric), Wall(2, BrickFull), Wall(3, BrickHalf));
        var byRef = m.Sheet("masonry").ToDictionary(r => r["source_ref"]);
        Assert.Equal("230", byRef["PW-wall-1"]["thickness"]);
        Assert.Equal("230", byRef["W1A2"]["thickness"]);   // full brick, from the layer
        Assert.Equal("115", byRef["W1B0"]["thickness"]);   // half brick, from the layer
        Assert.Equal(3, byRef.Count);
    }

    [Fact]
    public void A_brick_wall_says_its_thickness_came_from_its_layer_and_a_half_brick_wall_is_flagged_as_AQCs_110_wall()
    {
        var (plan, _) = Import(Wall(2, BrickFull), Wall(3, BrickHalf));
        Assert.Contains("full brick", plan.Items.Single(i => i.Ref == "W1A2").Reason);
        Assert.Contains("110 mm wall", plan.Items.Single(i => i.Ref == "W1B0").Reason);
    }

    [Fact]
    public void A_wall_on_an_unknown_layer_with_no_thickness_is_still_assumed_and_flagged()
    {
        var odd = BrickFull.Replace("MEASURE-FULLBRICK", "SOMETHING-ELSE");
        var (plan, m) = Import(Wall(2, odd));
        Assert.Equal("230", m.Sheet("masonry")[0]["thickness"]);
        Assert.Contains("assumed", plan.Items.Single(i => i.Section == "masonry").Reason);
    }

    [Fact]
    public void A_wall_row_note_from_O_Draft_is_shown_in_the_preview_even_when_thickness_is_stated()
    {
        // Current O-Draft brick walls send thickness_mm and an honesty _note; without this, a stated thickness looked "trusted".
        var noted = """{"ref":"W9","level":"Ground","length_mm":4000.0,"height_mm":2850.0,"thickness_mm":230.0,"layer":"MEASURE-FULLBRICK","_note":"thickness 230 mm is the nominal value the measure layer implies, not a measured thickness; check it"}""";
        var (plan, m) = Import(Wall(9, noted));
        Assert.Equal("230", m.Sheet("masonry")[0]["thickness"]);
        Assert.Contains("nominal", plan.Items.Single(i => i.Section == "masonry").Reason);
    }

    [Fact]
    public void Re_recording_keeps_the_parametric_wall_and_a_changed_one_updates_in_place()
    {
        var (_, m) = Import(Wall(1, Parametric));
        var longer = Parametric.Replace("5000.0", "6500.0");
        var plan = CadImporter.Plan(OspTakeoff.Read(new[] { Header(), Wall(1, longer) }).File, m);
        Assert.Equal(1, plan.Count(PlanAction.Update));
        CadImporter.Apply(plan, m);
        var row = Assert.Single(m.Sheet("masonry"));
        Assert.Equal("6500", row["length"]);
    }

    [Fact]
    public void A_parametric_wall_no_longer_recorded_is_reported_not_deleted()
    {
        var (_, m) = Import(Wall(1, Parametric), Wall(2, BrickFull));
        var plan = CadImporter.Plan(OspTakeoff.Read(new[] { Header(), Wall(2, BrickFull) }).File, m);   // the PW- wall went stale or was detached
        Assert.Contains(plan.Items, i => i.Action == PlanAction.NoLongerInDrawing && i.Ref == "PW-wall-1");
        CadImporter.Apply(plan, m);
        Assert.Equal(2, m.Sheet("masonry").Count);
    }

    [Fact]
    public void An_opening_is_placed_in_a_parametric_wall_by_position_like_any_other()
    {
        var door = new TakeoffRecord(OspTakeoff.MakeId(OspTakeoff.KindOpening, 7, 9), 1000, -115, 1900, 1015,
            """{"ref":"O1","kind":"Door","mark":"D2","level":"Ground","width_mm":900,"height_mm":2100}""");
        var file = OspTakeoff.Read(new[] { Header(), Wall(1, Parametric), door }).File;
        Assert.Equal("PW-wall-1", (string)file.Openings[0]["wall_ref"]!);
    }

    // ---- the optional fields O-Draft offered to add (its section 4B): accepted if they arrive, never required ----

    [Fact]
    public void Material_when_present_chooses_the_unit_type_and_a_default_thickness()
    {
        var block = """{"ref":"PW-b","level":"Ground","length_mm":4000.0,"height_mm":3000.0,"material":"block.concrete","layer":"A-WALL"}""";
        var half = """{"ref":"PW-h","level":"Ground","length_mm":4000.0,"height_mm":3000.0,"material":"brick.half","layer":"A-WALL"}""";
        var (plan, m) = Import(Wall(1, block), Wall(2, half));
        var byRef = m.Sheet("masonry").ToDictionary(r => r["source_ref"]);
        Assert.Equal("Cement Block", byRef["PW-b"]["unit_type"]);
        Assert.Equal("200", byRef["PW-b"]["thickness"]);
        Assert.Contains("assumed", plan.Items.Single(i => i.Ref == "PW-b").Reason);
        Assert.Equal("Brick", byRef["PW-h"]["unit_type"]);
        Assert.Equal("115", byRef["PW-h"]["thickness"]);
    }

    [Fact]
    public void An_rcc_wall_is_not_masonry_and_is_skipped_with_that_reason()
    {
        var rcc = """{"ref":"PW-r","level":"Ground","length_mm":4000.0,"height_mm":3000.0,"thickness_mm":200.0,"material":"rcc","layer":"A-WALL"}""";
        var (plan, m) = Import(Wall(1, rcc), Wall(2, Parametric));
        Assert.Single(m.Sheet("masonry"));
        Assert.Contains(plan.Items, i => i.Action == PlanAction.Skipped && i.Ref == "PW-r" && i.Reason.Contains("RCC"));
    }

    [Fact]
    public void Finish_and_the_provenance_fields_are_ignored_because_finishes_are_derived_here()
    {
        var rich = """{"ref":"PW-x","level":"Ground","length_mm":4000.0,"height_mm":3000.0,"thickness_mm":230.0,"layer":"A-WALL","material":"brick.full","finish":"plaster.internal","component_type":"osas.wall","schema_version":1}""";
        var (_, m) = Import(Wall(1, rich));
        Assert.Empty(m.Sheet("plaster"));
        Assert.Single(m.Sheet("masonry"));
        Assert.DoesNotContain("finish", m.Sheet("masonry")[0].Keys);
    }
}
