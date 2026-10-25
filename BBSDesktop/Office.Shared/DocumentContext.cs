// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace Office.Shared;

/// <summary>The company's own details, as OSAS holds them. Printed on every document; no app keeps its own copy.</summary>
public sealed record CompanyDetails(
    string Name,
    string Address,
    string Email,
    string Phone,
    string Pan,
    string Gstin,
    string CoaRegistration,
    string ArchitectName,
    string LetterheadStyle,
    string AccentHex);

/// <summary>A project's details, as O-Studio holds them: the project, its client, and the service in words.</summary>
public sealed record ProjectDetails(long Id, string Code, string Name, string SiteAddress, string Summary, string ClientName, string Consultancy, string Status, string ProjectRef);

/// <summary>
/// What a document needs, read in one call: the company's details (kept in OSAS), today's date, and, when a project was named,
/// that project's details (kept in O-Studio). O-CoreOp reports and O-Draft title plates print from this and never save it back.
/// </summary>
public sealed record DocumentContext(CompanyDetails Company, string PreparedOn, ProjectDetails? Project);
