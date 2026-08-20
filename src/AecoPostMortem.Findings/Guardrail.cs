namespace AecoPostMortem.Findings;

/// <summary>
/// PRD §5.4's guardrail — the product's own failure detector — computed from recorded operator
/// responses (Scenario 2 of issue #49, S-39, FR-45). PRD §5.4 calls a response the operator actually
/// acted on "adjudicated": <see cref="OperatorResponse.Accepted"/> or <see cref="OperatorResponse.Rejected"/>.
/// <see cref="OperatorResponse.Ignored"/> is not a verdict and is excluded from both figures below —
/// the same reasoning <c>FailedToolCallsFinding</c>'s own doc comment gives for never letting a
/// percentage stand without the counts that produced it: <see cref="AdjudicatedCount"/> is always
/// carried alongside both shares, and a share is <c>null</c>, never <c>0</c>, when the sample is empty.
/// </summary>
public sealed record Guardrail
{
    /// <summary>PRD §5.4's stated target: "not read at all" below this many adjudicated suggestions.
    /// Carried here as a fact about the guardrail, not enforced — deciding whether to actually read a
    /// share below this sample is a rendering-layer choice, not this computation's.</summary>
    public const int MinimumSampleTarget = 20;

    /// <summary>Accepted-or-rejected responses — the sample size both shares below are drawn from.</summary>
    public required int AdjudicatedCount { get; init; }

    public required int RejectedCount { get; init; }

    /// <summary><see cref="RejectedCount"/> ÷ <see cref="AdjudicatedCount"/>. <c>null</c> when
    /// <see cref="AdjudicatedCount"/> is zero — no suggestions adjudicated yet is not the same claim
    /// as a zero rejection rate.</summary>
    public required double? RejectionShare { get; init; }

    /// <summary>Adjudicated responses whose recorded <see cref="OperatorResponseRecord.Provenance"/>
    /// was <see cref="Provenance.Inferred"/>.</summary>
    public required int InferredAmongAdjudicatedCount { get; init; }

    /// <summary><see cref="InferredAmongAdjudicatedCount"/> ÷ <see cref="AdjudicatedCount"/>. Null
    /// under the same rule as <see cref="RejectionShare"/>.</summary>
    public required double? InferredShare { get; init; }

    /// <summary>Computes the guardrail from <paramref name="log"/>'s <see cref="OperatorResponseLog.CurrentResponses"/>
    /// — one response per finding identity, so a finding whose verdict changed counts once, as its
    /// latest verdict, never once per historical entry.</summary>
    public static Guardrail Compute(OperatorResponseLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var adjudicated = log.CurrentResponses()
            .Where(entry => entry.Response is OperatorResponse.Accepted or OperatorResponse.Rejected)
            .ToList();

        var rejectedCount = adjudicated.Count(entry => entry.Response == OperatorResponse.Rejected);
        var inferredCount = adjudicated.Count(entry => entry.Provenance == Provenance.Inferred);

        return new Guardrail
        {
            AdjudicatedCount = adjudicated.Count,
            RejectedCount = rejectedCount,
            RejectionShare = adjudicated.Count == 0 ? null : (double)rejectedCount / adjudicated.Count,
            InferredAmongAdjudicatedCount = inferredCount,
            InferredShare = adjudicated.Count == 0 ? null : (double)inferredCount / adjudicated.Count,
        };
    }
}
