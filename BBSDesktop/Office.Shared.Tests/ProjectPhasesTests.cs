// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Office.Shared;
using Xunit;

namespace Office.Shared.Tests;

public class ProjectPhasesTests
{
    [Fact]
    public void A_new_project_starts_with_the_default_stages_and_no_billing_phases()
    {
        var p = ProjectPhases.CreateDefault();
        Assert.Equal(new[] { "Foundation", "Plinth", "Superstructure", "Finishing" }, p.Stages.Select(s => s.Name));
        Assert.Empty(p.Phases);
    }

    [Fact]
    public void Billing_phases_are_numbered_from_one_and_a_stage_belongs_to_one_phase()
    {
        var p = ProjectPhases.CreateDefault();
        var first = p.AddPhase("Substructure", new[] { "foundation", "plinth" });
        var second = p.AddPhase("Structure", new[] { "plinth", "superstructure", "no-such-stage" });
        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.Equal(new[] { "foundation" }, first.StageIds);
        Assert.Equal(new[] { "plinth", "superstructure" }, second.StageIds);
        Assert.Equal(2, p.PhaseOf("PLINTH")!.Number);
        Assert.Null(p.PhaseOf("finishing"));
        Assert.Null(p.PhaseOf(null));
    }

    [Fact]
    public void Phases_round_trip_through_json()
    {
        var p = ProjectPhases.CreateDefault();
        p.AddPhase("Substructure", new[] { "foundation", "plinth" });
        var back = ProjectPhases.FromJson(JsonNode.Parse(p.ToJson().ToJsonString())!.AsObject());
        Assert.Equal(p.ToJson().ToJsonString(), back.ToJson().ToJsonString());
    }

    [Fact]
    public void A_project_saved_before_phases_existed_gets_the_defaults()
    {
        Assert.Equal(4, ProjectPhases.FromJson(null).Stages.Count);
        Assert.Equal(4, ProjectPhases.FromJson(new JsonObject()).Stages.Count);
    }

    [Fact]
    public void Damaged_phase_data_is_repaired_not_trusted()
    {
        var o = JsonNode.Parse("""
            {"stages":[{"id":"a","name":"A"},{"id":"a","name":"again"},{"id":""},{"name":"no id"}],
             "billing_phases":[{"number":0,"name":"zero"},{"number":1,"stages":["a","gone"]},{"number":1,"stages":["a"]},{"number":2,"stages":["a"]}]}
            """)!.AsObject();
        var p = ProjectPhases.FromJson(o);
        Assert.Equal(new[] { "a" }, p.Stages.Select(s => s.Id));
        Assert.Equal(new[] { 1, 2 }, p.Phases.Select(x => x.Number));
        Assert.Equal(new[] { "a" }, p.Phases[0].StageIds);
        Assert.Empty(p.Phases[1].StageIds);
    }
}

public class RevisionLabelsTests
{
    [Fact]
    public void Revision_zero_is_the_first_issue_and_next_counts_up()
    {
        Assert.Equal("Rev 0", RevisionLabels.Revision(0));
        Assert.Equal("Rev 0", RevisionLabels.Revision(-3));
        Assert.Equal(1, RevisionLabels.Next(0));
        Assert.Equal(1, RevisionLabels.Next(-5));
        Assert.Equal(4, RevisionLabels.Next(3));
    }

    [Fact]
    public void Estimates_and_running_bills_are_named_with_their_revision()
    {
        Assert.Equal("Estimate Rev 2", RevisionLabels.Estimate(2));
        Assert.Equal("Estimate Rev 0 · Foundation", RevisionLabels.Estimate(0, " Foundation "));
        Assert.Equal("ABC/RA/2026-27/003 Rev 1", RevisionLabels.RunningBill("ABC/RA/2026-27/003", 1));
        Assert.Equal("Running bill (not numbered) Rev 0", RevisionLabels.RunningBill("", 0));
    }
}

public class PhaseTotalsTests
{
    [Fact]
    public void Amounts_are_grouped_in_the_project_order_with_no_phase_last()
    {
        var items = new (string?, double)[] { ("Plinth", 10), (null, 5), ("foundation", 20), ("Plinth", 2.5), ("Roofing", 1), ("", 4) };
        var g = PhaseTotals.Group(items, new[] { "Foundation", "Plinth", "Superstructure" });
        Assert.Equal(new[] { "Foundation", "Plinth", "Roofing", "(no phase)" }, g.Select(x => x.Phase));
        Assert.Equal(new[] { 20.0, 12.5, 1.0, 9.0 }, g.Select(x => x.Amount));
    }

    [Fact]
    public void Nothing_in_gives_nothing_out_and_bad_numbers_are_ignored()
    {
        Assert.Empty(PhaseTotals.Group(Array.Empty<(string?, double)>(), new[] { "A" }));
        var g = PhaseTotals.Group(new (string?, double)[] { ("A", double.NaN), ("A", double.PositiveInfinity), ("A", 3) }, new[] { "A" });
        Assert.Equal(3.0, Assert.Single(g).Amount);
    }
}
