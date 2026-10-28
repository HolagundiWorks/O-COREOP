// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Office.Shared;
using Xunit;

namespace Office.Shared.Tests;

public class StudioDraftShareTests
{
    [Fact]
    public void Estimate_draft_kind_and_ra_kind_are_stable_for_Studio()
    {
        Assert.Equal("aqc.estimate.draft", StudioDraftShare.EstimateDraftKind);
        Assert.Equal("aqc.ra.draft.abc123", StudioDraftShare.RaDraftKind("abc123"));
        Assert.True(StudioDraftShare.IsRaDraftKind("aqc.ra.draft.abc123"));
        Assert.False(StudioDraftShare.IsRaDraftKind("aqc.estimate.draft"));
    }

    [Fact]
    public void Ra_draft_kind_rejects_empty_or_path_like_ids()
    {
        Assert.Throws<ArgumentException>(() => StudioDraftShare.RaDraftKind(""));
        Assert.Throws<ArgumentException>(() => StudioDraftShare.RaDraftKind("a.b"));
        Assert.Throws<ArgumentException>(() => StudioDraftShare.RaDraftKind("a/b"));
    }

    [Fact]
    public void Envelope_carries_draft_state_summary_and_body_for_Studio_inbox()
    {
        var project = ArtifactRefs.ProjectRef("company-uuid", 7);
        var payload = new JsonObject { ["grand_total"] = 1000.5, ["civil"] = new JsonArray() };
        var artifact = ArtifactRefs.Estimate(project, "estimate_draft", 1, ArtifactRefs.EstimateChecksumBody(payload));
        var when = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var envelope = StudioDraftShare.BuildEnvelope(
            "estimate",
            StudioDraftShare.StateDraft,
            "owner",
            when,
            artifact,
            StudioDraftShare.EstimateSummary(1000.5, "DSR 2024", "Villa"),
            payload);

        Assert.Equal(1u, envelope["v"]!.GetValue<uint>());
        Assert.Equal("draft", envelope["state"]!.GetValue<string>());
        Assert.Equal("estimate", envelope["artifact_kind"]!.GetValue<string>());
        Assert.Equal("owner", envelope["shared_by"]!.GetValue<string>());
        Assert.Equal("2026-10-28T12:00:00.0000000Z", envelope["shared_at"]!.GetValue<string>());
        Assert.Equal(1000.5, envelope["summary"]!["grand_total"]!.GetValue<double>());
        Assert.Equal("Villa", envelope["summary"]!["title"]!.GetValue<string>());
        Assert.Equal(1000.5, envelope["body"]!["grand_total"]!.GetValue<double>());
        Assert.Equal("estimate", envelope["artifact_ref"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Ra_envelope_summary_names_the_bill()
    {
        var project = ArtifactRefs.ProjectRef("company-uuid", 3);
        var billId = "deadbeefdeadbeefdeadbeefdeadbeef";
        var payload = new JsonObject { ["id"] = billId, ["gross"] = 50, ["net"] = 40 };
        var artifact = ArtifactRefs.RunningBill(project, billId, 2, payload.ToJsonString());
        var envelope = StudioDraftShare.BuildEnvelope(
            "ra_bill",
            StudioDraftShare.StateDraft,
            "pm",
            DateTimeOffset.UtcNow,
            artifact,
            StudioDraftShare.RaBillSummary(billId, "CON/RA/2026-27/001", 50, 40, "Contractor Co"),
            payload);

        Assert.Equal("ra_bill", envelope["artifact_kind"]!.GetValue<string>());
        Assert.Equal(billId, envelope["summary"]!["bill_id"]!.GetValue<string>());
        Assert.Equal(40.0, envelope["summary"]!["net"]!.GetValue<double>());
        Assert.Equal("ra_bill", envelope["artifact_ref"]!["kind"]!.GetValue<string>());
        Assert.Equal(billId, envelope["artifact_ref"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void Envelope_rejects_unknown_artifact_kind()
    {
        Assert.Throws<ArgumentException>(() =>
            StudioDraftShare.BuildEnvelope(
                "invoice",
                "draft",
                "u",
                DateTimeOffset.UtcNow,
                null,
                new JsonObject(),
                new JsonObject()));
    }
}
