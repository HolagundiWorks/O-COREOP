// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Office.Shared;

/// <summary>
/// Builds suite <c>ProjectRef</c> / <c>ArtifactRef</c> JSON (O-Studio <c>docs/contracts/refs.md</c>).
/// The company file is the system of record: identity is <c>company_uuid</c> + <c>project_id</c> only.
/// </summary>
public static class ArtifactRefs
{
    public const uint Version = 1;

    public static JsonObject ProjectRef(string companyUuid, long projectId)
    {
        if (string.IsNullOrWhiteSpace(companyUuid))
            throw new ArgumentException("A ProjectRef needs the company file id.", nameof(companyUuid));
        if (projectId <= 0)
            throw new ArgumentException("A ProjectRef needs a project id.", nameof(projectId));
        return new JsonObject
        {
            ["v"] = Version,
            ["company_uuid"] = companyUuid.Trim(),
            ["project_id"] = projectId,
        };
    }

    /// <summary>Lower-case hex SHA-256 of UTF-8 content (contract spelling).</summary>
    public static string Sha256Hex(string utf8)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(utf8));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// An estimate ArtifactRef. <paramref name="checksumContent"/> is the maker's canonical body for this revision
    /// (not including the ArtifactRef itself).
    /// </summary>
    public static JsonObject Estimate(JsonObject projectRef, string id, uint revision, string checksumContent) =>
        Artifact(projectRef, "estimate", id, revision, checksumContent);

    /// <summary>A running-account bill ArtifactRef (draft share into Studio).</summary>
    public static JsonObject RunningBill(JsonObject projectRef, string id, uint revision, string checksumContent) =>
        Artifact(projectRef, "ra_bill", id, revision, checksumContent);

    static JsonObject Artifact(JsonObject projectRef, string kind, string id, uint revision, string checksumContent)
    {
        if (projectRef["v"]?.GetValue<uint>() != Version
            || string.IsNullOrWhiteSpace(projectRef["company_uuid"]?.GetValue<string>())
            || projectRef["project_id"] is null)
            throw new ArgumentException("A well-formed ProjectRef is required.", nameof(projectRef));
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200)
            throw new ArgumentException("An artifact needs an id of 1 to 200 characters.", nameof(id));
        if (revision == 0)
            throw new ArgumentException("Revisions start at 1.", nameof(revision));
        return new JsonObject
        {
            ["v"] = Version,
            ["project"] = projectRef.DeepClone(),
            ["kind"] = kind,
            ["id"] = id.Trim(),
            ["revision"] = revision,
            ["checksum_sha256"] = Sha256Hex(checksumContent),
            ["made_from"] = new JsonArray(),
        };
    }

    /// <summary>Stable checksum input for an estimate snapshot (totals + rate book + line amounts).</summary>
    public static string EstimateChecksumBody(JsonObject estimateWithoutArtifactRef)
    {
        var clone = estimateWithoutArtifactRef.DeepClone()!.AsObject();
        clone.Remove("artifact_ref");
        return clone.ToJsonString();
    }
}
