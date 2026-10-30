using System.Text.Json.Nodes;
using Aqc.CadImport;
using Xunit;

namespace AQC.CadImport.Tests;

/// <summary>
/// The records O-Draft's own `takeoff.record` wrote on 2026-10-19 (fixtures/odraft-takeoff-records.json: ids, boxes and payloads
/// exactly as the gateway returned them), read by O-CoreOp's reader. It differs from the contract's samples in ways that matter: the room has no
/// level and no length or breadth, the walls have no thickness, the openings have no wall_ref.
/// </summary>
public class ODraftOutputTests
{
    static List<TakeoffRecord> Real() =>
        ((JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "odraft-takeoff-records.json")))!)
        .Select(n => new TakeoffRecord(ulong.Parse((string)n!["id"]!), (double)n["minX"]!, (double)n["minY"]!, (double)n["maxX"]!, (double)n["maxY"]!, (string)n["payload"]!))
        .ToList();

    static ImportModel Empty()
    {
        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        return m;
    }

    static List<TakeoffRecord> Edited(Func<TakeoffRecord, TakeoffRecord> f) => Real().Select(f).ToList();

    [Fact]
    public void O_Drafts_slot_matches_the_slot_this_reader_computes_for_the_same_drawing_id()
    {
        var d = Assert.Single(OspTakeoff.ListDrawings(Real()));
        Assert.Equal(OspTakeoff.SlotFor(d.DrawingId), d.Slot);   // two independent implementations of the contract agree
        Assert.True(d.IsComplete);
        Assert.Null(d.Problem);
        Assert.Equal(7, d.Rows);
    }

    [Fact]
    public void The_real_database_reads_without_anything_skipped()
    {
        var r = OspTakeoff.Read(Real());
        Assert.Empty(r.Skipped);
        Assert.Single(r.File.Walls);
        Assert.Equal(2, r.File.Openings.Count);
        Assert.Single(r.File.Columns); Assert.Single(r.File.Lintels); Assert.Single(r.File.Rooms); Assert.Single(r.File.Slabs);
        Assert.Equal("Ground", r.File.Levels[0].Name);
    }

    [Fact]
    public void The_room_gets_its_length_and_breadth_from_its_outline()
    {
        var r = OspTakeoff.Read(Real());
        var room = r.File.Rooms[0];
        Assert.Equal(4000, (double)room["length_mm"]!);
        Assert.Equal(3000, (double)room["breadth_mm"]!);
        Assert.Contains(r.Notes!, n => n.Contains("GF BEDROOM") && n.Contains("outline"));
        Assert.Null(room["_note"]);   // the outline agrees with the 12 m2 area, so there is nothing to check
    }

    [Fact]
    public void An_l_shaped_room_keeps_its_area_and_says_the_size_is_approximate()
    {
        var recs = Edited(x => OspTakeoff.KindOf(x.Id) == OspTakeoff.KindRoom ? x with { PayloadText = x.PayloadText!.Replace("\"area_m2\":12.0", "\"area_m2\":9.0") } : x);
        var room = OspTakeoff.Read(recs).File.Rooms[0];
        Assert.Equal(3000, (double)room["breadth_mm"]!);
        Assert.Equal(3000, (double)room["length_mm"]!);   // 9 m2 / 3 m, not the 4 m of the box
        Assert.Contains("approximate", (string)room["_note"]!);
    }

    [Fact]
    public void Openings_are_placed_in_the_wall_they_sit_on_by_position()
    {
        var r = OspTakeoff.Read(Real());
        Assert.All(r.File.Openings, o => Assert.Equal("W120", (string)o["wall_ref"]!));
        Assert.Equal(2, r.Notes!.Count(n => n.Contains("placed in wall W120")));
    }

    [Fact]
    public void An_opening_far_from_every_wall_is_left_without_a_wall()
    {
        var recs = Edited(x => OspTakeoff.KindOf(x.Id) == OspTakeoff.KindOpening && x.PayloadText!.Contains("\"D2\"")
            ? x with { MinY = 5000, MaxY = 6000 } : x);
        var door = OspTakeoff.Read(recs).File.Openings.Single(o => (string)o["mark"]! == "D2");
        Assert.Null(door["wall_ref"]);
    }

    [Fact]
    public void An_opening_on_another_level_is_not_matched_to_the_wall()
    {
        var recs = Edited(x => OspTakeoff.KindOf(x.Id) == OspTakeoff.KindOpening ? x with { PayloadText = x.PayloadText!.Replace("\"Ground\"", "\"First\"") } : x);
        Assert.All(OspTakeoff.Read(recs).File.Openings, o => Assert.Null(o["wall_ref"]));
    }

    [Fact]
    public void The_real_take_off_imports_completely_with_the_assumptions_flagged_in_the_preview()
    {
        var m = Empty();
        var plan = CadImporter.Plan(OspTakeoff.Read(Real()).File, m);

        Assert.Equal(0, plan.Count(PlanAction.Skipped));
        // level 'Ground' is new; rows: 1 wall, 2 opening rows, 1 door + 1 window schedule, 1 column, 1 lintel, 1 room floor, 1 slab
        var wall = plan.Items.Single(i => i.Section == "masonry");
        Assert.Contains("230 mm (full brick) taken from layer MEASURE-FULLBRICK", wall.Reason);   // no thickness_mm, so the layer says it
        Assert.Equal("230", wall.Row!["thickness"]);
        Assert.All(plan.Items.Where(i => i.Section == "masonry_openings"), i => Assert.Contains("chosen by position", i.Reason));
        Assert.Equal("4000", plan.Items.Single(i => i.Section == "flooring").Row!["length"]);
        Assert.Equal("3000", plan.Items.Single(i => i.Section == "flooring").Row!["breadth"]);

        CadImporter.Apply(plan, m);
        Assert.Single(m.Sheet("masonry"));
        Assert.All(m.Sheet("masonry_openings"), r => Assert.Equal(m.Sheet("masonry")[0]["mark"], r["wall_mark"]));   // they deduct from that wall
        Assert.Single(m.Sheet("slabs"));
        Assert.Equal("Two-Way", m.Sheet("slabs")[0]["slab_type"]);   // 9 m x 6 m
        // importing the same database again changes nothing
        var again = CadImporter.Plan(OspTakeoff.Read(Real()).File, m);
        Assert.Equal(0, again.Count(PlanAction.Add));
        Assert.Equal(0, again.Count(PlanAction.Update));
    }

    [Fact]
    public void A_row_with_no_level_in_a_file_with_several_floors_is_skipped_with_that_reason()
    {
        var recs = Real();
        recs[0] = recs[0] with { PayloadText = recs[0].PayloadText!.Replace("\"levels\":[", "\"levels\":[{\"name\":\"Basement\",\"height_mm\":3000,\"slab_thickness_mm\":150,\"beam_depth_mm\":450},") };
        var plan = CadImporter.Plan(OspTakeoff.Read(recs).File, Empty());
        Assert.Contains(plan.Items, i => i.Section == "flooring" && i.Action == PlanAction.Skipped && i.Reason.Contains("several floors"));
    }

    [Fact]
    public void An_unsaved_drawing_with_no_name_is_called_by_its_slot_in_every_message()
    {
        var d = Assert.Single(OspTakeoff.ListDrawings(Real()));
        Assert.Equal("", d.Drawing);
        Assert.Equal("drawing 11694033", d.DisplayName);
        var ex = Assert.Throws<BridgeFormatException>(() => OspTakeoff.ReadAnnotatedPdf(Real(), d.Slot));
        Assert.StartsWith("drawing 11694033:", ex.Message);   // not ": no annotated drawing ..."
        var recording = Real();
        recording[0] = recording[0] with { PayloadText = recording[0].PayloadText!.Replace("\"state\":\"complete\"", "\"state\":\"recording\"") };
        Assert.Contains("drawing 11694033", Assert.Throws<BridgeFormatException>(() => OspTakeoff.Read(recording)).Message);
    }

    [Fact]
    public void A_ventilator_is_imported_as_a_window()
    {
        var recs = Edited(x => OspTakeoff.KindOf(x.Id) == OspTakeoff.KindOpening && x.PayloadText!.Contains("\"W3\"")
            ? x with { PayloadText = x.PayloadText!.Replace("\"Window\"", "\"Ventilator\"").Replace("\"W3\"", "\"V1\"") } : x);
        var m = Empty();
        CadImporter.Apply(CadImporter.Plan(OspTakeoff.Read(recs).File, m), m);
        Assert.Contains(m.Sheet("windows"), r => r["mark"] == "V1");
        Assert.DoesNotContain(m.Sheet("doors"), r => r["mark"] == "V1");
        Assert.Contains(m.Sheet("masonry_openings"), r => r["opening_kind"] == "Window" && r["opening_l"] == "1200");
    }
}
