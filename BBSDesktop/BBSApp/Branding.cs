// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace BBSApp;

/// <summary>
/// Product branding — O-CoreOp, in OSAS the Open Source Architecture Suit (Human Centric Works). Formerly AQC Core.
/// Keep display strings aligned with OSAS product nomenclature.
/// </summary>
public static class Branding
{
    /// <summary>Window · tile · about — suite style (space, not hyphen).</summary>
    public const string AppName = "O-CoreOp";

    /// <summary>Legacy hyphenated name (files · older docs).</summary>
    public const string LegacyAppName = "AQC-Core";

    /// <summary>Short mark for dense chrome.</summary>
    public const string ShortName = "O-CoreOp";

    public const string FullName = "Quantities, estimates and billing";
    public const string SuiteLine = "OSAS · Open Source Architecture Suit";
    public const string Tagline =
        "Estimation · BBS · BOQ — local quantities, estimates and billing";
    public const string HeroSupport =
        "RCC BBS, civil BOQ, shuttering, estimate & PDF drawing takeoff";

    public const string Company = "Human Centric Works";
    public const string CompanyShort = "HCW";
    public const string Location = "Hospet";
    public const string CompanyLine = "Human Centric Works · Hospet";
    public const string DevelopedBy = "Developed by Human Centric Works · Hospet";
    public const string Copyright = "© Human Centric Works · Hospet";
    public const string LicenseName = "GNU AGPL v3 (Community) or Commercial";
    public const string LicenseUrl = "https://www.gnu.org/licenses/agpl-3.0.html";
    public const string SuiteUrl = "https://github.com/holagundiworks/osas-open_source_architecture_suit";

    /// <summary>%LocalAppData% folder. Earlier versions used AQCCore; the first run copies that folder here and leaves it in place.</summary>
    public const string AppDataFolder = "O-CoreOp";
    public const string PreviousAppDataFolder = "AQCCore";
    public const string LegacyAppDataFolder = "BOQCore";

    public const string ProjectFileFilter = "O-Suite project";
    public const string ProjectExtension = ".osp";

    public static string WindowTitle(string? projectName = null, bool dirty = false)
    {
        string baseTitle = string.IsNullOrWhiteSpace(projectName)
            ? AppName
            : $"{AppName} — {projectName}";
        return dirty ? $"{baseTitle} •" : baseTitle;
    }

    public static string AppDataDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            MigrateAppData(root);
            var dir = Path.Combine(root, AppDataFolder);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Copies the settings folder of earlier versions (AQCCore) to the current one the first time, without overwriting anything.
    /// The old folder stays, so going back to an earlier version still finds its data.
    /// </summary>
    public static void MigrateAppData(string localAppData)
    {
        var current = Path.Combine(localAppData, AppDataFolder);
        var previous = Path.Combine(localAppData, PreviousAppDataFolder);
        try
        {
            if (!Directory.Exists(current) && Directory.Exists(previous))
                CopyFolder(previous, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A half-copied folder would look like missing data; remove it so the next start tries again.
            try
            {
                if (Directory.Exists(current))
                    Directory.Delete(current, true);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    public static string ResolveAppDataFile(string fileName)
    {
        var primary = Path.Combine(AppDataDirectory, fileName);
        if (File.Exists(primary)) return primary;

        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyAppDataFolder, fileName);
        return File.Exists(legacy) ? legacy : primary;
    }

    public static string ResolveAppDataSubdir(string subdir)
    {
        var primary = Path.Combine(AppDataDirectory, subdir);
        Directory.CreateDirectory(primary);
        return primary;
    }
}
