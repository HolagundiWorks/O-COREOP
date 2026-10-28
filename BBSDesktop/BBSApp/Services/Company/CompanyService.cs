// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
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

    /// <summary>The company file chosen in OSAS, the suite app, if there is one and it still exists. The company is chosen only there.</summary>
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

    /// <summary>Starts OSAS, where the company is chosen, created and signed in to. False when OSAS is not installed.</summary>
    public static bool StartOsas()
    {
        var folder = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\OSAS", "InstallLocation", null) as string;
        var exe = Path.Combine(string.IsNullOrWhiteSpace(folder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OSAS") : folder, "OSAS.exe");
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

    /// <summary>Opens a company file signed in by a one-time ticket from the suite app (OSAS).</summary>
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

    public static Task<IReadOnlyList<SharedProject>> ProjectsAsync() => Task.Run(() => Require().Projects());

    public static Task<IReadOnlyList<SharedClient>> ClientsAsync() => Task.Run(() => Require().Clients());

    /// <summary>False when this company session was opened from the suite — clients/projects are created only in Studio.</summary>
    public static bool CanCreateClientsAndProjects => CompanyOfficePolicy.AllowsClientProjectCreate(_company);

    public static Task<long> CreateProjectAsync(string name, long? clientId, string newClientName, string site) =>
        Task.Run(() =>
        {
            var company = Require();
            CompanyOfficePolicy.RequireClientProjectCreate(company);
            var client = clientId ?? company.CreateClient(newClientName);
            return company.CreateProject(name, client, site);
        });

    /// <summary>
    /// Shares an estimate snapshot into the company file as <see cref="StudioDraftShare.EstimateDraftKind"/> with state
    /// <c>draft</c> for Studio's inbox. Does not certify or lock anything in CoreOp.
    /// </summary>
    public static Task<(long Revision, string ChecksumSha256)> ShareEstimateDraftAsync(JsonObject estimatePayload, double grandTotal, string? rateBookVersionName) =>
        Task.Run(() =>
        {
            var company = Require();
            var link = ProjectStore.Current.Shared ?? throw new OfficeException(2, "Link this project to the company file before sharing a draft with Studio.");
            var project = ArtifactRefs.ProjectRef(company.Uuid, link.ProjectId);
            var checksumBody = ArtifactRefs.EstimateChecksumBody(estimatePayload);
            var revision = (uint)Math.Max(1, company.DocumentRevision(link.ProjectId, StudioDraftShare.EstimateDraftKind) + 1);
            var artifact = ArtifactRefs.Estimate(project, "estimate_draft", revision, checksumBody);
            var envelope = StudioDraftShare.BuildEnvelope(
                "estimate",
                StudioDraftShare.StateDraft,
                company.User,
                DateTimeOffset.UtcNow,
                artifact,
                StudioDraftShare.EstimateSummary(grandTotal, rateBookVersionName, ProjectStore.Current.Info.Name),
                estimatePayload);
            return StudioDraftShare.PutDraft(company, link.ProjectId, StudioDraftShare.EstimateDraftKind, envelope);
        });

    /// <summary>
    /// Shares a running-account bill into the company file as <c>aqc.ra.draft.&lt;bill_id&gt;</c> with state <c>draft</c>
    /// for Studio's inbox. Local Certify in CoreOp is interim only and is not the suite approval gate.
    /// </summary>
    public static Task<(long Revision, string ChecksumSha256)> ShareRaBillDraftAsync(string billId, JsonObject billPayload, double gross, double net, string? numberOrPreview, string? party) =>
        Task.Run(() =>
        {
            var company = Require();
            var link = ProjectStore.Current.Shared ?? throw new OfficeException(2, "Link this project to the company file before sharing a draft with Studio.");
            var kind = StudioDraftShare.RaDraftKind(billId);
            var project = ArtifactRefs.ProjectRef(company.Uuid, link.ProjectId);
            var checksumBody = billPayload.ToJsonString();
            var revision = (uint)Math.Max(1, company.DocumentRevision(link.ProjectId, kind) + 1);
            var artifact = ArtifactRefs.RunningBill(project, billId, revision, checksumBody);
            var envelope = StudioDraftShare.BuildEnvelope(
                "ra_bill",
                StudioDraftShare.StateDraft,
                company.User,
                DateTimeOffset.UtcNow,
                artifact,
                StudioDraftShare.RaBillSummary(billId, numberOrPreview ?? "", gross, net, party),
                billPayload);
            return StudioDraftShare.PutDraft(company, link.ProjectId, kind, envelope);
        });

    /// <summary>
    /// Opens a project from the company file into the app. When it has no document yet (it was made in O-Studio), the app starts a
    /// new project with its name, client and site filled in from the office's record.
    /// </summary>
    public static async Task<string> LoadIntoAppAsync(SharedProject project)
    {
        var document = await Task.Run(() => Require().GetDocument(project.Id));
        var store = ProjectStore.Current;
        if (document is null)
        {
            store.Reset();
            store.Info.Name = project.Name;
            store.Name = project.Name;
            store.Info.ClientName = project.ClientName;
            store.Info.Location = project.SiteAddress;
            store.Shared = new SharedProjectLink(project.Id, 0);
            await RefreshDetailsAsync();
            store.IsDirty = true;
            return $"{project.Name} is new here; it is saved to the company file when you save.";
        }
        if (JsonNode.Parse(document.Body) is not JsonObject root)
            throw new OfficeException(1, "The saved project could not be read.");
        store.LoadFrom(root);
        store.FilePath = null;
        store.Shared = new SharedProjectLink(project.Id, document.Revision);
        await RefreshDetailsAsync();
        store.IsDirty = false;
        return $"{project.Name}, revision {document.Revision}, last saved by {document.UpdatedBy}.";
    }

    /// <summary>
    /// Takes the company's details (kept in OSAS) and the project's details (kept in O-Studio) from the company file for the open project,
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
    /// Saves the open project to the company file at the revision it was loaded from. Throws <see cref="OfficeException"/> when another
    /// app has saved since; the message says to reload and nothing is overwritten.
    /// </summary>
    public static Task SaveCurrentAsync(long? toProjectId = null) =>
        Task.Run(() =>
        {
            var store = ProjectStore.Current;
            var link = store.Shared;
            var projectId = toProjectId ?? link?.ProjectId ?? throw new OfficeException(2, "This project is not linked to the company file.");
            var expected = toProjectId is null || link?.ProjectId == toProjectId ? link?.Revision ?? 0 : Require().DocumentRevision(projectId);
            var saved = Require().SaveDocument(projectId, expected, store.ToJson().ToJsonString());
            store.Shared = new SharedProjectLink(projectId, saved.Revision);
            store.IsDirty = false;
        });

    /// <summary>What a look at the company file found.</summary>
    public enum PollResult
    {
        /// <summary>Nothing the open project cares about changed.</summary>
        None,

        /// <summary>The other app saved the open project and this window had nothing unsaved, so it should reload it now.</summary>
        ReloadNow,

        /// <summary>The other app saved the open project, but this window has unsaved changes. The next save will refuse and offer a reload.</summary>
        ConflictPending,
    }

    /// <summary>
    /// Looks for a change made by the other app. It asks the engine only for the change counter and the revision of the open project's
    /// document, and does not count as activity. When the document has moved on: with nothing unsaved here the project should be reloaded
    /// (live sync); with unsaved changes the link is marked so a save refuses and asks for a reload.
    /// </summary>
    public static Task<PollResult> PollAsync() =>
        Task.Run(() =>
        {
            var company = _company;
            var link = ProjectStore.Current.Shared;
            if (company is null)
                return PollResult.None;
            var watch = company.Watch(link?.ProjectId);
            if (!watch.SchemaOk)
                throw new OfficeException(1, "Another app has upgraded this company file. Update O-CoreOp to keep working with it.");
            var changed = watch.Version != _lastDataVersion;
            _lastDataVersion = watch.Version;
            if (changed && link is not null)
            {
                // Someone changed the company file: OSAS may have changed the company's details or O-Studio the project's.
                try
                {
                    var store = ProjectStore.Current;
                    store.Info.ApplyCompanyFile(company.DocumentContext(link.ProjectId));
                    store.Parties.FollowCompanyFile(store.Info);
                    store.Name = store.Info.Name;
                }
                catch (OfficeException)
                {
                    // The project may have been archived or removed in O-Studio; the open document is left as it is.
                }
            }
            if (link is null || watch.Revision == link.Revision || watch.Revision == 0)
                return PollResult.None;
            if (!ProjectStore.Current.IsDirty)
                return PollResult.ReloadNow;
            if (link.ChangedElsewhere)
                return PollResult.None;
            link.ChangedElsewhere = true;
            return PollResult.ConflictPending;
        });

    /// <summary>Loads the open project again from the company file (what the other app saved), replacing what is in the window.</summary>
    public static async Task<string> ReloadOpenAsync()
    {
        var link = ProjectStore.Current.Shared ?? throw new OfficeException(2, "This project is not linked to the company file.");
        var document = await Task.Run(() => Require().GetDocument(link.ProjectId));
        if (document is null || JsonNode.Parse(document.Body) is not JsonObject root)
            throw new OfficeException(1, "The saved project could not be read.");
        var store = ProjectStore.Current;
        store.LoadFrom(root);
        store.FilePath = null;
        store.Shared = new SharedProjectLink(link.ProjectId, document.Revision);
        await RefreshDetailsAsync();
        store.IsDirty = false;
        return $"{store.Name}, revision {document.Revision}, saved by {document.UpdatedBy}.";
    }

    static SharedCompany Require() => _company ?? throw new OfficeException(2, "No company file is open.");
}
