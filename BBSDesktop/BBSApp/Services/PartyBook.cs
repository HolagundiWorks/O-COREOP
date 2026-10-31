// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;

namespace BBSApp.Services;

/// <summary>Which side of the contract a document/bill is issued from.</summary>
public enum PartyRole { PM, Contractor }

public static class PartyRoleX
{
    public static string ToToken(this PartyRole r) => r == PartyRole.Contractor ? "contractor" : "pm";

    public static PartyRole Parse(string? token) =>
        string.Equals(token, "contractor", StringComparison.OrdinalIgnoreCase)
            ? PartyRole.Contractor
            : PartyRole.PM;

    public static string Display(this PartyRole r) => r == PartyRole.Contractor ? "Contractor" : "Project Manager";
}

/// <summary>
/// One operating party on the project (the Project Manager / PMC, or the Contractor).
/// Carries its own letterhead identity and numbering prefix so letters, bills and other
/// documents issued by this persona are branded and numbered independently.
/// </summary>
public sealed class Party
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public PartyRole Role { get; set; } = PartyRole.PM;
    public string Company { get; set; } = "";
    public string SignatoryName { get; set; } = "";
    /// <summary>Designation under the signature (e.g. "Project Manager", "Proprietor").</summary>
    public string SignatoryRole { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Gstin { get; set; } = "";
    public string Pan { get; set; } = "";
    /// <summary>Numbering prefix for this party's series. Empty = derive from company initials.</summary>
    public string NumberPrefix { get; set; } = "";
    public string LogoPath { get; set; } = "";

    public static string DefaultPrefix(PartyRole r) => r == PartyRole.Contractor ? "CON" : "PMC";
    public static string DefaultSignatoryRole(PartyRole r) => r == PartyRole.Contractor ? "Contractor" : "Project Manager";

    /// <summary>Numbering prefix: explicit prefix, else company initials, else the role default.</summary>
    public string EffectivePrefix(string? fallbackCompany = null)
    {
        if (!string.IsNullOrWhiteSpace(NumberPrefix)) return NumberPrefix.Trim();
        var src = !string.IsNullOrWhiteSpace(Company) ? Company : (fallbackCompany ?? "");
        var initials = new string(src
            .Split(new[] { ' ', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0])).ToArray());
        return string.IsNullOrWhiteSpace(initials) ? DefaultPrefix(Role) : initials;
    }

    /// <summary>Company name to print on the letterhead (falls back to project company).</summary>
    public string CompanyDisplay(string? fallbackCompany = null) =>
        !string.IsNullOrWhiteSpace(Company) ? Company.Trim()
        : !string.IsNullOrWhiteSpace(fallbackCompany) ? fallbackCompany!.Trim()
        : Role.Display();

    public string? ResolvedLogoPath => ProjectInfo.ResolveLogoFile(LogoPath);

    public string RegistrationLine
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(Gstin)) bits.Add($"GSTIN: {Gstin.Trim()}");
            if (!string.IsNullOrWhiteSpace(Pan)) bits.Add($"PAN: {Pan.Trim()}");
            return string.Join("  ·  ", bits);
        }
    }

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["role"] = Role.ToToken(),
        ["company"] = Company,
        ["signatory_name"] = SignatoryName,
        ["signatory_role"] = SignatoryRole,
        ["address"] = Address,
        ["phone"] = Phone,
        ["email"] = Email,
        ["gstin"] = Gstin,
        ["pan"] = Pan,
        ["number_prefix"] = NumberPrefix,
        ["logo_path"] = LogoPath
    };

    public static Party FromJson(JsonObject o, PartyRole roleDefault) => new()
    {
        Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
        Role = o["role"] is null ? roleDefault : PartyRoleX.Parse(o["role"]!.GetValue<string>()),
        Company = o["company"]?.GetValue<string>() ?? "",
        SignatoryName = o["signatory_name"]?.GetValue<string>() ?? "",
        SignatoryRole = o["signatory_role"]?.GetValue<string>() ?? "",
        Address = o["address"]?.GetValue<string>() ?? "",
        Phone = o["phone"]?.GetValue<string>() ?? "",
        Email = o["email"]?.GetValue<string>() ?? "",
        Gstin = o["gstin"]?.GetValue<string>() ?? "",
        Pan = o["pan"]?.GetValue<string>() ?? "",
        NumberPrefix = o["number_prefix"]?.GetValue<string>() ?? "",
        LogoPath = o["logo_path"]?.GetValue<string>() ?? ""
    };
}

/// <summary>
/// The two personas that operate one project — Project Manager and Contractor — plus which
/// one is currently active. Every letter, bill, contract and cash entry is tagged with the
/// persona that issued it and numbered/branded from that persona.
/// </summary>
public sealed class PartyBook
{
    public Party Pm { get; private set; } = new() { Role = PartyRole.PM };
    public Party Contractor { get; private set; } = new() { Role = PartyRole.Contractor };

    /// <summary>Persona currently driving the app — new documents default to this side.</summary>
    public PartyRole Active { get; set; } = PartyRole.PM;

    public Party ActiveParty => For(Active);
    public Party For(PartyRole role) => role == PartyRole.Contractor ? Contractor : Pm;

    /// <summary>Counterparty of the given role (PM ↔ Contractor).</summary>
    public Party Counterparty(PartyRole role) => role == PartyRole.Contractor ? Pm : Contractor;

    /// <summary>
    /// The company name on documents comes from O-Studio's company file, so the PM party takes its name, address, contacts
    /// and registrations from there each time they are read, instead of keeping the copy made the first time. The signatory stays as set.
    /// </summary>
    public void FollowCompanyFile(ProjectInfo info)
    {
        if (!info.FromCompanyFile) return;
        Pm.Company = info.CompanyName;
        Pm.Address = info.Address;
        Pm.Phone = info.ContactPhone;
        Pm.Email = info.ContactEmail;
        Pm.Gstin = info.Gstin;
        Pm.Pan = info.Pan;
    }

    /// <summary>Seed missing PM identity from the project's own company/prepared-by, first-run defaults.</summary>
    public void EnsureDefaults(ProjectInfo info)
    {
        Pm.Role = PartyRole.PM;
        Contractor.Role = PartyRole.Contractor;
        if (string.IsNullOrWhiteSpace(Pm.Company)) Pm.Company = info.CompanyName;
        if (string.IsNullOrWhiteSpace(Pm.SignatoryName)) Pm.SignatoryName = info.PreparedByName;
        if (string.IsNullOrWhiteSpace(Pm.SignatoryRole))
            Pm.SignatoryRole = info.PreparedByRole == "PMC" ? "Project Manager" : info.PreparedByRole;
        if (string.IsNullOrWhiteSpace(Pm.Address)) Pm.Address = info.Address;
        if (string.IsNullOrWhiteSpace(Pm.Phone)) Pm.Phone = info.ContactPhone;
        if (string.IsNullOrWhiteSpace(Pm.Email)) Pm.Email = info.ContactEmail;
        if (string.IsNullOrWhiteSpace(Pm.Gstin)) Pm.Gstin = info.Gstin;
        if (string.IsNullOrWhiteSpace(Pm.Pan)) Pm.Pan = info.Pan;
        if (string.IsNullOrWhiteSpace(Pm.LogoPath)) Pm.LogoPath = info.LogoPath;
        if (string.IsNullOrWhiteSpace(Contractor.SignatoryRole)) Contractor.SignatoryRole = "Proprietor";
    }

    public void Reset()
    {
        Pm = new Party { Role = PartyRole.PM };
        Contractor = new Party { Role = PartyRole.Contractor };
        Active = PartyRole.PM;
    }

    public JsonObject ToJson() => new()
    {
        ["active"] = Active.ToToken(),
        ["pm"] = Pm.ToJson(),
        ["contractor"] = Contractor.ToJson()
    };

    public void LoadFrom(JsonObject? o, ProjectInfo info)
    {
        Reset();
        if (o is not null)
        {
            if (o["pm"] is JsonObject pm) Pm = Party.FromJson(pm, PartyRole.PM);
            if (o["contractor"] is JsonObject con) Contractor = Party.FromJson(con, PartyRole.Contractor);
            Active = PartyRoleX.Parse(o["active"]?.GetValue<string>());
        }
        EnsureDefaults(info);
    }
}
