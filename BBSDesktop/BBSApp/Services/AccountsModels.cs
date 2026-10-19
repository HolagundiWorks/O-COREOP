// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BBSApp.Services;

public enum CashKind { Receipt, Payment }
public enum CashAccount { Cash, Bank }

/// <summary>A cash/bank receipt or payment.</summary>
public sealed class CashTxn
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Today;
    /// <summary>Persona whose cashbook this entry belongs to.</summary>
    public PartyRole IssuedByRole { get; set; } = PartyRole.PM;
    public CashKind Kind { get; set; } = CashKind.Payment;
    public CashAccount Account { get; set; } = CashAccount.Bank;
    public string Party { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public double Amount { get; set; }
    public string Reference { get; set; } = "";

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["date"] = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["issued_by"] = IssuedByRole.ToToken(),
        ["kind"] = (int)Kind,
        ["account"] = (int)Account,
        ["party"] = Party,
        ["category"] = Category,
        ["description"] = Description,
        ["amount"] = Amount,
        ["reference"] = Reference
    };

    public static CashTxn FromJson(JsonObject o)
    {
        var t = new CashTxn
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            IssuedByRole = PartyRoleX.Parse(o["issued_by"]?.GetValue<string>()),
            Kind = (CashKind)(o["kind"]?.GetValue<int>() ?? 1),
            Account = (CashAccount)(o["account"]?.GetValue<int>() ?? 1),
            Party = o["party"]?.GetValue<string>() ?? "",
            Category = o["category"]?.GetValue<string>() ?? "",
            Description = o["description"]?.GetValue<string>() ?? "",
            Amount = o["amount"]?.GetValue<double>() ?? 0,
            Reference = o["reference"]?.GetValue<string>() ?? ""
        };
        if (DateTime.TryParse(o["date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) t.Date = d;
        return t;
    }
}

/// <summary>One measured line on a running bill.</summary>
public sealed class BillLine
{
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Rate { get; set; }
    public double Qty { get; set; }
    public double Amount => Rate * Qty;

    public JsonObject ToJson() => new()
    {
        ["description"] = Description,
        ["unit"] = Unit,
        ["rate"] = Rate,
        ["qty"] = Qty
    };

    public static BillLine FromJson(JsonObject o) => new()
    {
        Description = o["description"]?.GetValue<string>() ?? "",
        Unit = o["unit"]?.GetValue<string>() ?? "",
        Rate = o["rate"]?.GetValue<double>() ?? 0,
        Qty = o["qty"]?.GetValue<double>() ?? 0
    };
}

/// <summary>A running-account (RA) bill against a contract, with retention and deductions.</summary>
public sealed class RunningBill
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    public int BillNo { get; set; }
    /// <summary>Persona that raises this RA bill (normally the contractor).</summary>
    public PartyRole IssuedByRole { get; set; } = PartyRole.Contractor;
    public DateTime Date { get; set; } = DateTime.Today;
    public string ContractId { get; set; } = "";
    public string ContractLabel { get; set; } = "";
    public string Party { get; set; } = "";
    public List<BillLine> Lines { get; } = new();
    public double RetentionPct { get; set; } = 5;
    public double OtherDeductions { get; set; }
    public double AdvanceRecovery { get; set; }
    // Statutory (India). Defaults 0 so legacy bills are unchanged; new bills seed sensible values in the UI.
    /// <summary>Output GST added on the taxable value (CGST+SGST or IGST), e.g. 18.</summary>
    public double GstPct { get; set; }
    /// <summary>Income-tax TDS u/s 194C on gross (typically 1% individual / 2% company).</summary>
    public double TdsPct { get; set; }
    /// <summary>Building &amp; other construction workers' welfare cess on gross (typically 1%).</summary>
    public double CessPct { get; set; }
    /// <summary>GST-TDS on taxable value (2% where the deductor is required to deduct).</summary>
    public double GstTdsPct { get; set; }
    public bool Certified { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public double Gross => Lines.Sum(l => l.Amount);
    public double Gst => Gross * GstPct / 100.0;
    /// <summary>Taxable value + GST — the contractor's tax-invoice total before deductions.</summary>
    public double Invoice => Gross + Gst;
    public double Retention => Gross * RetentionPct / 100.0;
    public double Tds => Gross * TdsPct / 100.0;
    public double Cess => Gross * CessPct / 100.0;
    public double GstTds => Gross * GstTdsPct / 100.0;
    public double StatutoryDeductions => Retention + Tds + Cess + GstTds + OtherDeductions + AdvanceRecovery;
    public double Net => Invoice - StatutoryDeductions;

    public JsonObject ToJson()
    {
        var lines = new JsonArray();
        foreach (var l in Lines) lines.Add(l.ToJson());
        return new JsonObject
        {
            ["id"] = Id,
            ["number"] = Number,
            ["bill_no"] = BillNo,
            ["issued_by"] = IssuedByRole.ToToken(),
            ["date"] = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["contract_id"] = ContractId,
            ["contract_label"] = ContractLabel,
            ["party"] = Party,
            ["retention_pct"] = RetentionPct,
            ["other_deductions"] = OtherDeductions,
            ["advance_recovery"] = AdvanceRecovery,
            ["gst_pct"] = GstPct,
            ["tds_pct"] = TdsPct,
            ["cess_pct"] = CessPct,
            ["gst_tds_pct"] = GstTdsPct,
            ["certified"] = Certified ? 1 : 0,
            ["lines"] = lines,
            ["created_utc"] = CreatedUtc.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    public static RunningBill FromJson(JsonObject o)
    {
        var b = new RunningBill
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Number = o["number"]?.GetValue<string>() ?? "",
            BillNo = o["bill_no"]?.GetValue<int>() ?? 0,
            IssuedByRole = o["issued_by"] is null ? PartyRole.PM : PartyRoleX.Parse(o["issued_by"]!.GetValue<string>()),
            ContractId = o["contract_id"]?.GetValue<string>() ?? "",
            ContractLabel = o["contract_label"]?.GetValue<string>() ?? "",
            Party = o["party"]?.GetValue<string>() ?? "",
            RetentionPct = o["retention_pct"]?.GetValue<double>() ?? 5,
            OtherDeductions = o["other_deductions"]?.GetValue<double>() ?? 0,
            AdvanceRecovery = o["advance_recovery"]?.GetValue<double>() ?? 0,
            GstPct = o["gst_pct"]?.GetValue<double>() ?? 0,
            TdsPct = o["tds_pct"]?.GetValue<double>() ?? 0,
            CessPct = o["cess_pct"]?.GetValue<double>() ?? 0,
            GstTdsPct = o["gst_tds_pct"]?.GetValue<double>() ?? 0,
            Certified = (o["certified"]?.GetValue<int>() ?? 0) != 0
        };
        if (DateTime.TryParse(o["date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) b.Date = d;
        if (DateTime.TryParse(o["created_utc"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cu)) b.CreatedUtc = cu;
        if (o["lines"] is JsonArray la) foreach (var it in la) if (it is JsonObject lo) b.Lines.Add(BillLine.FromJson(lo));
        return b;
    }
}

/// <summary>Project accounts: running bills, cash/bank transactions, opening balances.</summary>
public sealed class AccountsBook
{
    public string Prefix { get; set; } = "";
    public double OpeningCash { get; set; }
    public double OpeningBank { get; set; }
    public ObservableCollection<RunningBill> Bills { get; } = new();
    public ObservableCollection<CashTxn> Transactions { get; } = new();
    private readonly Dictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);

    public string EffectivePrefix(string companyName)
    {
        if (!string.IsNullOrWhiteSpace(Prefix)) return Prefix.Trim();
        var initials = new string((companyName ?? "")
            .Split(new[] { ' ', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0])).ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "OC" : initials;
    }

    private static string PartyPrefix(RunningBill b, string companyName) =>
        ProjectStore.Current.Parties.For(b.IssuedByRole).EffectivePrefix(companyName);

    private static string CounterKey(RunningBill b, string fy) =>
        $"{b.IssuedByRole.ToToken()}|RA|{fy}";

    public string PreviewBillNumber(RunningBill b, string companyName)
    {
        string fy = OfficeRegister.FinancialYear(b.Date);
        int next = (_counters.TryGetValue(CounterKey(b, fy), out var last) ? last : 0) + 1;
        return $"{PartyPrefix(b, companyName)}/RA/{fy}/{next:000}";
    }

    public void CertifyBill(RunningBill b, string companyName)
    {
        if (b.Certified && !string.IsNullOrWhiteSpace(b.Number)) return;
        string fy = OfficeRegister.FinancialYear(b.Date);
        string key = CounterKey(b, fy);
        int next = (_counters.TryGetValue(key, out var last) ? last : 0) + 1;
        _counters[key] = next;
        b.BillNo = Bills.Count(x => x.Certified && x.IssuedByRole == b.IssuedByRole) + 1;
        b.Number = $"{PartyPrefix(b, companyName)}/RA/{fy}/{next:000}";
        b.Certified = true;
    }

    // ---- balances ----
    public double Receipts(CashAccount acct) => Transactions.Where(t => t.Kind == CashKind.Receipt && t.Account == acct).Sum(t => t.Amount);
    public double Payments(CashAccount acct) => Transactions.Where(t => t.Kind == CashKind.Payment && t.Account == acct).Sum(t => t.Amount);
    public double CashBalance => OpeningCash + Receipts(CashAccount.Cash) - Payments(CashAccount.Cash);
    public double BankBalance => OpeningBank + Receipts(CashAccount.Bank) - Payments(CashAccount.Bank);
    public double TotalReceipts => Transactions.Where(t => t.Kind == CashKind.Receipt).Sum(t => t.Amount);
    public double TotalPayments => Transactions.Where(t => t.Kind == CashKind.Payment).Sum(t => t.Amount);

    public IEnumerable<string> Parties()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Transactions) if (!string.IsNullOrWhiteSpace(t.Party)) set.Add(t.Party.Trim());
        foreach (var b in Bills) if (!string.IsNullOrWhiteSpace(b.Party)) set.Add(b.Party.Trim());
        return set;
    }

    public void Clear()
    {
        Bills.Clear();
        Transactions.Clear();
        _counters.Clear();
        Prefix = "";
        OpeningCash = OpeningBank = 0;
    }

    public JsonObject ToJson()
    {
        var bills = new JsonArray();
        foreach (var b in Bills) bills.Add(b.ToJson());
        var txns = new JsonArray();
        foreach (var t in Transactions) txns.Add(t.ToJson());
        var counters = new JsonObject();
        foreach (var kv in _counters) counters[kv.Key] = kv.Value;
        return new JsonObject
        {
            ["prefix"] = Prefix ?? "",
            ["opening_cash"] = OpeningCash,
            ["opening_bank"] = OpeningBank,
            ["counters"] = counters,
            ["bills"] = bills,
            ["transactions"] = txns
        };
    }

    public void LoadFrom(JsonObject? o)
    {
        Clear();
        if (o is null) return;
        Prefix = o["prefix"]?.GetValue<string>() ?? "";
        OpeningCash = o["opening_cash"]?.GetValue<double>() ?? 0;
        OpeningBank = o["opening_bank"]?.GetValue<double>() ?? 0;
        if (o["counters"] is JsonObject c)
            foreach (var kv in c)
                if (kv.Value is JsonValue v && v.TryGetValue<int>(out var n))
                    _counters[OfficeRegister.MigrateCounterKey(kv.Key)] = n;
        if (o["bills"] is JsonArray ba) foreach (var it in ba) if (it is JsonObject bo) Bills.Add(RunningBill.FromJson(bo));
        if (o["transactions"] is JsonArray ta) foreach (var it in ta) if (it is JsonObject to) Transactions.Add(CashTxn.FromJson(to));
    }
}
