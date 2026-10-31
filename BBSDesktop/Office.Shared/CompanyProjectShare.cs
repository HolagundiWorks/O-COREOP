// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;

namespace Office.Shared;

/// <summary>
/// Mirrors O-CoreOp totals into the company file as <see cref="SharedCompany.DocumentKind"/> so O-Studio can read
/// BOQ, estimate and running-bill abstracts without opening the <c>.osp</c>. Full quantities stay in the project file.
/// </summary>
public static class CompanyProjectShare
{
    /// <summary>
    /// Builds the JSON body for <c>aqc.project</c>: <c>project_ref</c>, <c>last_estimate</c>, and <c>accounts.bills</c>
    /// (the fields <c>project_*_summary</c> in O-Studio's engine read).
    /// </summary>
    public static JsonObject BuildSummaryBody(JsonObject projectRoot)
    {
        var body = new JsonObject();
        if (projectRoot["project_ref"] is JsonNode projectRef)
            body["project_ref"] = projectRef.DeepClone();
        if (projectRoot["last_estimate"] is JsonNode estimate)
            body["last_estimate"] = estimate.DeepClone();
        if (projectRoot["accounts"] is JsonObject accounts && accounts["bills"] is JsonNode bills)
            body["accounts"] = new JsonObject { ["bills"] = bills.DeepClone() };
        else
            body["accounts"] = new JsonObject { ["bills"] = new JsonArray() };
        return body;
    }

    static readonly HashSet<string> AllowedSummaryKeys = new(StringComparer.Ordinal)
    {
        "project_ref", "last_estimate", "last_estimate_rate_book_version_id", "accounts",
    };

    /// <summary>Client-side check before push; server policy in <c>validate_body_for_put</c> is authoritative.</summary>
    public static string? ValidateSummaryBody(JsonObject body)
    {
        foreach (var key in body)
        {
            if (!AllowedSummaryKeys.Contains(key.Key))
                return $"Summary documents cannot include \"{key.Key}\" (full project data stays in the .osp file).";
        }
        if (body["accounts"] is JsonObject accounts)
        {
            foreach (var key in accounts)
            {
                if (key.Key != "bills")
                    return "The accounts object may only contain bills.";
            }
        }
        return null;
    }

    /// <summary>
    /// Writes the summary to the company file. Updates <paramref name="linkRevision"/> on success.
    /// Returns null on success, or a user-visible message when the write was skipped or refused.
    /// </summary>
    public static string? TryPushSummary(SharedCompany company, long projectId, ref long linkRevision, JsonObject projectRoot)
    {
        var summary = BuildSummaryBody(projectRoot);
        if (ValidateSummaryBody(summary) is { } clientErr)
            return clientErr;
        var body = summary.ToJsonString();
        try
        {
            var (revision, _) = company.SaveDocument(projectId, linkRevision, body);
            linkRevision = revision;
            return null;
        }
        catch (OfficeException ex)
        {
            var remote = company.DocumentRevision(projectId);
            if (remote > linkRevision)
                linkRevision = remote;
            return ex.Message;
        }
    }
}
