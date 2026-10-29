// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;

namespace BBSApp.Services;

/// <summary>One computed linked-item line produced by a <see cref="LinkRule"/>.</summary>
public sealed class DerivedItem
{
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string SourceTrade { get; set; } = "";
    public string SourceTradeKey { get; set; } = "";
    public string SourceMark { get; set; } = "";
    public string Level { get; set; } = "";
    public LinkBasis Basis { get; set; }
    public double SourceQty { get; set; }
    public double Factor { get; set; }
    public string TargetTrade { get; set; } = "";
    public string TargetTradeKey { get; set; } = "";
    public double TargetQty { get; set; }
    public string TargetUnit { get; set; } = "";
    /// <summary>True when this line's source was itself produced by an upstream rule (a chained link).</summary>
    public bool Chained { get; set; }

    /// <summary>Rate-code override carried from the rule (empty = use the canonical trade code).</summary>
    public string RateCodeOverride { get; set; } = "";
    /// <summary>Manual unit rate carried from the rule (0 = price via the rate book).</summary>
    public double RateOverride { get; set; }

    // ── Pricing (filled by DerivationEngine.Price; zero until then) ──
    /// <summary>Rate code this derived quantity prices on (see EstimateCalculator.CodeForElement).</summary>
    public string RateCode { get; set; } = "";
    /// <summary>Unit rate from the priced rate book; 0 when the code has no rate.</summary>
    public double Rate { get; set; }
    /// <summary>TargetQty × Rate.</summary>
    public double Amount { get; set; }
    /// <summary>True when the rate book has no rate for <see cref="RateCode"/>.</summary>
    public bool RateMissing { get; set; }
}

/// <summary>
/// Data-driven derivation of linked estimation quantities from a <see cref="LinkRuleBook"/>.
/// Reads source-trade quantities from <see cref="CivilBoqCalculator.BuildAll"/> (already unit-normalised),
/// orders rules so producers run before consumers, and lets outputs feed downstream rules
/// (masonry → plaster → paint). Read-only: it never mutates the take-off sheets.
/// </summary>
public static class DerivationEngine
{
    /// <summary>Accumulated measures for a single (trade, mark, level) node.</summary>
    private sealed class NodeQty
    {
        public string Mark = "";
        public string Level = "";
        public double AreaM2;
        public double VolumeM3;
        public double LengthM;
        public double BreadthM;
        public double Count;
    }

    public static IReadOnlyList<DerivedItem> Preview(
        ProjectStore store, LinkRuleBook book, IReadOnlySet<string>? levels = null)
    {
        var work = new Dictionary<string, List<NodeQty>>(StringComparer.OrdinalIgnoreCase);

        // 1. Seed the graph from the measured civil BOQ (source of truth, SI units).
        //    Masonry is handled separately below: a 230 mm wall is measured by volume,
        //    so its BOQ line carries no face area — but Area-basis links (→ plaster,
        //    paint) need the wall face area.
        foreach (var c in CivilBoqCalculator.BuildAll(store, levels))
        {
            if (string.IsNullOrWhiteSpace(c.Element)) continue;
            if (c.Linked) continue; // don't re-derive from already-applied link rows
            if (c.Element.Equals("Masonry", StringComparison.OrdinalIgnoreCase)) continue;
            var node = new NodeQty
            {
                Mark = string.IsNullOrWhiteSpace(c.Mark) ? "—" : c.Mark,
                Level = c.Level ?? "",
                AreaM2 = c.AreaM2,
                VolumeM3 = c.VolumeM3,
                LengthM = c.LengthM,
                BreadthM = c.BreadthM,
                Count = c.Unit.Contains("nos", StringComparison.OrdinalIgnoreCase) ? c.Qty : 1
            };
            AddNode(work, c.Element, node);
        }

        // RCC members are measured on their own pages. A rule can read them (never write them): concrete volume,
        // formwork contact area and run length per member, so plaster, waterproofing or a PCC bed can follow them.
        SeedRcc(work, store, levels);

        // Masonry nodes carry the net area of ONE face (opening-deducted) and the wall
        // run length — so Area-basis rules give both faces at factor 2, and Length-basis
        // rules (→ DPC) read the run. Same geometry as the finishes reconcile flow.
        foreach (var w in store.MasonryWalls)
        {
            double L = Mm(w, "length"), H = Mm(w, "height");
            if (L <= 0 || H <= 0) continue;
            string level = MaterialsCalculator.RowLevel(w);
            if (levels is not null && level.Length > 0 && !levels.Contains(level)) continue;
            string rule = Row(w, "deduct_rule", "IS1200 masonry");
            var (_, _, netMm2, _) = CivilBoqCalculator.DeductFaceArea(L, H, w, rule, false);
            AddNode(work, "Masonry", new NodeQty
            {
                Mark = Row(w, "mark", "MW"),
                Level = level,
                AreaM2 = netMm2 / 1e6,
                LengthM = L / 1000.0,
                Count = 1
            });
        }

        // 2. Run rules in producer-before-consumer order so chains resolve.
        var rules = book.Rules
            .Where(r => r.Enabled
                        && !string.IsNullOrWhiteSpace(r.SourceTrade)
                        && !string.IsNullOrWhiteSpace(r.TargetTrade))
            .ToList();
        var ordered = TopoOrder(rules);

        var seeded = new HashSet<string>(work.Keys, StringComparer.OrdinalIgnoreCase);
        var results = new List<DerivedItem>();

        foreach (var rule in ordered)
        {
            if (!work.TryGetValue(rule.SourceTrade, out var sources) || sources.Count == 0)
                continue;
            bool chained = !seeded.Contains(rule.SourceTrade);
            var produced = new List<NodeQty>();

            if (rule.PerItem)
            {
                foreach (var src in sources)
                {
                    double drive = Drive(src, rule.Basis);
                    if (drive <= 0) continue;
                    double tq = drive * rule.Factor;
                    results.Add(Make(rule, src.Mark, src.Level, drive, tq, chained));
                    produced.Add(TargetNode(rule, src.Mark, src.Level, tq));
                }
            }
            else
            {
                double drive = sources.Sum(s => Drive(s, rule.Basis));
                if (drive <= 0) continue;
                double tq = drive * rule.Factor;
                results.Add(Make(rule, "ALL", "", drive, tq, chained));
                produced.Add(TargetNode(rule, "ALL", "", tq));
            }

            foreach (var p in produced)
                AddNode(work, rule.TargetTrade, p);
        }

        return results;
    }

    /// <summary>
    /// Price a derivation preview against a rate-book version: fills <see cref="DerivedItem.RateCode"/>,
    /// <see cref="DerivedItem.Rate"/> and <see cref="DerivedItem.Amount"/> on each item and returns the
    /// priced grand total plus any codes the rate book has no rate for. Uses the same canonical codes as
    /// the estimate (<see cref="EstimateCalculator.CodeForElement"/>).
    /// </summary>
    public static (double Total, IReadOnlyList<string> MissingCodes) Price(
        IReadOnlyList<DerivedItem> items, RateBookVersion? version)
    {
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = version is null ? null : RateBookStore.Current.IndexByCode(version);
        double total = 0;
        foreach (var it in items)
        {
            it.RateCode = CodeFor(it);
            if (it.RateOverride > 0)
            {
                // Manual rate wins over any rate-book lookup — prices even with no rate book.
                it.Rate = it.RateOverride;
                it.RateMissing = false;
            }
            else if (index is not null && index.TryGetValue(it.RateCode, out var ri))
            {
                it.Rate = ri.Rate;
                it.RateMissing = false;
            }
            else
            {
                it.Rate = 0;
                it.RateMissing = true;
                if (index is not null) missing.Add(it.RateCode);
            }
            it.Amount = Math.Round(it.TargetQty * it.Rate, 2);
            total += it.Amount;
        }
        return (Math.Round(total, 2), missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Roll a derivation preview up to one total per target trade.</summary>
    public static IReadOnlyList<(string Trade, string Unit, double Qty, int Lines)> Totals(
        IReadOnlyList<DerivedItem> items)
    {
        return items
            .GroupBy(i => i.TargetTrade, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                Trade: LinkTradeRegistry.Display(g.Key),
                Unit: g.First().TargetUnit,
                Qty: g.Sum(i => i.TargetQty),
                Lines: g.Count()))
            .OrderBy(t => t.Trade, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Materialise the enabled rules' derived quantities into the target take-off sheets as
    /// tagged <c>link_applied</c> rows, so they flow into the BOQ and estimate. Idempotent:
    /// clears prior applied rows first. Returns the number of rows written.
    /// </summary>
    public static int Apply(ProjectStore store, LinkRuleBook book, IReadOnlySet<string>? levels = null)
    {
        var items = Preview(store, book, levels);
        ClearApplied(store);

        int written = 0;
        foreach (var it in items)
        {
            var sheet = store.SheetForTrade(it.TargetTradeKey);
            if (sheet is null || it.TargetQty <= 0) continue;
            sheet.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mark"] = $"LK-{it.SourceMark}",
                ["level"] = it.Level,
                ["link_applied"] = "1",
                ["link_trade"] = it.TargetTradeKey,
                ["item_code"] = CodeFor(it),
                ["rate"] = it.RateOverride > 0 ? Inv(it.RateOverride) : "",
                ["derived_by"] = it.RuleId,
                ["source"] = "auto_link",
                ["source_mark"] = it.SourceMark,
                ["qty"] = Inv(it.TargetQty),
                ["unit"] = it.TargetUnit,
                ["status"] = "linked",
                ["notes"] = $"Linked · {it.RuleName}"
            });
            written++;
        }
        if (written > 0) store.Notify();
        return written;
    }

    /// <summary>Remove all previously applied link rows from every target sheet. Returns rows removed.</summary>
    public static int ClearApplied(ProjectStore store)
    {
        int removed = 0;
        foreach (var t in LinkTradeRegistry.All)
        {
            var sheet = store.SheetForTrade(t.Key);
            if (sheet is null) continue;
            for (int i = sheet.Count - 1; i >= 0; i--)
                if (sheet[i].TryGetValue("link_applied", out var v) && v == "1")
                {
                    sheet.RemoveAt(i);
                    removed++;
                }
        }
        if (removed > 0) store.Notify();
        return removed;
    }

    private static readonly Dictionary<string, string> RccKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Column"] = "rcc.columns", ["Beam"] = "rcc.beams", ["Pedestal"] = "rcc.pedestals", ["Lintel"] = "rcc.lintels",
        ["Slab"] = "rcc.slabs", ["Footing"] = "rcc.footings", ["Wall"] = "rcc.walls", ["Stairs"] = "rcc.stairs", ["Stair"] = "rcc.stairs"
    };

    /// <summary>
    /// Adds one node per RCC member (by mark and level): <c>VolumeM3</c> from the concrete BOQ, <c>AreaM2</c> the formwork contact area,
    /// <c>LengthM</c> the member's run (beam span, column height, slab span, footing length, wall length).
    /// </summary>
    private static void SeedRcc(Dictionary<string, List<NodeQty>> work, ProjectStore store, IReadOnlySet<string>? levels)
    {
        var nodes = new Dictionary<(string Key, string Mark, string Level), NodeQty>();
        NodeQty Get(string key, string mark, string level)
        {
            var id = (key, string.IsNullOrWhiteSpace(mark) ? "—" : mark, level ?? "");
            if (!nodes.TryGetValue(id, out var n))
                nodes[id] = n = new NodeQty { Mark = id.Item2, Level = id.Item3, Count = 1 };
            return n;
        }

        foreach (var c in MaterialsCalculator.BuildConcreteBoq(store, levels))
            if (RccKeys.TryGetValue(c.Element, out var k)) Get(k, c.Mark, c.Level).VolumeM3 += c.VolumeM3;

        foreach (var p in ShutteringCalculator.FromRcc(store, levels))
        {
            if (!RccKeys.TryGetValue(p.MemberType, out var k)) continue;
            var n = Get(k, p.Mark, p.Level);
            n.AreaM2 += p.AreaM2;
            double run = Math.Max(Mm(p.DimSnapshot, "length"), Mm(p.DimSnapshot, "height")) / 1000.0;
            if (run > n.LengthM) n.LengthM = run;
            if (n.BreadthM <= 0) n.BreadthM = Mm(p.DimSnapshot, "breadth") / 1000.0;
        }

        foreach (var ((key, _, _), node) in nodes)
            AddNode(work, key, node);
    }

    private static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static DerivedItem Make(
        LinkRule rule, string mark, string level, double drive, double tq, bool chained) => new()
    {
        RuleId = rule.Id,
        RuleName = rule.Name,
        SourceTrade = LinkTradeRegistry.Display(rule.SourceTrade),
        SourceTradeKey = rule.SourceTrade,
        SourceMark = mark,
        Level = level,
        Basis = rule.Basis,
        SourceQty = Math.Round(drive, 3),
        Factor = rule.Factor,
        TargetTrade = LinkTradeRegistry.Display(rule.TargetTrade),
        TargetTradeKey = rule.TargetTrade,
        TargetQty = Math.Round(tq, 3),
        TargetUnit = string.IsNullOrWhiteSpace(rule.TargetUnit)
            ? LinkTradeRegistry.Unit(rule.TargetTrade)
            : rule.TargetUnit,
        Chained = chained,
        RateCodeOverride = rule.RateCodeOverride?.Trim() ?? "",
        RateOverride = rule.RateOverride > 0 ? rule.RateOverride : 0
    };

    /// <summary>The rate code a derived line prices on: the rule's override when set, else the
    /// target trade's canonical code (shared with the estimate via EstimateCalculator).</summary>
    private static string CodeFor(DerivedItem it) =>
        string.IsNullOrWhiteSpace(it.RateCodeOverride)
            ? EstimateCalculator.CodeForElement(it.TargetTradeKey, it.TargetUnit)
            : it.RateCodeOverride.Trim();

    /// <summary>Store a produced quantity under the target trade so downstream rules can read it.</summary>
    private static NodeQty TargetNode(LinkRule rule, string mark, string level, double tq)
    {
        var node = new NodeQty { Mark = mark, Level = level, Count = 1 };
        string unit = LinkTradeRegistry.Unit(rule.TargetTrade);
        if (unit.Contains("m³", StringComparison.OrdinalIgnoreCase) || unit.Contains("m3", StringComparison.OrdinalIgnoreCase))
            node.VolumeM3 = tq;
        else if (unit == "m")
            node.LengthM = tq;
        else if (unit.Contains("nos", StringComparison.OrdinalIgnoreCase))
            node.Count = tq;
        else
            node.AreaM2 = tq; // m² / default
        return node;
    }

    private static void AddNode(Dictionary<string, List<NodeQty>> work, string trade, NodeQty node)
    {
        if (!work.TryGetValue(trade, out var list))
        {
            list = new List<NodeQty>();
            work[trade] = list;
        }
        list.Add(node);
    }

    private static double Mm(Dictionary<string, string> r, string key) =>
        r.TryGetValue(key, out var v)
        && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static string Row(Dictionary<string, string> r, string key, string def) =>
        r.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : def;

    private static double Drive(NodeQty n, LinkBasis basis) => basis switch
    {
        LinkBasis.Area => n.AreaM2,
        LinkBasis.Volume => n.VolumeM3,
        LinkBasis.Length => n.LengthM,
        LinkBasis.Perimeter => n.LengthM > 0 && n.BreadthM > 0 ? 2 * (n.LengthM + n.BreadthM) : 0,
        LinkBasis.Count => n.Count > 0 ? n.Count : 1,
        _ => 0
    };

    /// <summary>Kahn topological sort of trades on source→target edges; rules ordered by source rank.</summary>
    private static List<LinkRule> TopoOrder(List<LinkRule> rules)
    {
        var trades = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules) { trades.Add(r.SourceTrade); trades.Add(r.TargetTrade); }

        var indeg = trades.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var adj = trades.ToDictionary(t => t, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules)
        {
            if (r.SourceTrade.Equals(r.TargetTrade, StringComparison.OrdinalIgnoreCase)) continue; // self-loop
            adj[r.SourceTrade].Add(r.TargetTrade);
            indeg[r.TargetTrade]++;
        }

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        int order = 0;
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            rank[t] = order++;
            foreach (var next in adj[t])
                if (--indeg[next] == 0) queue.Enqueue(next);
        }
        // Any trade left in a cycle ranks last (stable, arbitrary among themselves).
        foreach (var t in trades)
            if (!rank.ContainsKey(t)) rank[t] = order++;

        return rules
            .OrderBy(r => rank.TryGetValue(r.SourceTrade, out var k) ? k : int.MaxValue)
            .ToList();
    }
}
