// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Text.Json.Nodes;
using Office.Shared;

namespace BBSApp.Services;

public sealed class EstimateLine
{
    public string Code { get; set; } = "";
    /// <summary>The DSR item number from the rate-book item the line is priced on; empty when it has none.</summary>
    public string DsrItemNo { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Qty { get; set; }
    public double Rate { get; set; }
    public double Amount { get; set; }
    public string Notes { get; set; } = "";
    public string Level { get; set; } = "";
    public string Mark { get; set; } = "";
    public double LengthM { get; set; }
    public double BreadthM { get; set; }
    public double HeightM { get; set; }
    public double AreaM2 { get; set; }
    public double VolumeM3 { get; set; }
    /// <summary>The estimation phase the measured item was put in, in O-Draft; empty when it has none.</summary>
    public string Phase { get; set; } = "";
    /// <summary>The running-bill phase the measured item was put in, in O-Draft; empty when it has none.</summary>
    public string BillPhase { get; set; } = "";
}

public sealed class EstimateResult
{
    public string RateBookVersionId { get; set; } = "";
    public string RateBookVersionName { get; set; } = "";
    public List<EstimateLine> Civil { get; set; } = new();
    public List<EstimateLine> Materials { get; set; } = new();
    public List<EstimateLine> Steel { get; set; } = new();
    public EstimateMarkupBreakdown Markups { get; set; } = new();
    public double BaseTotal => Civil.Sum(l => l.Amount) + Materials.Sum(l => l.Amount) + Steel.Sum(l => l.Amount);
    public double GrandTotal => Markups.GrandTotal > 0 ? Markups.GrandTotal : BaseTotal;
    public List<string> MissingCodes { get; set; } = new();
    /// <summary>Suite ArtifactRef JSON when this estimate is tied to a company-file project; otherwise null.</summary>
    public JsonObject? ArtifactRef { get; set; }
    /// <summary>Revision of this estimate: 0 for the first calculation, then one more each time it is calculated again.</summary>
    public int Revision { get; set; }
    /// <summary>The estimation phase this estimate covers; empty for all phases.</summary>
    public string Phase { get; set; } = "";

    /// <summary>
    /// The civil items' amounts grouped by estimation phase (or by running-bill phase). Materials and steel are
    /// derived from the civil items and the member sheets, so they carry no phase of their own and are not in this split.
    /// </summary>
    public IReadOnlyList<(string Phase, double Amount)> CivilByPhase(bool billing) =>
        PhaseTotals.Group(Civil.Select(l => ((string?)(billing ? l.BillPhase : l.Phase), l.Amount)), ProjectPhases.DefaultStages.Select(s => s.Name));
}

/// <summary>DSR / SOR abstract-of-cost table with measurement columns (L, B, H, area, volume).</summary>
public static class DsrEstimateFormat
{
    public static readonly string[] Headers =
    {
        "Sl. No.", "Item code", "DSR item no.", "Description of item",
        "L (m)", "B (m)", "H (m)", "Area (m²)", "Volume (m³)",
        "Unit", "Quantity", "Rate (₹)", "Amount (₹)", "Remarks"
    };

    public static string DsrUnit(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return unit;
        string u = unit.Trim();
        if (u.Equals("m²", StringComparison.OrdinalIgnoreCase)
            || u.Equals("m2", StringComparison.OrdinalIgnoreCase)
            || u.Equals("sqm", StringComparison.OrdinalIgnoreCase)
            || u.Equals("sq.m", StringComparison.OrdinalIgnoreCase))
            return "Sqm";
        if (u.Equals("m³", StringComparison.OrdinalIgnoreCase)
            || u.Equals("m3", StringComparison.OrdinalIgnoreCase)
            || u.Equals("cum", StringComparison.OrdinalIgnoreCase)
            || u.Equals("cu.m", StringComparison.OrdinalIgnoreCase))
            return "Cum";
        if (u.Equals("m", StringComparison.OrdinalIgnoreCase)
            || u.Equals("rmt", StringComparison.OrdinalIgnoreCase)
            || u.Equals("rm", StringComparison.OrdinalIgnoreCase)
            || u.Equals("r.m", StringComparison.OrdinalIgnoreCase))
            return "Rmt";
        if (u.Equals("nos", StringComparison.OrdinalIgnoreCase)
            || u.Equals("no", StringComparison.OrdinalIgnoreCase)
            || u.Equals("no.", StringComparison.OrdinalIgnoreCase))
            return "Nos";
        if (u.Equals("kg", StringComparison.OrdinalIgnoreCase))
            return "Kg";
        if (u.Contains("bag", StringComparison.OrdinalIgnoreCase))
            return "Bags";
        return u;
    }

    public static string ItemDescription(EstimateLine l)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.Category))
            parts.Add(l.Category.Trim());
        if (!string.IsNullOrWhiteSpace(l.Description))
            parts.Add(l.Description.Trim());
        string body = parts.Count == 0 ? "—" : string.Join(" — ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
        var loc = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.Level)) loc.Add(l.Level.Trim());
        if (!string.IsNullOrWhiteSpace(l.Mark)) loc.Add("Mark " + l.Mark.Trim());
        if (loc.Count > 0) body += " (" + string.Join(", ", loc) + ")";
        return body;
    }

    public static string Remarks(EstimateLine l)
    {
        if (!string.IsNullOrWhiteSpace(l.Notes)) return l.Notes.Trim();
        return "";
    }

    private static string Meas(double v) =>
        v > 0 ? v.ToString("0.###", CultureInfo.InvariantCulture) : "—";

    /// <summary>Build DSR rows with continuous Sl. No. across sections. Returns (rows, nextSlNo).</summary>
    public static (List<IReadOnlyList<string>> Rows, int NextSl) ToRows(
        IEnumerable<EstimateLine> lines, int startSl = 1)
    {
        var rows = new List<IReadOnlyList<string>>();
        int sl = startSl;
        foreach (var l in lines)
        {
            if (l.Qty <= 0 && l.Amount <= 0) continue;
            rows.Add(new[]
            {
                sl.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(l.Code) ? "—" : l.Code.Trim(),
                string.IsNullOrWhiteSpace(l.DsrItemNo) ? "—" : l.DsrItemNo.Trim(),
                ItemDescription(l),
                Meas(l.LengthM),
                Meas(l.BreadthM),
                Meas(l.HeightM),
                Meas(l.AreaM2),
                Meas(l.VolumeM3),
                DsrUnit(l.Unit),
                l.Qty.ToString("0.###", CultureInfo.InvariantCulture),
                l.Rate.ToString("0.00", CultureInfo.InvariantCulture),
                l.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                Remarks(l)
            });
            sl++;
        }
        return (rows, sl);
    }

    public static List<string[]> ToStringRows(IEnumerable<EstimateLine> lines, int startSl = 1)
    {
        var (rows, _) = ToRows(lines, startSl);
        return rows.Select(r => r.ToArray()).ToList();
    }
}

/// <summary>Qty × rate using a RateBook version. Keeps CivilBoqCalculator qty-only.</summary>
public static class EstimateCalculator
{
    public static EstimateResult Build(
        ProjectStore store,
        RateBookVersion version,
        IReadOnlySet<string>? levels = null)
    {
        var index = RateBookStore.Current.IndexByCode(version);
        var result = new EstimateResult
        {
            RateBookVersionId = version.Id,
            RateBookVersionName = version.Name
        };
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var civil = CivilBoqCalculator.BuildAll(store, levels);
        var phaseOf = PhaseIndex(store);
        foreach (var c in civil)
        {
            if (c.Qty <= 0) continue;
            // Count-only companion lines (doors/windows) — priced on m²
            if (c.Unit.Equals("nos", StringComparison.OrdinalIgnoreCase)
                && c.ItemCode.EndsWith("-NOS", StringComparison.OrdinalIgnoreCase))
                continue;
            string code = string.IsNullOrWhiteSpace(c.ItemCode)
                ? InferCivilCode(c)
                : c.ItemCode.Trim();
            double rate;
            string notes;
            if (c.RateOverride > 0)
            {
                // Manual rate carried on the line (e.g. a linked item with a pinned rate).
                rate = c.RateOverride;
                notes = "manual rate";
            }
            else
            {
                (rate, notes) = Lookup(index, code, missing);
            }
            result.Civil.Add(new EstimateLine
            {
                Code = code,
                DsrItemNo = DsrNo(index, code),
                Category = c.Element,
                Description = c.Description,
                Unit = c.Unit,
                Qty = Round3(c.Qty),
                Rate = rate,
                Amount = Round2(c.Qty * rate),
                Notes = string.IsNullOrEmpty(notes) ? c.Notes : notes,
                Level = c.Level,
                Mark = c.Mark,
                LengthM = Round3(c.LengthM),
                BreadthM = Round3(c.BreadthM),
                HeightM = Round3(c.HeightM),
                AreaM2 = Round3(c.AreaM2),
                VolumeM3 = Round3(c.VolumeM3),
                Phase = phaseOf.TryGetValue(PhaseKey(c.Mark, c.Level), out var ph) ? ph.Phase : "",
                BillPhase = ph.BillPhase ?? ""
            });
        }

        // Materials: bags / bricks / sand / agg only (work items already in Civil)
        var concrete = MaterialsCalculator.BuildConcreteBoq(store, levels);
        bool rmc = store.ConcreteFromRmc;
        var po = MaterialsCalculator.MaterialPurchaseOrder(concrete, includeConcreteSplit: !rmc)
            .Concat(CivilBoqCalculator.MaterialPurchaseOrder(civil)
                .Where(p => p.Category is "Units" or "Cement" or "Sand" or "Aggregate"))
            .ToList();
        foreach (var p in po)
        {
            if (p.Qty <= 0) continue;
            string code = InferMaterialCode(p);
            var (rate, notes) = Lookup(index, code, missing);
            result.Materials.Add(new EstimateLine
            {
                Code = code,
                DsrItemNo = DsrNo(index, code),
                Category = p.Category,
                Description = p.Item,
                Unit = p.Unit,
                Qty = Round3(p.Qty),
                Rate = rate,
                Amount = Round2(p.Qty * rate),
                Notes = string.IsNullOrEmpty(notes) ? p.Notes : notes
            });
        }

        // Steel: total kg × STL-KG (simple estimate)
        double steelKg = SteelWeightKg(store, levels);
        if (steelKg > 0)
        {
            const string code = "STL-KG";
            var (rate, notes) = Lookup(index, code, missing);
            result.Steel.Add(new EstimateLine
            {
                Code = code,
                DsrItemNo = DsrNo(index, code),
                Category = "Steel",
                Description = "Reinforcement steel",
                Unit = "kg",
                Qty = Round3(steelKg),
                Rate = rate,
                Amount = Round2(steelKg * rate),
                Notes = notes
            });
        }

        result.MissingCodes = missing.OrderBy(x => x).ToList();
        result.Markups = EstimateMarkupBreakdown.Compute(result.BaseTotal, store.Markups);
        return result;
    }

    static string PhaseKey(string mark, string level) => mark.Trim().ToUpperInvariant() + "|" + level.Trim();

    /// <summary>The phases the member sheets hold (set in O-Draft), by mark and storey.</summary>
    static Dictionary<string, (string Phase, string BillPhase)> PhaseIndex(ProjectStore store)
    {
        var index = new Dictionary<string, (string, string)>();
        foreach (var rows in new[] { store.MasonryWalls, store.Columns, store.Lintels, store.Slabs, store.Flooring })
            foreach (var r in rows)
            {
                string phase = r.TryGetValue("phase", out var p) ? p.Trim() : "";
                string bill = r.TryGetValue("bill_phase", out var b) ? b.Trim() : "";
                if (phase.Length == 0 && bill.Length == 0) continue;
                if (!r.TryGetValue("mark", out var mark) || string.IsNullOrWhiteSpace(mark)) continue;
                index[PhaseKey(mark, MaterialsCalculator.RowLevel(r))] = (phase, bill);
            }
        return index;
    }

    public static JsonObject ToJson(EstimateResult r)
    {
        var civil = new JsonArray();
        foreach (var l in r.Civil) civil.Add(LineToJson(l));
        var mats = new JsonArray();
        foreach (var l in r.Materials) mats.Add(LineToJson(l));
        var steel = new JsonArray();
        foreach (var l in r.Steel) steel.Add(LineToJson(l));
        var missing = new JsonArray();
        foreach (var m in r.MissingCodes) missing.Add(m);
        var json = new JsonObject
        {
            ["rate_book_version_id"] = r.RateBookVersionId,
            ["rate_book_version_name"] = r.RateBookVersionName,
            ["revision"] = r.Revision,
            ["phase"] = r.Phase,
            ["grand_total"] = r.GrandTotal,
            ["base_total"] = r.BaseTotal,
            ["markups"] = r.Markups.ToJson(),
            ["civil"] = civil,
            ["materials"] = mats,
            ["steel"] = steel,
            ["missing_codes"] = missing
        };
        if (r.ArtifactRef is not null)
            json["artifact_ref"] = r.ArtifactRef.DeepClone();
        return json;
    }

    /// <summary>
    /// Stamps a suite ArtifactRef onto the estimate when the open project is linked to the company file.
    /// Call after Calculate so O-Studio can name the same estimate once it is saved to the company file.
    /// </summary>
    public static void AttachCompanyArtifactRef(EstimateResult result, ProjectStore store)
    {
        result.ArtifactRef = null;
        var company = Company.CompanyService.Current;
        var link = store.Shared;
        if (company is null || link is null)
            return;
        var project = ArtifactRefs.ProjectRef(company.Uuid, link.ProjectId);
        var body = ToJson(result);
        var revision = (uint)Math.Max(1, link.Revision + 1);
        result.ArtifactRef = ArtifactRefs.Estimate(project, "last_estimate", revision, ArtifactRefs.EstimateChecksumBody(body));
    }

    public static EstimateResult? FromJson(JsonObject? o)
    {
        if (o is null) return null;
        var r = new EstimateResult
        {
            RateBookVersionId = o["rate_book_version_id"]?.GetValue<string>() ?? "",
            RateBookVersionName = o["rate_book_version_name"]?.GetValue<string>() ?? "",
            Revision = Math.Max(0, o["revision"]?.GetValue<int>() ?? 0),
            Phase = o["phase"]?.GetValue<string>() ?? ""
        };
        LoadLines(o["civil"] as JsonArray, r.Civil);
        LoadLines(o["materials"] as JsonArray, r.Materials);
        LoadLines(o["steel"] as JsonArray, r.Steel);
        r.Markups = EstimateMarkupBreakdown.FromJson(o["markups"] as JsonObject)
            ?? EstimateMarkupBreakdown.Compute(r.BaseTotal, ProjectStore.Current.Markups);
        if (o["missing_codes"] is JsonArray miss)
        {
            foreach (var n in miss)
                if (n is JsonValue jv && jv.TryGetValue<string>(out var s) && s is not null)
                    r.MissingCodes.Add(s);
        }
        if (o["artifact_ref"] is JsonObject artifact)
            r.ArtifactRef = artifact.DeepClone().AsObject();
        return r;
    }

    private static JsonObject LineToJson(EstimateLine l) => new()
    {
        ["code"] = l.Code,
        ["dsr_item_no"] = l.DsrItemNo,
        ["category"] = l.Category,
        ["description"] = l.Description,
        ["unit"] = l.Unit,
        ["qty"] = l.Qty,
        ["rate"] = l.Rate,
        ["amount"] = l.Amount,
        ["notes"] = l.Notes,
        ["level"] = l.Level,
        ["mark"] = l.Mark,
        ["length_m"] = l.LengthM,
        ["breadth_m"] = l.BreadthM,
        ["height_m"] = l.HeightM,
        ["area_m2"] = l.AreaM2,
        ["volume_m3"] = l.VolumeM3,
        ["phase"] = l.Phase,
        ["bill_phase"] = l.BillPhase
    };

    private static void LoadLines(JsonArray? arr, List<EstimateLine> dest)
    {
        if (arr is null) return;
        foreach (var n in arr)
        {
            if (n is not JsonObject o) continue;
            dest.Add(new EstimateLine
            {
                Code = o["code"]?.GetValue<string>() ?? "",
                DsrItemNo = o["dsr_item_no"]?.GetValue<string>() ?? "",
                Category = o["category"]?.GetValue<string>() ?? "",
                Description = o["description"]?.GetValue<string>() ?? "",
                Unit = o["unit"]?.GetValue<string>() ?? "",
                Qty = o["qty"]?.GetValue<double>() ?? 0,
                Rate = o["rate"]?.GetValue<double>() ?? 0,
                Amount = o["amount"]?.GetValue<double>() ?? 0,
                Notes = o["notes"]?.GetValue<string>() ?? "",
                Level = o["level"]?.GetValue<string>() ?? "",
                Mark = o["mark"]?.GetValue<string>() ?? "",
                LengthM = o["length_m"]?.GetValue<double>() ?? 0,
                BreadthM = o["breadth_m"]?.GetValue<double>() ?? 0,
                HeightM = o["height_m"]?.GetValue<double>() ?? 0,
                AreaM2 = o["area_m2"]?.GetValue<double>() ?? 0,
                VolumeM3 = o["volume_m3"]?.GetValue<double>() ?? 0,
                Phase = o["phase"]?.GetValue<string>() ?? "",
                BillPhase = o["bill_phase"]?.GetValue<string>() ?? ""
            });
        }
    }

    private static string DsrNo(IReadOnlyDictionary<string, RateItem> index, string code) =>
        index.TryGetValue(code, out var item) ? item.DsrItemNo ?? "" : "";

    private static (double rate, string notes) Lookup(
        IReadOnlyDictionary<string, RateItem> index, string code, HashSet<string> missing)
    {
        if (index.TryGetValue(code, out var item))
            return (item.Rate, "");
        missing.Add(code);
        return (0, "rate missing");
    }

    public static string InferCivilCode(CivilLine c)
    {
        if (!string.IsNullOrWhiteSpace(c.ItemCode)) return c.ItemCode.Trim();
        return CodeForElement(c.Element ?? "", c.Unit ?? "");
    }

    /// <summary>
    /// Canonical rate code for a civil / finish trade element (unit disambiguates masonry m²/m³).
    /// Shared by the estimate and the linked-item derivation so applied link rows price on the
    /// same codes the estimate would infer — no drift between the two.
    /// </summary>
    public static string CodeForElement(string element, string unit)
    {
        string el = element ?? "";
        unit ??= "";
        if (el.Equals("Masonry", StringComparison.OrdinalIgnoreCase))
            return unit.Equals("m²", StringComparison.OrdinalIgnoreCase) ? "MSN-BRICK-M2" : "MSN-BRICK-M3";
        if (el.Equals("Plaster", StringComparison.OrdinalIgnoreCase)) return "PL-STD";
        if (el.Equals("Painting", StringComparison.OrdinalIgnoreCase)) return "PT-STD";
        if (el.Equals("PCC", StringComparison.OrdinalIgnoreCase)) return "PCC-STD";
        if (el.Equals("Earthwork", StringComparison.OrdinalIgnoreCase)) return "EW-STD";
        if (el.Equals("SSM", StringComparison.OrdinalIgnoreCase)) return "SSM-STD";
        if (el.Equals("Shuttering", StringComparison.OrdinalIgnoreCase)) return "SH-STD";
        if (el.Equals("Flooring", StringComparison.OrdinalIgnoreCase)) return "FL-STD";
        if (el.Equals("Waterproofing", StringComparison.OrdinalIgnoreCase)) return "WP-STD";
        if (el.Equals("DPC", StringComparison.OrdinalIgnoreCase)) return "DPC-STD";
        if (el.Equals("Coping", StringComparison.OrdinalIgnoreCase)) return "CP-STD";
        if (el.Equals("Screed", StringComparison.OrdinalIgnoreCase)) return "SC-STD";
        if (el.Equals("VDF", StringComparison.OrdinalIgnoreCase)) return "VDF-STD";
        if (el.Equals("Skirting", StringComparison.OrdinalIgnoreCase)) return "SK-STD";
        if (el.Equals("Parapet", StringComparison.OrdinalIgnoreCase)) return "PR-STD";
        if (el.Equals("Plinth protection", StringComparison.OrdinalIgnoreCase)
            || el.Equals("PlinthProtection", StringComparison.OrdinalIgnoreCase)) return "PP-STD";
        if (el.Equals("Doors", StringComparison.OrdinalIgnoreCase)
            || el.Equals("Door", StringComparison.OrdinalIgnoreCase)) return "DR-MS";
        if (el.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            || el.Equals("Window", StringComparison.OrdinalIgnoreCase)) return "WN-AL-2.5T";
        return $"UNK-{el}";
    }

    private static string InferMaterialCode(PoLine p)
    {
        string item = p.Item ?? "";
        if (item.Contains("Brick", StringComparison.OrdinalIgnoreCase) && p.Unit.Contains("nos", StringComparison.OrdinalIgnoreCase))
            return "MAT-BRICK";
        if (item.Contains("ACC", StringComparison.OrdinalIgnoreCase)) return "MAT-ACC";
        if (item.Contains("Cement block", StringComparison.OrdinalIgnoreCase)) return "MAT-CEMBLK";
        if (item.Contains("Cement", StringComparison.OrdinalIgnoreCase) || item.Contains("OPC", StringComparison.OrdinalIgnoreCase))
            return "MAT-CEMENT";
        if (item.Contains("Sand", StringComparison.OrdinalIgnoreCase) || item.Contains("Fine", StringComparison.OrdinalIgnoreCase))
            return "MAT-SAND";
        if (item.Contains("Aggregate", StringComparison.OrdinalIgnoreCase) || item.Contains("Coarse", StringComparison.OrdinalIgnoreCase))
            return "MAT-AGG";
        // Work items already priced in civil — skip duplicates by using work codes
        if (item.Contains("Plaster", StringComparison.OrdinalIgnoreCase)) return "PL-STD";
        if (item.Contains("Painting", StringComparison.OrdinalIgnoreCase)) return "PT-STD";
        if (item.Contains("PCC", StringComparison.OrdinalIgnoreCase)) return "PCC-STD";
        if (item.Contains("Earthwork", StringComparison.OrdinalIgnoreCase)) return "EW-STD";
        if (item.Contains("Size stone", StringComparison.OrdinalIgnoreCase)) return "SSM-STD";
        if (item.Contains("Shuttering", StringComparison.OrdinalIgnoreCase) || item.Contains("Formwork", StringComparison.OrdinalIgnoreCase))
            return "SH-STD";
        if (item.Contains("Flooring", StringComparison.OrdinalIgnoreCase)) return "FL-STD";
        if (item.Contains("230", StringComparison.OrdinalIgnoreCase)) return "MSN-BRICK-M3";
        if (item.Contains("110", StringComparison.OrdinalIgnoreCase)) return "MSN-BRICK-M2";
        return "MAT-" + item.Replace(' ', '_');
    }

    private static double SteelWeightKg(ProjectStore store, IReadOnlySet<string>? levels)
    {
        double total = 0;
        void absorb(string kind, IEnumerable<Dictionary<string, string>> rows)
        {
            var filtered = MaterialsCalculator.FilterByLevels(rows, levels).ToList();
            if (filtered.Count == 0) return;
            var res = EngineClient.Generate(kind, store.SettingsJson(), filtered);
            if (!res.Ok) return;
            foreach (var r in res.Summary.Rows)
            {
                if (r.Count < 4 || r[0].Equals("TOTAL", StringComparison.OrdinalIgnoreCase)) continue;
                if (double.TryParse(r[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var wt))
                    total += wt;
            }
        }
        absorb("columns", store.Columns);
        absorb("beams", store.Beams);
        absorb("slabs", store.Slabs);
        absorb("footings", store.Footings);
        absorb("walls", store.Walls);
        absorb("stairs", store.Stairs);
        absorb("pedestals", store.Pedestals);
        absorb("lintels", store.Lintels);
        return total;
    }

    private static double Round3(double v) => Math.Round(v, 3);
    private static double Round2(double v) => Math.Round(v, 2);
}
