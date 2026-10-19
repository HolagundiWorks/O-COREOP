// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BBSApp.Services;

/// <summary>A schedule-of-rates item (unit rate library).</summary>
public sealed class SorItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Rate { get; set; }

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["code"] = Code,
        ["description"] = Description,
        ["unit"] = Unit,
        ["rate"] = Rate
    };

    public static SorItem FromJson(JsonObject o) => new()
    {
        Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
        Code = o["code"]?.GetValue<string>() ?? "",
        Description = o["description"]?.GetValue<string>() ?? "",
        Unit = o["unit"]?.GetValue<string>() ?? "",
        Rate = o["rate"]?.GetValue<double>() ?? 0
    };
}

/// <summary>A reusable contract clause.</summary>
public sealed class StandardTerm
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";

    public JsonObject ToJson() => new() { ["id"] = Id, ["title"] = Title, ["text"] = Text };
    public static StandardTerm FromJson(JsonObject o) => new()
    {
        Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
        Title = o["title"]?.GetValue<string>() ?? "",
        Text = o["text"]?.GetValue<string>() ?? ""
    };
}

/// <summary>One priced line on an item-rate contract / work order.</summary>
public sealed class ContractLine
{
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Qty { get; set; }
    public double Rate { get; set; }
    public double Amount => Qty * Rate;

    public JsonObject ToJson() => new()
    {
        ["description"] = Description,
        ["unit"] = Unit,
        ["qty"] = Qty,
        ["rate"] = Rate
    };

    public static ContractLine FromJson(JsonObject o) => new()
    {
        Description = o["description"]?.GetValue<string>() ?? "",
        Unit = o["unit"]?.GetValue<string>() ?? "",
        Qty = o["qty"]?.GetValue<double>() ?? 0,
        Rate = o["rate"]?.GetValue<double>() ?? 0
    };
}

public enum ContractKind { ItemRateWorkOrder, LumpSumWorkOrder, Tender }

/// <summary>A contract / work order / tender — draft, edit, finalize.</summary>
public sealed class Contract
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    /// <summary>Persona that issues the order (normally the PM to the contractor).</summary>
    public PartyRole IssuedByRole { get; set; } = PartyRole.PM;
    public ContractKind Kind { get; set; } = ContractKind.ItemRateWorkOrder;
    public string Title { get; set; } = "";
    public string ContractorName { get; set; } = "";
    public string ContractorAddress { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTime AwardDate { get; set; } = DateTime.Today;
    public DateTime CompletionDate { get; set; } = DateTime.Today.AddDays(30);
    /// <summary>Used for lump-sum; item-rate totals from lines.</summary>
    public double LumpSumValue { get; set; }
    public double RetentionPct { get; set; } = 5;
    public List<ContractLine> Lines { get; } = new();
    /// <summary>Selected/added clause texts, in order.</summary>
    public List<string> Terms { get; } = new();
    public bool Finalized { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public bool IsItemRate => Kind == ContractKind.ItemRateWorkOrder || Kind == ContractKind.Tender;
    public double LinesTotal => Lines.Sum(l => l.Amount);
    public double Value => IsItemRate ? LinesTotal : LumpSumValue;

    public string KindCode => Kind == ContractKind.Tender ? "TEN" : "WO";
    public static string KindDisplay(ContractKind k) => k switch
    {
        ContractKind.ItemRateWorkOrder => "Item-rate work order",
        ContractKind.LumpSumWorkOrder => "Lump-sum work order",
        ContractKind.Tender => "Tender",
        _ => "Work order"
    };

    public JsonObject ToJson()
    {
        var lines = new JsonArray();
        foreach (var l in Lines) lines.Add(l.ToJson());
        var terms = new JsonArray();
        foreach (var t in Terms) terms.Add(t);
        return new JsonObject
        {
            ["id"] = Id,
            ["number"] = Number,
            ["issued_by"] = IssuedByRole.ToToken(),
            ["kind"] = (int)Kind,
            ["title"] = Title,
            ["contractor_name"] = ContractorName,
            ["contractor_address"] = ContractorAddress,
            ["scope"] = Scope,
            ["award_date"] = AwardDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["completion_date"] = CompletionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["lump_sum_value"] = LumpSumValue,
            ["retention_pct"] = RetentionPct,
            ["lines"] = lines,
            ["terms"] = terms,
            ["finalized"] = Finalized ? 1 : 0,
            ["created_utc"] = CreatedUtc.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    public static Contract FromJson(JsonObject o)
    {
        var c = new Contract
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Number = o["number"]?.GetValue<string>() ?? "",
            IssuedByRole = PartyRoleX.Parse(o["issued_by"]?.GetValue<string>()),
            Kind = (ContractKind)(o["kind"]?.GetValue<int>() ?? 0),
            Title = o["title"]?.GetValue<string>() ?? "",
            ContractorName = o["contractor_name"]?.GetValue<string>() ?? "",
            ContractorAddress = o["contractor_address"]?.GetValue<string>() ?? "",
            Scope = o["scope"]?.GetValue<string>() ?? "",
            LumpSumValue = o["lump_sum_value"]?.GetValue<double>() ?? 0,
            RetentionPct = o["retention_pct"]?.GetValue<double>() ?? 5,
            Finalized = (o["finalized"]?.GetValue<int>() ?? 0) != 0
        };
        if (DateTime.TryParse(o["award_date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ad)) c.AwardDate = ad;
        if (DateTime.TryParse(o["completion_date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var cd)) c.CompletionDate = cd;
        if (DateTime.TryParse(o["created_utc"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cu)) c.CreatedUtc = cu;
        if (o["lines"] is JsonArray la) foreach (var it in la) if (it is JsonObject lo) c.Lines.Add(ContractLine.FromJson(lo));
        if (o["terms"] is JsonArray ta) foreach (var it in ta) { var s = it?.GetValue<string>(); if (!string.IsNullOrEmpty(s)) c.Terms.Add(s); }
        return c;
    }
}

/// <summary>Project contracts + schedule of rates + terms library, with auto-numbering.</summary>
public sealed class ContractRegister
{
    public string Prefix { get; set; } = "";
    public ObservableCollection<Contract> Contracts { get; } = new();
    public ObservableCollection<SorItem> Rates { get; } = new();
    public ObservableCollection<StandardTerm> Terms { get; } = new();
    private readonly Dictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);

    public string EffectivePrefix(string companyName)
    {
        if (!string.IsNullOrWhiteSpace(Prefix)) return Prefix.Trim();
        var initials = new string((companyName ?? "")
            .Split(new[] { ' ', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0])).ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "OC" : initials;
    }

    private static string PartyPrefix(Contract c, string companyName) =>
        ProjectStore.Current.Parties.For(c.IssuedByRole).EffectivePrefix(companyName);

    private static string CounterKey(Contract c, string fy) =>
        $"{c.IssuedByRole.ToToken()}|{c.KindCode}|{fy}";

    public string PreviewNumber(Contract c, string companyName)
    {
        string fy = OfficeRegister.FinancialYear(c.AwardDate);
        int next = (_counters.TryGetValue(CounterKey(c, fy), out var last) ? last : 0) + 1;
        return $"{PartyPrefix(c, companyName)}/{c.KindCode}/{fy}/{next:000}";
    }

    public void Finalize(Contract c, string companyName)
    {
        if (c.Finalized && !string.IsNullOrWhiteSpace(c.Number)) return;
        string fy = OfficeRegister.FinancialYear(c.AwardDate);
        string key = CounterKey(c, fy);
        int next = (_counters.TryGetValue(key, out var last) ? last : 0) + 1;
        _counters[key] = next;
        c.Number = $"{PartyPrefix(c, companyName)}/{c.KindCode}/{fy}/{next:000}";
        c.Finalized = true;
    }

    public void EnsureSeeded()
    {
        if (Rates.Count == 0)
        {
            void R(string code, string desc, string unit, double rate) =>
                Rates.Add(new SorItem { Code = code, Description = desc, Unit = unit, Rate = rate });
            R("EW-01", "Earthwork excavation in ordinary soil incl. lift & lead", "cum", 250);
            R("PCC-01", "PCC 1:4:8 in foundation", "cum", 4800);
            R("RCC-01", "RCC M25 incl. formwork & finishing (excl. steel)", "cum", 7200);
            R("STL-01", "Reinforcement steel — cut, bend, place", "kg", 72);
            R("BW-01", "Brickwork 230 mm in CM 1:6", "cum", 6500);
            R("PL-01", "Cement plaster 12 mm CM 1:4", "sqm", 240);
            R("FL-01", "Vitrified tile flooring 600×600 incl. bedding", "sqm", 950);
            R("PT-01", "Emulsion paint 2 coats over primer", "sqm", 110);
        }
        if (Terms.Count == 0)
        {
            void T(string title, string text) => Terms.Add(new StandardTerm { Title = title, Text = text });
            T("Scope & specifications", "The Contractor shall execute the work strictly as per the drawings, "
                + "specifications and relevant IS codes, and to the satisfaction of the Engineer-in-charge.");
            T("Time & completion", "Time is the essence of the contract. The work shall be completed within the "
                + "stipulated period from the date of this order, failing which liquidated damages shall apply.");
            T("Payment terms", "Running account bills shall be paid within 15 days of certification. Final payment "
                + "shall be released after rectification of defects and submission of all statutory documents.");
            T("Retention & security", "Retention shall be deducted from each bill and released after the defect "
                + "liability period on satisfactory performance.");
            T("Quality & materials", "All materials shall conform to relevant IS standards and be approved before use. "
                + "Rejected materials shall be removed from site at the Contractor's cost.");
            T("Safety & statutory", "The Contractor shall comply with all safety, labour and statutory requirements and "
                + "indemnify the Employer against any claims arising therefrom.");
            T("Variations", "Extra or varied items shall be executed only on written instruction and paid at agreed "
                + "rates or, failing agreement, at rates derived from the schedule of rates.");
            T("Dispute resolution", "Disputes shall be settled amicably; failing which, they shall be referred to "
                + "arbitration under the Arbitration and Conciliation Act, 1996. Jurisdiction: Hospet.");
        }
    }

    public void Clear()
    {
        Contracts.Clear();
        Rates.Clear();
        Terms.Clear();
        _counters.Clear();
        Prefix = "";
    }

    public JsonObject ToJson()
    {
        var contracts = new JsonArray();
        foreach (var c in Contracts) contracts.Add(c.ToJson());
        var rates = new JsonArray();
        foreach (var r in Rates) rates.Add(r.ToJson());
        var terms = new JsonArray();
        foreach (var t in Terms) terms.Add(t.ToJson());
        var counters = new JsonObject();
        foreach (var kv in _counters) counters[kv.Key] = kv.Value;
        return new JsonObject
        {
            ["prefix"] = Prefix ?? "",
            ["counters"] = counters,
            ["contracts"] = contracts,
            ["rates"] = rates,
            ["terms"] = terms
        };
    }

    public void LoadFrom(JsonObject? o)
    {
        Clear();
        if (o is null) { EnsureSeeded(); return; }
        Prefix = o["prefix"]?.GetValue<string>() ?? "";
        if (o["counters"] is JsonObject c)
            foreach (var kv in c)
                if (kv.Value is JsonValue v && v.TryGetValue<int>(out var n))
                    _counters[OfficeRegister.MigrateCounterKey(kv.Key)] = n;
        if (o["contracts"] is JsonArray ca) foreach (var it in ca) if (it is JsonObject co) Contracts.Add(Contract.FromJson(co));
        if (o["rates"] is JsonArray ra) foreach (var it in ra) if (it is JsonObject ro) Rates.Add(SorItem.FromJson(ro));
        if (o["terms"] is JsonArray ta) foreach (var it in ta) if (it is JsonObject to) Terms.Add(StandardTerm.FromJson(to));
        EnsureSeeded();
    }
}
