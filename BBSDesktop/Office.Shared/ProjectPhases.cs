// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;

namespace Office.Shared;

/// <summary>A stage of the building work that quantities, estimate lines and bill lines are grouped by.</summary>
public sealed record WorkStage(string Id, string Name);

/// <summary>
/// A billing phase: the work stages one running bill may cover. Phases are numbered from 1 in the order the
/// contract bills them.
/// </summary>
public sealed class BillingPhase
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public List<string> StageIds { get; set; } = new();

    public JsonObject ToJson() => new()
    {
        ["number"] = Number,
        ["name"] = Name,
        ["stages"] = new JsonArray(StageIds.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
    };

    public static BillingPhase? FromJson(JsonObject? o)
    {
        if (o is null) return null;
        var number = o["number"]?.GetValue<int>() ?? 0;
        if (number < 1) return null;
        var p = new BillingPhase { Number = number, Name = o["name"]?.GetValue<string>() ?? "" };
        if (o["stages"] is JsonArray a)
            foreach (var s in a)
                if (s?.GetValue<string>() is { Length: > 0 } id && !p.StageIds.Contains(id))
                    p.StageIds.Add(id);
        return p;
    }
}

/// <summary>The work stages and billing phases of a project, with the defaults a new project starts from.</summary>
public sealed class ProjectPhases
{
    /// <summary>Stages a new project starts with. The user can rename, add and remove them.</summary>
    public static IReadOnlyList<WorkStage> DefaultStages { get; } = new[]
    {
        new WorkStage("foundation", "Foundation"),
        new WorkStage("plinth", "Plinth"),
        new WorkStage("superstructure", "Superstructure"),
        new WorkStage("finishing", "Finishing"),
    };

    public List<WorkStage> Stages { get; } = new();
    public List<BillingPhase> Phases { get; } = new();

    public static ProjectPhases CreateDefault()
    {
        var p = new ProjectPhases();
        p.Stages.AddRange(DefaultStages);
        return p;
    }

    public WorkStage? Stage(string? id) => id is null ? null : Stages.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The billing phase that covers a work stage, or null when no phase does.</summary>
    public BillingPhase? PhaseOf(string? stageId) =>
        stageId is null ? null : Phases.FirstOrDefault(p => p.StageIds.Any(s => string.Equals(s, stageId, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Adds a billing phase with the next number. A stage can belong to one phase only: it moves here from any other.</summary>
    public BillingPhase AddPhase(string name, IEnumerable<string> stageIds)
    {
        var ids = stageIds.Where(id => Stage(id) is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var other in Phases)
            other.StageIds.RemoveAll(s => ids.Contains(s, StringComparer.OrdinalIgnoreCase));
        var phase = new BillingPhase { Number = (Phases.Count == 0 ? 0 : Phases.Max(p => p.Number)) + 1, Name = name.Trim(), StageIds = ids };
        Phases.Add(phase);
        return phase;
    }

    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["stages"] = new JsonArray(Stages.Select(s => (JsonNode?)new JsonObject { ["id"] = s.Id, ["name"] = s.Name }).ToArray()),
            ["billing_phases"] = new JsonArray(Phases.Select(p => (JsonNode?)p.ToJson()).ToArray()),
        };
        return o;
    }

    /// <summary>Reads what <see cref="ToJson"/> wrote. A project saved before phases existed gets the default stages and no billing phases.</summary>
    public static ProjectPhases FromJson(JsonObject? o)
    {
        var p = new ProjectPhases();
        if (o?["stages"] is JsonArray stages)
        {
            foreach (var s in stages.OfType<JsonObject>())
            {
                var id = s["id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(id) && p.Stage(id) is null)
                    p.Stages.Add(new WorkStage(id!, s["name"]?.GetValue<string>() ?? id!));
            }
        }
        if (p.Stages.Count == 0)
            p.Stages.AddRange(DefaultStages);
        if (o?["billing_phases"] is JsonArray phases)
        {
            foreach (var ph in phases.OfType<JsonObject>().Select(BillingPhase.FromJson).OfType<BillingPhase>())
            {
                ph.StageIds.RemoveAll(id => p.Stage(id) is null || p.PhaseOf(id) is not null);
                if (p.Phases.All(x => x.Number != ph.Number))
                    p.Phases.Add(ph);
            }
        }
        return p;
    }
}

/// <summary>How estimate revisions and running-bill revisions are written. Revision 0 is the first issue.</summary>
public static class RevisionLabels
{
    public static string Revision(int revision) => $"Rev {Math.Max(0, revision)}";

    /// <summary>The next revision after <paramref name="revision"/>; a negative or missing one counts as the first issue.</summary>
    public static int Next(int revision) => Math.Max(0, revision) + 1;

    /// <summary>An estimate as it is named in lists and reports: <c>Estimate Rev 2</c>, or <c>Estimate Rev 2 · Foundation</c> for one phase.</summary>
    public static string Estimate(int revision, string? phaseName = null) =>
        string.IsNullOrWhiteSpace(phaseName) ? $"Estimate {Revision(revision)}" : $"Estimate {Revision(revision)} · {phaseName.Trim()}";

    /// <summary>A running bill as it is named in lists and reports: its number (kept across revisions) and the revision.</summary>
    public static string RunningBill(string number, int revision) =>
        string.IsNullOrWhiteSpace(number) ? $"Running bill (not numbered) {Revision(revision)}" : $"{number.Trim()} {Revision(revision)}";
}

/// <summary>Amounts grouped by phase, for the estimate and the running bill.</summary>
public static class PhaseTotals
{
    /// <summary>The name shown for amounts that carry no phase.</summary>
    public const string NoPhase = "(no phase)";

    /// <summary>
    /// Sums <paramref name="items"/> by phase. Phases come out in the order of <paramref name="order"/> (the project's
    /// list), then any other name in first-seen order, then <see cref="NoPhase"/> last. A phase with no amount is left out.
    /// </summary>
    public static IReadOnlyList<(string Phase, double Amount)> Group(IEnumerable<(string? Phase, double Amount)> items, IEnumerable<string> order)
    {
        var sums = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var seen = new List<string>();
        foreach (var (phase, amount) in items)
        {
            if (!double.IsFinite(amount)) continue;
            var key = string.IsNullOrWhiteSpace(phase) ? NoPhase : phase.Trim();
            if (!sums.ContainsKey(key)) { sums[key] = 0; seen.Add(key); }
            sums[key] += amount;
        }
        var result = new List<(string, double)>();
        foreach (var name in order.Concat(seen))
            if (!string.Equals(name, NoPhase, StringComparison.OrdinalIgnoreCase) && sums.TryGetValue(name, out var v) && result.All(r => !string.Equals(r.Item1, name, StringComparison.OrdinalIgnoreCase)))
                result.Add((name, v));
        if (sums.TryGetValue(NoPhase, out var rest))
            result.Add((NoPhase, rest));
        return result;
    }
}
