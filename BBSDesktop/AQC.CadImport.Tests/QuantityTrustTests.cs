// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Aqc.CadImport;
using Xunit;

namespace AQC.CadImport.Tests;

/// <summary>
/// Quantity-trust checklist: known wall/opening numbers that must survive
/// bridge / project take-off → CadImport without double-count, broken wall_ref, or thickness regressions.
/// Fixture mirrors O-Draft <c>crates/engine/tests/fixtures/quantity-trust-checklist.json</c>.
/// </summary>
public class QuantityTrustTests
{
    const string Company = "quantity-trust-co";
    const long ProjectId = 42;
    const string DrawingId = "quantity-trust-checklist-0001";

    static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", n));
    static BridgeFile Checklist() => BridgeFile.Parse(Fixture("quantity-trust-checklist.json"));

    static ImportModel Empty()
    {
        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        m.Project["name"] = "Untitled";
        return m;
    }

    static ImportPlan Run(BridgeFile f, ImportModel m)
    {
        var p = CadImporter.Plan(f, m);
        CadImporter.Apply(p, m);
        return p;
    }

    [Fact]
    public void Checklist_imports_known_wall_and_opening_numbers()
    {
        var f = Checklist();
        Assert.Equal(DrawingId, f.DrawingId);
        Assert.Equal(Company, f.ProjectRefCompany);
        Assert.Equal(ProjectId, f.ProjectRefId);
        Assert.Equal(2, f.Walls.Count);
        Assert.Equal(2, f.Openings.Count);

        var m = Empty();
        var plan = Run(f, m);

        Assert.Equal(2, m.Sheet("masonry").Count);
        Assert.Equal(2, m.Sheet("masonry_openings").Count);
        Assert.Single(m.Sheet("doors"));
        Assert.Single(m.Sheet("windows"));

        var front = m.Sheet("masonry").Single(r => r["source_ref"] == "W-FRONT");
        var back = m.Sheet("masonry").Single(r => r["source_ref"] == "W-BACK");
        Assert.Equal("6000", front["length"]);
        Assert.Equal("3000", front["height"]);
        Assert.Equal("230", front["thickness"]);
        Assert.Equal("6000", back["length"]);
        Assert.Equal("230", back["thickness"]);
        Assert.All(plan.Items.Where(i => i.Section == "masonry"), i => Assert.True(string.IsNullOrEmpty(i.Reason) || !i.Reason.Contains("assumed"), i.Reason));

        // Openings deduct from the front wall only (wall_ref → wall_mark).
        Assert.All(m.Sheet("masonry_openings"), r => Assert.Equal(front["mark"], r["wall_mark"]));
        var door = m.Sheet("masonry_openings").Single(r => r["opening_kind"] == "Door");
        var win = m.Sheet("masonry_openings").Single(r => r["opening_kind"] == "Window");
        Assert.Equal("900", door["opening_l"]);
        Assert.Equal("2100", door["opening_h"]);
        Assert.Equal("1200", win["opening_l"]);
        Assert.Equal("1200", win["opening_h"]);

        // Schedule counts only — never a second deduct.
        Assert.All(m.Sheet("doors").Concat(m.Sheet("windows")), r => Assert.Equal("No", r["deduct_from_wall"]));
        Assert.Equal("1", m.Sheet("doors").Single()["nos"]);
        Assert.Equal("1", m.Sheet("windows").Single()["nos"]);
    }

    [Fact]
    public void Checklist_face_area_numbers_match_published_net()
    {
        // Documented QS target: front 18 − 1.89 − 1.44 = 14.67; back 18; total 32.67 m².
        var root = (JsonObject)JsonNode.Parse(Fixture("quantity-trust-checklist.json"))!;
        var expect = (JsonObject)root["_quantity_trust"]!["expected_face_m2"]!;
        double G(string k) => expect[k]!.GetValue<double>();
        Assert.Equal(18.0, G("front_gross"));
        Assert.Equal(1.89, G("door_deduct"));
        Assert.Equal(1.44, G("window_deduct"));
        Assert.Equal(14.67, G("front_net"));
        Assert.Equal(18.0, G("back_net"));
        Assert.Equal(32.67, G("total_net"));
        Assert.Equal(G("front_net"), G("front_gross") - G("door_deduct") - G("window_deduct"), 3);
        Assert.Equal(G("total_net"), G("front_net") + G("back_net"), 3);

        // Same numbers from imported facts (length_m × height_m − openings).
        var f = Checklist();
        static double Mm(JsonObject o, string key) => (double)o[key]!;
        static double Face(JsonObject w) => Mm(w, "length_mm") / 1000.0 * Mm(w, "height_mm") / 1000.0;
        static double Deduct(JsonObject o) => Mm(o, "width_mm") / 1000.0 * Mm(o, "height_mm") / 1000.0;
        var front = f.Walls.Single(w => (string)w["ref"]! == "W-FRONT");
        var back = f.Walls.Single(w => (string)w["ref"]! == "W-BACK");
        var frontDeduct = f.Openings.Where(o => (string)o["wall_ref"]! == "W-FRONT").Sum(Deduct);
        Assert.Equal(18.0, Face(front), 3);
        Assert.Equal(18.0, Face(back), 3);
        Assert.Equal(14.67, Face(front) - frontDeduct, 3);
        Assert.Equal(32.67, Face(front) - frontDeduct + Face(back), 3);
    }

    [Fact]
    public void Reimport_does_not_double_count()
    {
        var m = Empty();
        Run(Checklist(), m);
        var again = CadImporter.Plan(Checklist(), m);
        Assert.Equal(0, again.Count(PlanAction.Add));
        Assert.Equal(0, again.Count(PlanAction.Update));
        Assert.Empty(again.Warnings);
        Assert.Equal(2, m.Sheet("masonry").Count);
        Assert.Equal(2, m.Sheet("masonry_openings").Count);
    }

    [Fact]
    public void Missing_wall_ref_is_flagged_and_does_not_deduct()
    {
        var root = (JsonObject)JsonNode.Parse(Fixture("quantity-trust-checklist.json"))!;
        foreach (var o in root["openings"]!.AsArray().OfType<JsonObject>())
            o.Remove("wall_ref");
        var f = BridgeFile.Parse(root.ToJsonString());
        var m = Empty();
        var plan = Run(f, m);
        Assert.All(m.Sheet("masonry_openings"), r => Assert.Equal("", r["wall_mark"]));
        Assert.Contains(plan.Items, i => i.Section == "masonry_openings" && i.Reason.Contains("will not deduct"));
    }

    [Fact]
    public void Thickness_regression_without_mm_falls_back_to_layer_nominal()
    {
        var root = (JsonObject)JsonNode.Parse(Fixture("quantity-trust-checklist.json"))!;
        foreach (var w in root["walls"]!.AsArray().OfType<JsonObject>())
            w.Remove("thickness_mm");
        var m = Empty();
        var plan = Run(BridgeFile.Parse(root.ToJsonString()), m);
        Assert.All(m.Sheet("masonry"), r => Assert.Equal("230", r["thickness"]));
        Assert.Contains(plan.Items, i => i.Section == "masonry" && i.Reason.Contains("230") && i.Reason.Contains("MEASURE-FULLBRICK"));
    }

    [Fact]
    public void Second_drawing_same_floor_warns_double_count()
    {
        var m = Empty();
        Run(Checklist(), m);
        var other = Fixture("quantity-trust-checklist.json")
            .Replace(DrawingId, "quantity-trust-checklist-0002")
            .Replace("W-FRONT", "W-FRONT-B")
            .Replace("W-BACK", "W-BACK-B")
            .Replace("O-D2", "O-D2-B")
            .Replace("O-W3", "O-W3-B");
        var plan = CadImporter.Plan(BridgeFile.Parse(other), m);
        Assert.Contains(plan.Warnings, w => w.Contains(DrawingId) && w.Contains("Ground") && w.Contains("twice"));
        Assert.True(plan.Count(PlanAction.Add) > 0);
    }

    [Fact]
    public void Takeoff_records_from_checklist_carry_project_ref_and_import()
    {
        var recs = OspTakeoffTests.RecordsFromFixture(
            "quantity-trust-checklist.json",
            projectRef: new JsonObject { ["v"] = 1, ["company_uuid"] = Company, ["project_id"] = ProjectId });
        var d = Assert.Single(OspTakeoff.ListDrawings(recs));
        Assert.Equal(Company, d.ProjectRefCompany);
        Assert.Equal(ProjectId, d.ProjectRefId);
        Assert.True(d.IsComplete);

        var read = OspTakeoff.Read(recs, d.Slot);
        Assert.Equal(Company, read.File.ProjectRefCompany);
        Assert.Equal(ProjectId, read.File.ProjectRefId);
        Assert.Equal(2, read.File.Walls.Count);
        Assert.All(read.File.Openings, o => Assert.Equal("W-FRONT", (string)o["wall_ref"]!));

        var m = Empty();
        Run(read.File, m);
        Assert.Equal(2, m.Sheet("masonry").Count);
        var frontMark = m.Sheet("masonry").Single(r => r["source_ref"] == "W-FRONT")["mark"];
        Assert.All(m.Sheet("masonry_openings"), r => Assert.Equal(frontMark, r["wall_mark"]));
    }
}
