using System.Text.Json.Nodes;
using Aqc.CadImport;
using Xunit;

namespace AQC.CadImport.Tests;

/// <summary>The estimation and running-bill phase O-Draft puts on a row (<c>phase</c>, <c>bill_phase</c>) reaches the tables.</summary>
public class PhaseImportTests
{
    static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", n));

    static ImportModel NewModel()
    {
        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        m.Project["name"] = "Untitled Project";
        return m;
    }

    /// <summary>The small house with <paramref name="phase"/> and <paramref name="billPhase"/> on every wall, column, lintel, slab and opening.</summary>
    static BridgeFile House(string? phase, string? billPhase)
    {
        var root = JsonNode.Parse(Fixture("small-house.json"))!.AsObject();
        foreach (var section in new[] { "walls", "columns", "lintels", "slabs", "openings" })
            foreach (var row in root[section]!.AsArray().OfType<JsonObject>())
            {
                row.Remove("phase");
                row.Remove("bill_phase");
                if (phase is not null) row["phase"] = phase;
                if (billPhase is not null) row["bill_phase"] = billPhase;
            }
        return BridgeFile.Parse(root.ToJsonString());
    }

    static ImportPlan Run(BridgeFile f, ImportModel m)
    {
        var p = CadImporter.Plan(f, m, null);
        CadImporter.Apply(p, m);
        return p;
    }

    [Fact]
    public void Phases_land_on_the_rows_of_every_section_that_carries_them()
    {
        var m = NewModel();
        Run(House("Foundation", "Phase 1"), m);
        foreach (var section in new[] { "masonry", "masonry_openings", "columns", "lintels", "slabs" })
        {
            Assert.NotEmpty(m.Sheet(section));
            Assert.All(m.Sheet(section), r =>
            {
                Assert.Equal("Foundation", r["phase"]);
                Assert.Equal("Phase 1", r["bill_phase"]);
            });
        }
    }

    [Fact]
    public void A_phase_changed_in_the_drawing_updates_the_row_and_leaves_the_geometry_alone()
    {
        var m = NewModel();
        Run(House("Foundation", "Phase 1"), m);
        var wall = m.Sheet("masonry").First();
        var hash = wall["source_hash"];
        var length = wall["length"];

        var plan = Run(House("Superstructure", "Phase 1"), m);

        Assert.Equal("Superstructure", wall["phase"]);
        Assert.Equal("Phase 1", wall["bill_phase"]);
        Assert.Equal(hash, wall["source_hash"]);
        Assert.Equal(length, wall["length"]);
        Assert.Contains(plan.Items, i => i.Action == PlanAction.Update && i.Section == "masonry" && i.Reason.Contains("phase"));
        Assert.Equal(0, plan.Count(PlanAction.Add));
    }

    [Fact]
    public void The_same_phases_import_again_as_unchanged()
    {
        var m = NewModel();
        Run(House("Plinth", "Phase 2"), m);
        var again = CadImporter.Plan(House("Plinth", "Phase 2"), m, null);
        Assert.False(again.HasChanges, again.Summary);
    }

    [Fact]
    public void A_file_without_phases_keeps_a_phase_typed_in_the_table()
    {
        var m = NewModel();
        Run(House(null, null), m);
        var wall = m.Sheet("masonry").First();
        Assert.False(wall.ContainsKey("phase"), "no phase is invented");

        wall["phase"] = "Finishing";
        var plan = Run(House(null, null), m);

        Assert.Equal("Finishing", wall["phase"]);
        Assert.False(plan.HasChanges, plan.Summary);
    }
}
