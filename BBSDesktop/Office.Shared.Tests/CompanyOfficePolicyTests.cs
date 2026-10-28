// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Office.Shared;
using Xunit;

namespace Office.Shared.Tests;

public class CompanyOfficePolicyTests
{
    [Fact]
    public void Null_company_cannot_create_clients_or_projects()
    {
        Assert.False(CompanyOfficePolicy.AllowsClientProjectCreate(null));
    }

    [Fact]
    public void Suite_linked_session_cannot_create_clients_or_projects()
    {
        Assert.False(CompanyOfficePolicy.AllowsClientProjectCreate(companyOpen: true, suiteLinked: true));
        Assert.True(CompanyOfficePolicy.AllowsClientProjectCreate(companyOpen: true, suiteLinked: false));
        Assert.False(CompanyOfficePolicy.AllowsClientProjectCreate(companyOpen: false, suiteLinked: false));
    }

    [Fact]
    public void Require_create_throws_the_use_Studio_message_when_suite_linked()
    {
        var ex = Assert.Throws<OfficeException>(() => CompanyOfficePolicy.RequireClientProjectCreate(suiteLinked: true));
        Assert.Equal(2, ex.Status);
        Assert.Equal(CompanyOfficePolicy.UseStudioForClientsProjects, ex.Message);
        CompanyOfficePolicy.RequireClientProjectCreate(suiteLinked: false);
    }

    [Fact]
    public void Use_Studio_message_is_stable_for_UI()
    {
        Assert.Contains("Studio", CompanyOfficePolicy.UseStudioForClientsProjects, StringComparison.Ordinal);
        Assert.Contains("Studio", CompanyOfficePolicy.UseStudioForCompanyDetails, StringComparison.Ordinal);
    }
}
