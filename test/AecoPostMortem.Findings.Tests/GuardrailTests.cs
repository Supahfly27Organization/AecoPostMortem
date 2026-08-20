namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Scenario 2 of issue #49 (S-39, FR-45): the guardrail (PRD §5.4) is computable from recorded
/// operator responses — it reports the rejection share and the share of acted-on findings that were
/// <see cref="Provenance.Inferred"/>. PRD §5.4 calls acted-on responses "adjudicated": accepted or
/// rejected, never <see cref="OperatorResponse.Ignored"/>.
/// </summary>
public sealed class GuardrailTests
{
    private static OperatorResponseRecord Record(
        FindingClass findingClass,
        string recurrenceKey,
        Provenance provenance,
        OperatorResponse response) => new()
    {
        Class = findingClass,
        RecurrenceKey = recurrenceKey,
        Provenance = provenance,
        Response = response,
        RecordedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void The_rejection_share_is_rejected_over_adjudicated()
    {
        var log = OperatorResponseLog.Empty
            .Record(Record(FindingClass.Waste, "a", Provenance.Observed, OperatorResponse.Accepted))
            .Record(Record(FindingClass.Waste, "b", Provenance.Observed, OperatorResponse.Rejected))
            .Record(Record(FindingClass.Waste, "c", Provenance.Observed, OperatorResponse.Rejected))
            .Record(Record(FindingClass.Waste, "d", Provenance.Observed, OperatorResponse.Rejected))
            .Record(Record(FindingClass.Waste, "e", Provenance.Observed, OperatorResponse.Accepted));

        var guardrail = Guardrail.Compute(log);

        Assert.Equal(5, guardrail.AdjudicatedCount);
        Assert.Equal(3, guardrail.RejectedCount);
        Assert.Equal(0.6, guardrail.RejectionShare);
    }

    [Fact]
    public void Ignored_responses_are_excluded_from_the_adjudicated_sample()
    {
        var log = OperatorResponseLog.Empty
            .Record(Record(FindingClass.Waste, "a", Provenance.Observed, OperatorResponse.Ignored))
            .Record(Record(FindingClass.Waste, "b", Provenance.Observed, OperatorResponse.Ignored))
            .Record(Record(FindingClass.Waste, "c", Provenance.Observed, OperatorResponse.Rejected));

        var guardrail = Guardrail.Compute(log);

        Assert.Equal(1, guardrail.AdjudicatedCount);
        Assert.Equal(1.0, guardrail.RejectionShare);
    }

    [Fact]
    public void The_inferred_share_is_inferred_acted_on_findings_over_adjudicated()
    {
        var log = OperatorResponseLog.Empty
            .Record(Record(FindingClass.MissingCapability, "a", Provenance.Inferred, OperatorResponse.Accepted))
            .Record(Record(FindingClass.MissingCapability, "b", Provenance.Inferred, OperatorResponse.Rejected))
            .Record(Record(FindingClass.Waste, "c", Provenance.Derived, OperatorResponse.Accepted))
            .Record(Record(FindingClass.Waste, "d", Provenance.Observed, OperatorResponse.Accepted));

        var guardrail = Guardrail.Compute(log);

        Assert.Equal(4, guardrail.AdjudicatedCount);
        Assert.Equal(2, guardrail.InferredAmongAdjudicatedCount);
        Assert.Equal(0.5, guardrail.InferredShare);
    }

    [Fact]
    public void With_no_adjudicated_responses_both_shares_are_null_rather_than_zero()
    {
        var log = OperatorResponseLog.Empty
            .Record(Record(FindingClass.Waste, "a", Provenance.Observed, OperatorResponse.Ignored));

        var guardrail = Guardrail.Compute(log);

        Assert.Equal(0, guardrail.AdjudicatedCount);
        Assert.Null(guardrail.RejectionShare);
        Assert.Null(guardrail.InferredShare);
    }

    /// <summary>Changing a verdict must not double-count the earlier one toward either share — the
    /// guardrail reads <see cref="OperatorResponseLog.CurrentResponses"/>, not the raw history.</summary>
    [Fact]
    public void A_finding_whose_verdict_changed_counts_once_toward_the_guardrail()
    {
        var log = OperatorResponseLog.Empty
            .Record(new OperatorResponseRecord
            {
                Class = FindingClass.Waste,
                RecurrenceKey = "a",
                Provenance = Provenance.Observed,
                Response = OperatorResponse.Rejected,
                RecordedAt = DateTimeOffset.UnixEpoch,
            })
            .Record(new OperatorResponseRecord
            {
                Class = FindingClass.Waste,
                RecurrenceKey = "a",
                Provenance = Provenance.Observed,
                Response = OperatorResponse.Accepted,
                RecordedAt = DateTimeOffset.UnixEpoch.AddDays(1),
            });

        var guardrail = Guardrail.Compute(log);

        Assert.Equal(1, guardrail.AdjudicatedCount);
        Assert.Equal(0, guardrail.RejectedCount);
        Assert.Equal(0.0, guardrail.RejectionShare);
    }
}
