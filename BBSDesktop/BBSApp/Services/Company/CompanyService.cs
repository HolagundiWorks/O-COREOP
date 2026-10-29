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
    /// Tie the open project to a Studio project (the take-off header names it) and take its details from the company file, read only. False when
    /// the company file is not open or is another company's.
    /// </summary>
    public static async Task<bool> LinkProjectAsync(string? companyUuid, long projectId)
    {
        var company = _company;
        if (company is null || string.IsNullOrEmpty(companyUuid) || company.Uuid != companyUuid) return false;
        var store = ProjectStore.Current;
        if (store.Shared?.ProjectId == projectId) return true;
        store.Shared = new SharedProjectLink(projectId, 0);
        await RefreshDetailsAsync();
        return true;
    }
}
