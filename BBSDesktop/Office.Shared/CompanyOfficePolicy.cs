// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace Office.Shared;

/// <summary>
/// When CoreOp is opened from the suite (`OSTUDIO_COMPANY` + `OSTUDIO_TICKET`), Studio owns company and
/// office-record identity. CoreOp may still save project quantity documents (<c>aqc.project</c>)
/// and share estimate/RA drafts; it must not create or edit clients/projects in the company file.
/// </summary>
public static class CompanyOfficePolicy
{
    public const string UseStudioForClientsProjects =
        "Create clients and projects in Studio. O-CoreOp cannot change those office records when opened from the suite.";

    public const string UseStudioForCompanyDetails =
        "Company and project identity are kept in Studio. Open Studio to change them.";

    /// <summary>True when a suite-linked session is open — Studio owns clients/projects.</summary>
    public static bool AllowsClientProjectCreate(bool companyOpen, bool suiteLinked) =>
        companyOpen && !suiteLinked;

    /// <summary>True when this session may create clients/projects in the company file (tests/tools only).</summary>
    public static bool AllowsClientProjectCreate(SharedCompany? company) =>
        company is not null && AllowsClientProjectCreate(true, company.SuiteLinked);

    /// <summary>Throws <see cref="OfficeException"/> when suite-linked CoreOp must not mutate office records.</summary>
    public static void RequireClientProjectCreate(bool suiteLinked)
    {
        if (suiteLinked)
            throw new OfficeException(2, UseStudioForClientsProjects);
    }

    /// <summary>Throws <see cref="OfficeException"/> when suite-linked CoreOp must not mutate office records.</summary>
    public static void RequireClientProjectCreate(SharedCompany company) =>
        RequireClientProjectCreate(company.SuiteLinked);
}
