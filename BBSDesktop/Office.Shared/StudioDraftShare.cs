// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Text.Json.Nodes;

namespace Office.Shared;

/// <summary>
/// Builds company-file documents that CoreOp shares into the Studio / company inbox as drafts.
/// Studio lists these kinds, shows them for approval, then locks or requests a revised version.
/// Contract: <c>docs/STUDIO-DRAFT-SHARE.md</c> in this repository.
/// </summary>
public static class StudioDraftShare
{
    public const uint SchemaVersion = 1;

    /// <summary>Document kind for the latest estimate draft shared for a project.</summary>
    public const string EstimateDraftKind = "aqc.estimate.draft";

    /// <summary>Prefix for a running-account bill draft; full kind is <c>aqc.ra.draft.&lt;bill_id&gt;</c>.</summary>
    public const string RaDraftKindPrefix = "aqc.ra.draft.";

    public const string StateDraft = "draft";

    public static string RaDraftKind(string billId)
    {
        if (string.IsNullOrWhiteSpace(billId))
            throw new ArgumentException("A running-account draft needs a bill id.", nameof(billId));
        var id = billId.Trim();
        if (id.Length > 64 || id.Contains('.', StringComparison.Ordinal) || id.Contains('/', StringComparison.Ordinal))
            throw new ArgumentException("Bill id must be 1–64 characters without '.' or '/'.", nameof(billId));
        return RaDraftKindPrefix + id;
    }

    public static bool IsRaDraftKind(string kind) =>
        kind.StartsWith(RaDraftKindPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Envelope Studio reads from <c>doc_get</c>. <paramref name="payload"/> is the estimate or bill body;
    /// <paramref name="summary"/> is a short list of totals for the inbox without parsing the full body.
    /// </summary>
    public static JsonObject BuildEnvelope(
        string artifactKind,
        string state,
        string sharedBy,
        DateTimeOffset sharedAt,
        JsonObject? artifactRef,
        JsonObject summary,
        JsonObject payload)
    {
        if (artifactKind is not ("estimate" or "ra_bill"))
            throw new ArgumentException("artifactKind must be estimate or ra_bill.", nameof(artifactKind));
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("state is required.", nameof(state));

        var envelope = new JsonObject
        {
            ["v"] = SchemaVersion,
            ["state"] = state.Trim(),
            ["artifact_kind"] = artifactKind,
            ["shared_at"] = sharedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            ["shared_by"] = sharedBy?.Trim() ?? "",
            ["summary"] = summary.DeepClone(),
            ["body"] = payload.DeepClone(),
        };
        if (artifactRef is not null)
            envelope["artifact_ref"] = artifactRef.DeepClone();
        return envelope;
    }

    public static JsonObject EstimateSummary(double grandTotal, string? rateBookVersionName, string? title = null) =>
        new()
        {
            ["title"] = title ?? "Estimate",
            ["currency"] = "INR",
            ["grand_total"] = grandTotal,
            ["rate_book_version_name"] = rateBookVersionName ?? "",
        };

    public static JsonObject RaBillSummary(string billId, string numberOrPreview, double gross, double net, string? party = null) =>
        new()
        {
            ["title"] = string.IsNullOrWhiteSpace(numberOrPreview) ? "RA bill" : numberOrPreview.Trim(),
            ["bill_id"] = billId,
            ["currency"] = "INR",
            ["gross"] = gross,
            ["net"] = net,
            ["party"] = party ?? "",
        };

    /// <summary>
    /// Writes the draft via <c>doc_put</c>. Uses revision 0 when none exists yet; otherwise the current revision
    /// so a re-share replaces the previous draft of the same kind.
    /// </summary>
    public static (long Revision, string ChecksumSha256) PutDraft(
        SharedCompany company,
        long projectId,
        string documentKind,
        JsonObject envelope)
    {
        var expected = company.DocumentRevision(projectId, documentKind);
        return company.SaveDocument(projectId, expected, envelope.ToJsonString(), documentKind);
    }
}
