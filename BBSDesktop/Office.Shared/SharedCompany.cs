// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;

namespace Office.Shared;

public sealed record SharedClient(long Id, string Name);

public sealed record SharedProject(long Id, string Code, string Name, long ClientId, string ClientName, string SiteAddress, string Status);

/// <summary>A document an app keeps for a project in the company file, with the revision it was saved at.</summary>
public sealed record SharedDocument(long Revision, string Body, string ChecksumSha256, string UpdatedBy, string UpdatedAt);

/// <summary>
/// The office company file that O-Studio and O-CoreOp share. Clients and projects are the office's records; O-CoreOp keeps each
/// project's quantities and estimates as a document on that project (kind <see cref="DocumentKind"/>), saved by revision so a save
/// that would overwrite someone else's is refused.
/// </summary>
public sealed class SharedCompany : IDisposable
{
    /// <summary>The kind O-CoreOp uses for a project's own data: the same JSON a project file holds.</summary>
    public const string DocumentKind = "aqc.project";

    private readonly OfficeSession _session;

    private SharedCompany(OfficeSession session, string name, string uuid, string path, string user)
    {
        _session = session;
        Name = name;
        Uuid = uuid;
        Path = path;
        User = user;
    }

    public string Name { get; }

    public string Uuid { get; }

    public string Path { get; }

    public string User { get; }

    /// <summary>
    /// Opens a company file and signs in. <paramref name="secret"/> and <paramref name="unlock"/> are only for a protected file
    /// ("password" or "recovery"). Throws <see cref="OfficeException"/> with a message to show when it cannot be opened.
    /// </summary>
    public static SharedCompany Open(string appDir, string path, string user, string password, string secret = "", string unlock = "")
    {
        var session = OfficeSession.Create(appDir);
        try
        {
            var company = session.Call("company.open", new JsonObject { ["path"] = path, ["secret"] = secret, ["unlock"] = unlock });
            session.Call("auth.login", new JsonObject { ["username"] = user, ["password"] = password });
            return new SharedCompany(session, company?["name"]?.GetValue<string>() ?? "", company?["uuid"]?.GetValue<string>() ?? "", path, user);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a company file and signs in with a one-time ticket from the suite app (OSAS) instead of a password. The ticket works once, for
    /// two minutes, and signs in the person who was signed in there.
    /// </summary>
    public static SharedCompany OpenWithTicket(string appDir, string path, string ticket)
    {
        var session = OfficeSession.Create(appDir);
        try
        {
            var company = session.Call("company.open", new JsonObject { ["path"] = path, ["secret"] = "", ["unlock"] = "" });
            var who = session.Call("auth.login_ticket", new JsonObject { ["ticket"] = ticket });
            return new SharedCompany(session, company?["name"]?.GetValue<string>() ?? "", company?["uuid"]?.GetValue<string>() ?? "", path, who?["username"]?.GetValue<string>() ?? "");
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public IReadOnlyList<SharedClient> Clients() =>
        Array(_session.Call("client_list", new JsonObject { ["include_archived"] = false })).Select(c => new SharedClient(Long(c, "id"), Text(c, "name"))).ToList();

    public long CreateClient(string name) =>
        _session.Call("client_save", new JsonObject
        {
            ["client"] = new JsonObject { ["id"] = null, ["name"] = name, ["contact_person"] = "", ["email"] = "", ["phone"] = "", ["address"] = "", ["pan"] = "", ["gstin"] = "" },
        })!.GetValue<long>();

    public IReadOnlyList<SharedProject> Projects(bool includeArchived = false) =>
        Array(_session.Call("project_list", new JsonObject { ["client_id"] = null, ["include_archived"] = includeArchived }))
            .Select(p => new SharedProject(Long(p, "id"), Text(p, "code"), Text(p, "name"), Long(p, "client_id"), Text(p, "client_name"), Text(p, "site_address"), Text(p, "status")))
            .ToList();

    public long CreateProject(string name, long clientId, string siteAddress = "") =>
        _session.Call("project_save", new JsonObject
        {
            ["project"] = new JsonObject { ["id"] = null, ["client_id"] = clientId, ["code"] = "", ["name"] = name, ["site_address"] = siteAddress, ["status"] = "active" },
        })!.GetValue<long>();

    /// <summary>The project's document of this kind, or null when it has none yet.</summary>
    public SharedDocument? GetDocument(long projectId, string kind = DocumentKind)
    {
        var d = _session.Call("doc_get", new JsonObject { ["project_id"] = projectId, ["kind"] = kind });
        return d is null ? null : new SharedDocument(Long(d, "revision"), Text(d, "body"), Text(d, "checksum_sha256"), Text(d, "updated_by"), Text(d, "updated_at"));
    }

    /// <summary>
    /// Saves the document. <paramref name="expectedRevision"/> is the revision that was loaded (0 for a document never saved). When
    /// another app has saved since, the engine refuses with a message to reload, and nothing is written.
    /// </summary>
    public (long Revision, string ChecksumSha256) SaveDocument(long projectId, long expectedRevision, string body, string kind = DocumentKind)
    {
        var r = _session.Call("doc_put", new JsonObject { ["project_id"] = projectId, ["kind"] = kind, ["expected_revision"] = expectedRevision, ["body"] = body })!;
        return (Long(r, "revision"), Text(r, "checksum_sha256"));
    }

    /// <summary>The revision the project's document is at now, or 0 when it has none. Cheap: it does not read the document.</summary>
    public long DocumentRevision(long projectId, string kind = DocumentKind)
    {
        var list = _session.Call("doc_list", new JsonObject { ["project_id"] = projectId });
        return Array(list).Where(d => Text(d, "kind") == kind).Select(d => Long(d, "revision")).FirstOrDefault();
    }

    /// <summary>
    /// What to poll: the change counter, whether this build still understands the file, and the revision of the project's document now
    /// (0 when there is none or no project is given). It does not count as activity, so polling never keeps an idle session from locking.
    /// </summary>
    public (long Version, bool SchemaOk, long Revision) Watch(long? projectId)
    {
        var w = _session.Call("watch", new JsonObject { ["project_id"] = projectId, ["kind"] = DocumentKind })!;
        return (Long(w, "version"), w["schema_ok"]?.GetValue<bool>() ?? false, Long(w, "revision"));
    }

    /// <summary>A number that goes up whenever another app writes to the company file. Poll it to know when to look again.</summary>
    public long DataVersion() => Long(_session.Call("data_version")!, "version");

    /// <summary>False when another app has upgraded the file past what this build understands; the app should stop and ask for an update.</summary>
    public bool SchemaOk() => _session.Call("data_version")!["schema_ok"]!.GetValue<bool>();

    /// <summary>
    /// The company's details and, when <paramref name="projectId"/> is given, that project's details, in one read. This is what a document
    /// is printed from; the company's details are changed in OSAS and a project's in O-Studio, never here.
    /// </summary>
    public DocumentContext DocumentContext(long? projectId)
    {
        var c = _session.Call("document_context", new JsonObject { ["project_id"] = projectId })!;
        var co = c["company"]!;
        var company = new CompanyDetails(Text(co, "name"), Text(co, "address"), Text(co, "email"), Text(co, "phone"), Text(co, "pan"), Text(co, "gstin"),
            Text(co, "coa_registration"), Text(co, "architect_name"), Text(co, "letterhead_style"), Text(co, "accent_hex"));
        ProjectDetails? project = null;
        if (c["project"] is JsonNode p && p["project"] is JsonNode pr)
            project = new ProjectDetails(Long(pr, "id"), Text(pr, "code"), Text(pr, "name"), Text(pr, "site_address"), Text(pr, "client_name"),
                Text(p, "consultancy_label"), Text(pr, "status"), p["project_ref"]?.ToJsonString() ?? "");
        return new DocumentContext(company, Text(c, "prepared_on"), project);
    }

    /// <summary>The suite's reference to a project (docs/contracts/refs.md in the O-Studio repository), as JSON text.</summary>
    public string ProjectRef(long projectId) =>
        _session.Call("project_ref", new JsonObject { ["project_id"] = projectId })!.ToJsonString();

    public void Dispose()
    {
        try
        {
            _session.Call("company.close");
        }
        catch (OfficeException)
        {
            // Destroying the session below closes the file in any case.
        }
        _session.Dispose();
    }

    private static IEnumerable<JsonNode> Array(JsonNode? node) =>
        node is JsonArray array ? array.Where(n => n is not null).Select(n => n!) : Enumerable.Empty<JsonNode>();

    private static long Long(JsonNode node, string key) => node[key]?.GetValue<long>() ?? 0;

    private static string Text(JsonNode node, string key) => node[key]?.GetValue<string>() ?? "";
}
