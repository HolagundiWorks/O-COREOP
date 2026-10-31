// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Office.Shared;
using Xunit;

namespace Office.Shared.Tests;

public class CompanyProjectShareTests
{
    [Fact]
    public void Summary_body_keeps_ref_estimate_and_bills_only()
    {
        var root = new JsonObject
        {
            ["name"] = "Villa",
            ["walls"] = new JsonArray(),
            ["project_ref"] = ArtifactRefs.ProjectRef("u", 7),
            ["last_estimate"] = new JsonObject { ["grand_total"] = 100.0, ["base_total"] = 80.0 },
            ["accounts"] = new JsonObject
            {
                ["bills"] = new JsonArray { new JsonObject { ["number"] = "RA-1" } },
                ["transactions"] = new JsonArray { new JsonObject { ["amount"] = 1.0 } },
            },
        };
        var summary = CompanyProjectShare.BuildSummaryBody(root);
        Assert.NotNull(summary["project_ref"]);
        Assert.Equal(100.0, summary["last_estimate"]!["grand_total"]!.GetValue<double>());
        Assert.Single(summary["accounts"]!["bills"]!.AsArray());
        Assert.Null(summary["walls"]);
        Assert.Null(summary["accounts"]!["transactions"]);
    }
}
