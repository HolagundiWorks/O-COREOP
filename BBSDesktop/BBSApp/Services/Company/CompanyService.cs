// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using BBSApp.Services;
using Office.Shared;

namespace BBSApp.Services.Company;

/// <summary>Which project in the company file the open project is, and the revision of its document that was loaded or last saved.</summary>
public sealed class SharedProjectLink
{
    public SharedProjectLink(long projectId, long revision)
    {
        ProjectId = projectId;
        Revision = revision;
    }

    public long ProjectId { get; }

    public long Revision { get; set; }

    /// <summary>True once another app has saved a newer revision than this one.</summary>
    public bool ChangedElsewhere { get; set; }
}

/// <summary>
/// The company file shared with O-Studio (an .osas file; earlier .adms files open too). While one is open, a project can be opened from it and saved back to it,
/// and O-Studio can have the same file open at the same time. O-CoreOp keeps a project's quantities in the company file as a
/// document on that project; the clients and projects themselves are the office's records and are shared.
/// </summary>
public static class CompanyService
{
    static SharedCompany? _company;
    static long _lastDataVersion = -1;

    /// <summary>Folder for the engine's recent-companies list and temporary working copies.</summary>
    public static string AppDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "O-CoreOp", "office");

    static string RememberFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "O-CoreOp", "company.txt");

    public static event Action? Changed;

    static string SuiteFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OSAS", "suite.json");

    /// <summary>The company file chosen in O-Studio, if there is one and it still exists. The company is chosen only there.</summary>
    public static string? SuitePath()
    {
        try
        {
            if (!File.Exists(SuiteFile))
                return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SuiteFile));
            return doc.RootElement.TryGetProperty("company_path", out var p) && p.GetString() is { Length: > 0 } path && File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Starts O-Studio, where the company is chosen, created and signed in to. False when O-Studio is not installed.</summary>
    public static bool StartStudio()
    {
        var folder = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\O-Studio", "InstallLocation", null) as string;
        var exe = Path.Combine(string.IsNullOrWhiteSpace(folder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "O-Studio") : folder, "O-Studio.exe");
        if (!File.Exists(exe))
            return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    public static SharedCompany? Current => _company;

    public static bool IsOpen => _company is not null;

    /// <summary>True when office.dll is beside the app and speaks the version this build knows.</summary>
    public static bool EngineAvailable
    {
        get
        {
            try
            {
                OfficeSession.UseLibraryFolder(AppContext.BaseDirectory);
            }
            catch (InvalidOperationException)
            {
                // The resolver can be set once; it already was.
            }
            return OfficeSession.IsAvailable();
        }
    }

    public const string MissingEngine = "The company-file engine (office.dll) is not installed beside the app, so shared company files cannot be opened.";

    public static string StatusText => _company is null ? "Company · not open" : $"Company · {_company.Name}";

    /// <summary>The last company file opened, for the file picker to start from, or null.</summary>
    public static string? LastPath
    {
        get
        {
            try
            {
                return File.Exists(RememberFile) ? File.ReadAllText(RememberFile).Trim() : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public static Task OpenAsync(string path, string user, string password, string secret = "", string unlock = "") =>
        Task.Run(() =>
        {
            Close();
            _company = SharedCompany.Open(AppDir, path, user, password, secret, unlock);
            _lastDataVersion = -1;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RememberFile)!);
                File.WriteAllText(RememberFile, path);
            }
            catch (IOException)
            {
                // Remembering the path is a convenience only.
            }
            Changed?.Invoke();
        });

    /// <summary>Opens a company file signed in by a one-time ticket from O-Studio.</summary>
    public static Task OpenWithTicketAsync(string path, string ticket) =>
        Task.Run(() =>
        {
            Close();
            _company = SharedCompany.OpenWithTicket(AppDir, path, ticket);
            _lastDataVersion = -1;
            Changed?.Invoke();
        });

    public static void Close()
    {
        var company = _company;
        _company = null;
        company?.Dispose();
        Changed?.Invoke();
    }

    /// <summary>
    /// Takes the company's details (kept in O-Studio) and the project's details (also in the company file) for the open project,
    /// so reports and title blocks print what those apps hold now. Does nothing when the project is not linked to the company file.
    /// </summary>
    public static async Task RefreshDetailsAsync()
    {
        var store = ProjectStore.Current;
        var link = store.Shared;
        var company = _company;
        if (company is null || link is null)
            return;
        var context = await Task.Run(() => company.DocumentContext(link.ProjectId));
        store.Info.ApplyCompanyFile(context);
        store.Parties.FollowCompanyFile(store.Info);
        store.Name = store.Info.Name;
    }

    /// <summary>
    /// Tie the open project to a Studio project (the take-off header names it) and take its details from the company file, read only. False when
    /// the company file is not open or is another company's.
    /// </summary>
    public static async Task<bool> LinkProjectAsync(string? companyUuid, long projectId)
    {
        var company = _company;
        if (company is null || string.IsNullOrEmpty(companyUuid) || company.Uuid != companyUuid) return false;
        var store = ProjectStore.Current;
        if (store.Shared?.ProjectId == projectId)
        {
            await RefreshDetailsAsync();
            return true;
        }
        var revision = company.DocumentRevision(projectId, SharedCompany.DocumentKind);
        store.Shared = new SharedProjectLink(projectId, revision);
        await RefreshDetailsAsync();
        _ = TryPushProjectSummary(store);
        return true;
    }

    /// <summary>
    /// Pushes BOQ / estimate / RA abstracts to the company file (<c>aqc.project</c>) when this project is linked.
    /// Returns a message when the push failed; null when it succeeded or there was nothing to do.
    /// </summary>
    public static string? TryPushProjectSummary(ProjectStore store)
    {
        var company = _company;
        var link = store.Shared;
        if (company is null || link is null)
            return null;
        var revision = link.Revision;
        var err = CompanyProjectShare.TryPushSummary(company, link.ProjectId, ref revision, store.ToJson());
        link.Revision = revision;
        if (err is not null)
            link.ChangedElsewhere = true;
        return err;
    }

    /// <summary>Shares the latest estimate as an approval draft in O-Studio (kind <c>aqc.estimate.draft</c>).</summary>
    public static string? TryShareEstimateDraft(ProjectStore store)
    {
        var company = _company;
        var link = store.Shared;
        var estimate = store.LastEstimate;
        if (company is null || link is null)
            return "Open the company from O-Studio and link this project first.";
        if (estimate is null)
            return "Calculate an estimate first.";
        var payload = EstimateCalculator.ToJson(estimate);
        var envelope = StudioDraftShare.BuildEnvelope(
            "estimate",
            StudioDraftShare.StateDraft,
            company.User,
            DateTimeOffset.UtcNow,
            estimate.ArtifactRef,
            StudioDraftShare.EstimateSummary(estimate.GrandTotal, estimate.RateBookVersionName),
            payload);
        try
        {
            StudioDraftShare.PutDraft(company, link.ProjectId, StudioDraftShare.EstimateDraftKind, envelope);
            _ = TryPushProjectSummary(store);
            return null;
        }
        catch (OfficeException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Shares one running bill as an approval draft (kind <c>aqc.ra.draft.&lt;id&gt;</c>).</summary>
    public static string? TryShareRaBillDraft(ProjectStore store, RunningBill bill)
    {
        var company = _company;
        var link = store.Shared;
        if (company is null || link is null)
            return "Open the company from O-Studio and link this project first.";
        var payload = bill.ToJson();
        var kind = StudioDraftShare.RaDraftKind(bill.Id);
        var title = string.IsNullOrWhiteSpace(bill.Number) ? "RA bill (draft)" : bill.Number;
        var envelope = StudioDraftShare.BuildEnvelope(
            "ra_bill",
            StudioDraftShare.StateDraft,
            company.User,
            DateTimeOffset.UtcNow,
            null,
            StudioDraftShare.RaBillSummary(bill.Id, title, bill.Gross, bill.Net, bill.Party),
            payload);
        try
        {
            StudioDraftShare.PutDraft(company, link.ProjectId, kind, envelope);
            _ = TryPushProjectSummary(store);
            return null;
        }
        catch (OfficeException ex)
        {
            return ex.Message;
        }
    }
}
