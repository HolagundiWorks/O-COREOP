// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BBSApp.Services;

/// <summary>A correspondence/office document type (code + display + default body).</summary>
public sealed class DocTypeInfo
{
    public string Code { get; }
    public string Display { get; }
    public DocTypeInfo(string code, string display) { Code = code; Display = display; }
    public override string ToString() => Display;

    public static readonly IReadOnlyList<DocTypeInfo> All = new[]
    {
        new DocTypeInfo("LTR", "Letter"),
        new DocTypeInfo("MEMO", "Memo"),
        new DocTypeInfo("NOTICE", "Notice"),
        new DocTypeInfo("CIRC", "Circular"),
        new DocTypeInfo("CERT", "Certificate"),
        new DocTypeInfo("DECL", "Declaration"),
        new DocTypeInfo("SI", "Site instruction"),
        new DocTypeInfo("WON", "Work order note"),
        new DocTypeInfo("IPC", "Interim payment certificate"),
    };

    public static DocTypeInfo Find(string? code) =>
        All.FirstOrDefault(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static string DisplayFor(string? code) => Find(code).Display;

    /// <summary>Whether this type addresses a recipient ("To"). Notices/circulars are broadcast.</summary>
    public static bool HasRecipient(string code) =>
        code is "LTR" or "MEMO" or "SI" or "WON" or "CERT" or "IPC";

    public static string DefaultBody(string code) => code switch
    {
        "LTR" => "Dear Sir/Madam,\n\nWith reference to the above subject, we wish to inform you that …\n\n"
                 + "We request you to kindly …\n\nThanking you.",
        "MEMO" => "This memo is issued for the information and necessary action of all concerned.\n\n"
                  + "1. …\n2. …\n\nAll concerned are requested to comply.",
        "NOTICE" => "Notice is hereby given that …\n\nAll concerned are requested to take note and act accordingly.",
        "CIRC" => "This circular is issued for general information and compliance by all staff/sub-contractors.\n\n"
                  + "1. …\n2. …",
        "CERT" => "This is to certify that …\n\nThis certificate is issued on request for whatever purpose it may serve.",
        "DECL" => "I/We hereby declare that the information furnished above is true and correct "
                  + "to the best of my/our knowledge and belief.\n\nI/We undertake to …",
        "SI" => "You are hereby instructed to carry out the following at site with immediate effect:\n\n"
                + "1. …\n2. …\n\nThis instruction forms part of the contract. Please acknowledge receipt.",
        "WON" => "You are hereby entrusted with the following work as per agreed rates and specifications:\n\n"
                 + "Scope: …\nRate basis: …\nCompletion: …\n\nCommence work on receipt of this note.",
        "IPC" => "This is to certify that the work covered by the above running-account bill has been "
                 + "measured and checked, and that the net amount stated below is due and payable to the "
                 + "contractor, subject to the terms of the contract.\n\n"
                 + "Gross value of work done: …\nLess statutory & contract deductions: …\nNet amount certified: …\n\n"
                 + "Payment may be released accordingly.",
        _ => ""
    };
}

/// <summary>A single office/correspondence document. Number is assigned on finalize.</summary>
public sealed class OfficeDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TypeCode { get; set; } = "LTR";
    public string Number { get; set; } = "";
    /// <summary>Persona that issues this document (drives letterhead + numbering series).</summary>
    public PartyRole IssuedByRole { get; set; } = PartyRole.PM;
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public string ToName { get; set; } = "";
    public string ToAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string SignatoryName { get; set; } = "";
    public string SignatoryRole { get; set; } = "";
    public bool Finalized { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["type"] = TypeCode,
        ["number"] = Number,
        ["issued_by"] = IssuedByRole.ToToken(),
        ["issue_date"] = IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["to_name"] = ToName,
        ["to_address"] = ToAddress,
        ["subject"] = Subject,
        ["body"] = Body,
        ["signatory_name"] = SignatoryName,
        ["signatory_role"] = SignatoryRole,
        ["finalized"] = Finalized ? 1 : 0,
        ["created_utc"] = CreatedUtc.ToString("o", CultureInfo.InvariantCulture)
    };

    public static OfficeDocument FromJson(JsonObject o)
    {
        var d = new OfficeDocument
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            TypeCode = o["type"]?.GetValue<string>() ?? "LTR",
            Number = o["number"]?.GetValue<string>() ?? "",
            IssuedByRole = PartyRoleX.Parse(o["issued_by"]?.GetValue<string>()),
            ToName = o["to_name"]?.GetValue<string>() ?? "",
            ToAddress = o["to_address"]?.GetValue<string>() ?? "",
            Subject = o["subject"]?.GetValue<string>() ?? "",
            Body = o["body"]?.GetValue<string>() ?? "",
            SignatoryName = o["signatory_name"]?.GetValue<string>() ?? "",
            SignatoryRole = o["signatory_role"]?.GetValue<string>() ?? "",
            Finalized = (o["finalized"]?.GetValue<int>() ?? 0) != 0
        };
        if (DateTime.TryParse(o["issue_date"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt)) d.IssueDate = dt;
        if (DateTime.TryParse(o["created_utc"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var cu)) d.CreatedUtc = cu;
        return d;
    }
}

/// <summary>Project correspondence register: documents + per-type / per-FY running numbers.</summary>
public sealed class OfficeRegister
{
    /// <summary>Numbering prefix (e.g. company initials). Empty = derive from company name.</summary>
    public string Prefix { get; set; } = "";

    public ObservableCollection<OfficeDocument> Documents { get; } = new();

    /// <summary>"CODE|FY" -> last used sequence number.</summary>
    private readonly Dictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Indian financial year (Apr–Mar) as "2026-27".</summary>
    public static string FinancialYear(DateTime d)
    {
        int start = d.Month >= 4 ? d.Year : d.Year - 1;
        return $"{start}-{(start + 1) % 100:00}";
    }

    public string EffectivePrefix(string companyName)
    {
        if (!string.IsNullOrWhiteSpace(Prefix)) return Prefix.Trim();
        var initials = new string((companyName ?? "")
            .Split(new[] { ' ', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0])).ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "OC" : initials;
    }

    private static string Compose(string prefix, string code, string fy, int seq) =>
        $"{prefix}/{code}/{fy}/{seq:000}";

    /// <summary>Prefix for the persona that issues this document (its own series).</summary>
    private static string PartyPrefix(OfficeDocument doc, string companyName) =>
        ProjectStore.Current.Parties.For(doc.IssuedByRole).EffectivePrefix(companyName);

    /// <summary>Counter key — scoped by issuing persona so PM and Contractor number independently.</summary>
    private static string CounterKey(OfficeDocument doc, string fy) =>
        $"{doc.IssuedByRole.ToToken()}|{doc.TypeCode}|{fy}";

    /// <summary>Number this document WOULD get if finalized now (not yet assigned).</summary>
    public string PreviewNumber(OfficeDocument doc, string companyName)
    {
        string fy = FinancialYear(doc.IssueDate);
        int next = (_counters.TryGetValue(CounterKey(doc, fy), out var last) ? last : 0) + 1;
        return Compose(PartyPrefix(doc, companyName), doc.TypeCode, fy, next);
    }

    /// <summary>Assign the next running number for the persona/type/FY and lock the document.</summary>
    public void Finalize(OfficeDocument doc, string companyName)
    {
        if (doc.Finalized && !string.IsNullOrWhiteSpace(doc.Number)) return;
        string fy = FinancialYear(doc.IssueDate);
        string key = CounterKey(doc, fy);
        int next = (_counters.TryGetValue(key, out var last) ? last : 0) + 1;
        _counters[key] = next;
        doc.Number = Compose(PartyPrefix(doc, companyName), doc.TypeCode, fy, next);
        doc.Finalized = true;
    }

    /// <summary>Legacy counters (CODE|FY) predate personas — fold them into the PM series.</summary>
    internal static string MigrateCounterKey(string key) =>
        key.Split('|').Length >= 3 ? key : $"{PartyRole.PM.ToToken()}|{key}";

    public void Clear()
    {
        Documents.Clear();
        _counters.Clear();
        Prefix = "";
    }

    public JsonObject ToJson()
    {
        var docs = new JsonArray();
        foreach (var d in Documents) docs.Add(d.ToJson());
        var counters = new JsonObject();
        foreach (var kv in _counters) counters[kv.Key] = kv.Value;
        return new JsonObject
        {
            ["prefix"] = Prefix ?? "",
            ["counters"] = counters,
            ["documents"] = docs
        };
    }

    public void LoadFrom(JsonObject? o)
    {
        Clear();
        if (o is null) return;
        Prefix = o["prefix"]?.GetValue<string>() ?? "";
        if (o["counters"] is JsonObject c)
            foreach (var kv in c)
                if (kv.Value is JsonValue v && v.TryGetValue<int>(out var n))
                    _counters[MigrateCounterKey(kv.Key)] = n;
        if (o["documents"] is JsonArray arr)
            foreach (var item in arr)
                if (item is JsonObject doc) Documents.Add(OfficeDocument.FromJson(doc));
    }
}
