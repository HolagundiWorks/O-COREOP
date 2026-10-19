// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Office.Shared;
using Xunit;

namespace Office.Shared.Tests;

/// <summary>
/// These talk to the real office engine. They need office.dll (built from office-ffi in the O-Studio repository with
/// `cargo build --release`). Point OFFICE_DLL_DIR at the folder that holds it; without it the tests do nothing and say so.
/// </summary>
public class SharedCompanyTests : IDisposable
{
    private const string Password = "correct horse battery";
    private static readonly bool Available = Locate();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "office-shared-" + Guid.NewGuid().ToString("N")[..8]);

    public SharedCompanyTests() => Directory.CreateDirectory(_dir);

    private static bool Locate()
    {
        var folder = Environment.GetEnvironmentVariable("OFFICE_DLL_DIR");
        if (!string.IsNullOrEmpty(folder))
            OfficeSession.UseLibraryFolder(folder);
        return OfficeSession.IsAvailable();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string NewCompany()
    {
        var path = Path.Combine(_dir, "Studio");
        using var session = OfficeSession.Create(Path.Combine(_dir, "app-x"));
        session.Call("company.create", new JsonObject { ["path"] = path, ["name"] = "Studio One", ["username"] = "owner", ["password"] = Password, ["confirm"] = Password });
        session.Call("company.close");
        return path + ".adms";
    }

    private SharedCompany Open(string path, string app) => SharedCompany.Open(Path.Combine(_dir, app), path, "owner", Password);

    [Fact]
    public void The_engine_is_found_when_its_folder_is_given()
    {
        // Guards the tests below: with OFFICE_DLL_DIR set they must run against the real engine, not quietly do nothing.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OFFICE_DLL_DIR")))
            Assert.True(Available, "office.dll was not found in OFFICE_DLL_DIR");
    }

    [Fact]
    public void A_project_made_in_one_app_is_listed_in_the_other_while_both_have_the_company_open()
    {
        if (!Available) return;
        var path = NewCompany();
        using var studio = Open(path, "app-studio");
        using var coreop = Open(path, "app-coreop");
        Assert.Equal(studio.Uuid, coreop.Uuid);
        var client = studio.CreateClient("Acme");
        var project = studio.CreateProject("Villa", client, "Hospet");
        var seen = coreop.Projects().Single();
        Assert.Equal((project, "Villa", "Acme", "Hospet"), (seen.Id, seen.Name, seen.ClientName, seen.SiteAddress));
    }

    [Fact]
    public void A_project_document_is_saved_by_revision_and_a_stale_save_is_refused()
    {
        if (!Available) return;
        var path = NewCompany();
        using var studio = Open(path, "app-studio");
        using var coreop = Open(path, "app-coreop");
        var project = studio.CreateProject("Villa", studio.CreateClient("Acme"));
        Assert.Null(coreop.GetDocument(project));
        var first = coreop.SaveDocument(project, 0, "{\"levels\":2}");
        Assert.Equal(1, first.Revision);
        var loaded = coreop.GetDocument(project)!;
        Assert.Equal((1L, "{\"levels\":2}"), (loaded.Revision, loaded.Body));
        Assert.Equal(first.ChecksumSha256, loaded.ChecksumSha256);
        // a second window of the same app (or another app) saves on top of revision 1
        Assert.Equal(2, studio.SaveDocument(project, 1, "{\"levels\":3}").Revision);
        var stale = Assert.Throws<OfficeException>(() => coreop.SaveDocument(project, 1, "{\"levels\":9}"));
        Assert.Contains("changed in another app", stale.Message);
        Assert.Equal("{\"levels\":3}", coreop.GetDocument(project)!.Body);
    }

    [Fact]
    public void The_change_counter_moves_when_the_other_app_writes_and_a_reference_names_the_project()
    {
        if (!Available) return;
        var path = NewCompany();
        using var studio = Open(path, "app-studio");
        using var coreop = Open(path, "app-coreop");
        var before = coreop.DataVersion();
        var project = studio.CreateProject("Villa", studio.CreateClient("Acme"));
        Assert.NotEqual(before, coreop.DataVersion());
        Assert.True(coreop.SchemaOk());
        var reference = JsonNode.Parse(coreop.ProjectRef(project))!;
        Assert.Equal(project, reference["project_id"]!.GetValue<long>());
        Assert.Equal(studio.Uuid, reference["company_uuid"]!.GetValue<string>());
    }

    [Fact]
    public void A_wrong_password_is_refused_with_a_message()
    {
        if (!Available) return;
        var path = NewCompany();
        var error = Assert.Throws<OfficeException>(() => SharedCompany.Open(Path.Combine(_dir, "app-bad"), path, "owner", "not the password at all"));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }
}
