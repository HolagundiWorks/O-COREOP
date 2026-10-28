// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Office.Shared;
using Xunit;

namespace Office.Shared.Tests;

public class ArtifactRefsTests
{
    [Fact]
    public void Project_ref_names_company_and_project_only()
    {
        var r = ArtifactRefs.ProjectRef("uuid-a", 7);
        Assert.Equal(1u, r["v"]!.GetValue<uint>());
        Assert.Equal("uuid-a", r["company_uuid"]!.GetValue<string>());
        Assert.Equal(7L, r["project_id"]!.GetValue<long>());
        Assert.Null(r["aorms_project_office_id"]);
    }

    [Fact]
    public void Estimate_artifact_checksum_is_lower_case_hex_and_stable()
    {
        var project = ArtifactRefs.ProjectRef("uuid-a", 7);
        var body = new JsonObject { ["grand_total"] = 1234.5, ["rate_book_version_id"] = "rb-1" };
        var checksumBody = ArtifactRefs.EstimateChecksumBody(body);
        var a = ArtifactRefs.Estimate(project, "last_estimate", 2, checksumBody);
        Assert.Equal("estimate", a["kind"]!.GetValue<string>());
        Assert.Equal("last_estimate", a["id"]!.GetValue<string>());
        Assert.Equal(2u, a["revision"]!.GetValue<uint>());
        var hex = a["checksum_sha256"]!.GetValue<string>()!;
        Assert.Equal(64, hex.Length);
        Assert.Equal(hex, ArtifactRefs.Sha256Hex(checksumBody));
        Assert.Matches("^[0-9a-f]{64}$", hex);
        Assert.Equal(project.ToJsonString(), a["project"]!.ToJsonString());
    }

    [Fact]
    public void Estimate_checksum_body_strips_artifact_ref()
    {
        var body = new JsonObject
        {
            ["grand_total"] = 1,
            ["artifact_ref"] = new JsonObject { ["id"] = "x" },
        };
        Assert.DoesNotContain("artifact_ref", ArtifactRefs.EstimateChecksumBody(body));
    }

    [Fact]
    public void Running_bill_ArtifactRef_uses_ra_bill_kind()
    {
        var project = ArtifactRefs.ProjectRef("uuid-a", 7);
        var a = ArtifactRefs.RunningBill(project, "bill1", 1, "{\"id\":\"bill1\"}");
        Assert.Equal("ra_bill", a["kind"]!.GetValue<string>());
        Assert.Equal("bill1", a["id"]!.GetValue<string>());
        Assert.Equal(1u, a["revision"]!.GetValue<uint>());
    }
}
