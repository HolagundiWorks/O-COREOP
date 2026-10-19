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
/// The company file shared with O-Studio (an .adms file). While one is open, a project can be opened from it and saved back to it,
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

    public static void Close()
    {
        var company = _company;
        _company = null;
        company?.Dispose();
        Changed?.Invoke();
    }

    public static Task<IReadOnlyList<SharedProject>> ProjectsAsync() => Task.Run(() => Require().Projects());

    public static Task<IReadOnlyList<SharedClient>> ClientsAsync() => Task.Run(() => Require().Clients());

    public static Task<long> CreateProjectAsync(string name, long? clientId, string newClientName, string site) =>
        Task.Run(() =>
        {
            var company = Require();
            var client = clientId ?? company.CreateClient(newClientName);
            return company.CreateProject(name, client, site);
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
            store.IsDirty = true;
            return $"{project.Name} is new here; it is saved to the company file when you save.";
        }
        if (JsonNode.Parse(document.Body) is not JsonObject root)
            throw new OfficeException(1, "The saved project could not be read.");
        store.LoadFrom(root);
        store.FilePath = null;
        store.Shared = new SharedProjectLink(project.Id, document.Revision);
        store.IsDirty = false;
        return $"{project.Name}, revision {document.Revision}, last saved by {document.UpdatedBy}.";
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
            _lastDataVersion = watch.Version;
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
        store.IsDirty = false;
        return $"{store.Name}, revision {document.Revision}, saved by {document.UpdatedBy}.";
    }

    static SharedCompany Require() => _company ?? throw new OfficeException(2, "No company file is open.");
}
