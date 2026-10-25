// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;

namespace BBSApp.Services;

/// <summary>Per-project identity for reports, estimates, and title bar.</summary>
public sealed class ProjectInfo
{
    public string Name { get; set; } = "Untitled Project";
    public string Location { get; set; } = "";
    /// <summary>Short project description from O-Studio (title plates, estimate covers, report headers).</summary>
    public string Summary { get; set; } = "";
    public string ClientName { get; set; } = "";
    /// <summary>Engineer, Architect, or PMC.</summary>
    public string PreparedByRole { get; set; } = "Engineer";
    public string PreparedByName { get; set; } = "";
    public string CompanyName { get; set; } = Branding.Company;
    public string ContactPhone { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>GST identification number (India).</summary>
    public string Gstin { get; set; } = "";
    /// <summary>Company / corporate identity number.</summary>
    public string Cin { get; set; } = "";
    /// <summary>Permanent account number (tax).</summary>
    public string Pan { get; set; } = "";
    /// <summary>Absolute or relative path to project logo image (png/jpg).</summary>
    public string LogoPath { get; set; } = "";
    /// <summary>AORMS Connect / hub project UUID for Flush + JM pull (S11).</summary>
    public string HubProjectId { get; set; } = "";
    /// <summary>AORMS online project (Connect): the project office id, and AQC's binding id once bound. Set from File > Online projects.</summary>
    public string AormsProjectOfficeId { get; set; } = "";
    public string AormsAqcId { get; set; } = "";
    /// <summary>Last change number of the online project this file has caught up with (see ProjectSyncHost).</summary>
    public long AormsHeadSeq { get; set; }
    /// <summary>The take-off database (.vdb) O-Draft recorded this project's take-off in. Read from the Take-off page.</summary>
    public string TakeoffVdbPath { get; set; } = "";
    /// <summary>Annotated drawing PDFs fetched from ShilpiDB, one per drawing.</summary>
    public List<StoredAnnotatedDrawing> AnnotatedDrawings { get; } = new();

    public static readonly string[] PreparedByRoles = { "Engineer", "Architect", "PMC" };

    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["location"] = Location,
        ["summary"] = Summary,
        ["client_name"] = ClientName,
        ["prepared_by_role"] = PreparedByRole,
        ["prepared_by_name"] = PreparedByName,
        ["company_name"] = CompanyName,
        ["contact_phone"] = ContactPhone,
        ["contact_email"] = ContactEmail,
        ["address"] = Address,
        ["gstin"] = Gstin,
        ["cin"] = Cin,
        ["pan"] = Pan,
        ["logo_path"] = LogoPath,
        ["hub_project_id"] = HubProjectId,
        ["aorms_project_office_id"] = AormsProjectOfficeId,
        ["aorms_aqc_id"] = AormsAqcId,
        ["aorms_head_seq"] = AormsHeadSeq,
        ["takeoff_vdb_path"] = TakeoffVdbPath,
        ["annotated_drawings"] = new JsonArray(AnnotatedDrawings.Select(a => (JsonNode)a.ToJson()).ToArray())
    };

    public void LoadFrom(JsonObject? o)
    {
        if (o is null) return;
        Name = o["name"]?.GetValue<string>() ?? Name;
        Location = o["location"]?.GetValue<string>() ?? "";
        Summary = o["summary"]?.GetValue<string>() ?? "";
        ClientName = o["client_name"]?.GetValue<string>() ?? "";
        PreparedByRole = o["prepared_by_role"]?.GetValue<string>() ?? "Engineer";
        PreparedByName = o["prepared_by_name"]?.GetValue<string>() ?? "";
        CompanyName = o["company_name"]?.GetValue<string>() ?? Branding.Company;
        ContactPhone = o["contact_phone"]?.GetValue<string>() ?? "";
        ContactEmail = o["contact_email"]?.GetValue<string>() ?? "";
        Address = o["address"]?.GetValue<string>() ?? "";
        Gstin = o["gstin"]?.GetValue<string>() ?? "";
        Cin = o["cin"]?.GetValue<string>() ?? "";
        Pan = o["pan"]?.GetValue<string>() ?? "";
        LogoPath = o["logo_path"]?.GetValue<string>() ?? "";
        HubProjectId = o["hub_project_id"]?.GetValue<string>() ?? "";
        AormsProjectOfficeId = o["aorms_project_office_id"]?.GetValue<string>() ?? "";
        AormsAqcId = o["aorms_aqc_id"]?.GetValue<string>() ?? "";
        AormsHeadSeq = o["aorms_head_seq"] is JsonValue hv && hv.TryGetValue<long>(out var hs) ? hs : 0;
        TakeoffVdbPath = o["takeoff_vdb_path"]?.GetValue<string>() ?? "";
        AnnotatedDrawings.Clear();
        if (o["annotated_drawings"] is JsonArray ad)
            foreach (var e in ad.OfType<JsonObject>()) AnnotatedDrawings.Add(StoredAnnotatedDrawing.FromJson(e));
    }

    public void Reset()
    {
        Name = "Untitled Project";
        Location = "";
        Summary = "";
        ClientName = "";
        PreparedByRole = "Engineer";
        PreparedByName = "";
        CompanyName = Branding.Company;
        ContactPhone = "";
        ContactEmail = "";
        Address = "";
        Gstin = "";
        Cin = "";
        Pan = "";
        LogoPath = "";
        HubProjectId = "";
        AormsProjectOfficeId = "";
        AormsAqcId = "";
        AormsHeadSeq = 0;
        TakeoffVdbPath = "";
        AnnotatedDrawings.Clear();
        FromCompanyFile = false;
    }

    /// <summary>
    /// True while the project is linked to the shared company file: the company's name, address, contacts and registrations come from
    /// OSAS, and the project's name, site and client come from O-Studio. They are read for every document and are not edited here.
    /// Not saved with the project; it is true only after a read of the company file.
    /// </summary>
    public bool FromCompanyFile { get; private set; }

    /// <summary>Takes the company's and the project's details from the company file. Registrations that O-CoreOp alone keeps (CIN, logo) stay as they are.</summary>
    public void ApplyCompanyFile(Office.Shared.DocumentContext context)
    {
        var c = context.Company;
        if (!string.IsNullOrWhiteSpace(c.Name)) CompanyName = c.Name;
        Address = c.Address;
        ContactPhone = c.Phone;
        ContactEmail = c.Email;
        Gstin = c.Gstin;
        Pan = c.Pan;
        if (context.Project is { } p)
        {
            if (!string.IsNullOrWhiteSpace(p.Name)) Name = p.Name;
            Location = p.SiteAddress;
            Summary = p.Summary;
            ClientName = p.ClientName;
        }
        FromCompanyFile = true;
    }

    /// <summary>"GSTIN: … · CIN: … · PAN: …" for the present IDs, or empty.</summary>
    public string RegistrationLine
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(Gstin)) bits.Add($"GSTIN: {Gstin.Trim()}");
            if (!string.IsNullOrWhiteSpace(Cin)) bits.Add($"CIN: {Cin.Trim()}");
            if (!string.IsNullOrWhiteSpace(Pan)) bits.Add($"PAN: {Pan.Trim()}");
            return string.Join("  ·  ", bits);
        }
    }

    public string PreparedByLine
    {
        get
        {
            string role = string.IsNullOrWhiteSpace(PreparedByRole) ? "Engineer" : PreparedByRole.Trim();
            if (string.IsNullOrWhiteSpace(PreparedByName)) return $"Prepared by {role}";
            return $"Prepared by {role}: {PreparedByName.Trim()}";
        }
    }

    public string CompanyDisplay =>
        string.IsNullOrWhiteSpace(CompanyName) ? Branding.CompanyLine : CompanyName.Trim();

    public static string LogosDirectory => Branding.ResolveAppDataSubdir("project-logos");

    /// <summary>Copy source image into app LocalAppData and return the stored path.</summary>
    public static async Task<string> ImportLogoAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Logo file not found.", sourcePath);

        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var destName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var dest = Path.Combine(LogosDirectory, destName);
        await Task.Run(() => File.Copy(sourcePath, dest, overwrite: true));
        return dest;
    }

    public static string? ResolveLogoFile(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (File.Exists(stored)) return stored;

        var proj = ProjectStore.Current.FilePath;
        if (!string.IsNullOrWhiteSpace(proj))
        {
            var projDir = Path.GetDirectoryName(proj);
            if (!string.IsNullOrEmpty(projDir))
            {
                var combined = Path.GetFullPath(Path.Combine(projDir, stored));
                if (File.Exists(combined)) return combined;
            }
        }

        var local = Path.Combine(LogosDirectory, Path.GetFileName(stored));
        if (File.Exists(local)) return local;

        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Branding.LegacyAppDataFolder, "project-logos", Path.GetFileName(stored));
        return File.Exists(legacy) ? legacy : null;
    }

    public string? ResolvedLogoPath => ResolveLogoFile(LogoPath);
}
