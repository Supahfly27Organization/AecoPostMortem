using System.Text.Json.Serialization;
using AecoPostMortem.Findings;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// What <c>GET /api/monitor-comparison</c> answers: either a real comparison, or which of the three
/// structurally different reasons it cannot compute one. All four are served <c>200</c> — a refusal
/// here is a designed state about a pair that genuinely exists, not a missing resource, the same
/// distinction <see cref="StepEvidenceEnvelope"/> already draws in refusing to answer 404 for a step
/// whose raw event cannot be found. A version hash no session ever carried is still a 404: that
/// names something that does not exist.
///
/// This union replaced a bare, bodyless <c>404</c> shared by all three refusals. That shape forced
/// the one client that needed to tell them apart to re-implement
/// <see cref="RuleSetVersionAdjacency.RequireAdjacentPair"/>'s own sort-and-index logic in
/// TypeScript (<c>web/src/api/useMonitorComparison.ts</c>) purely to rule out one of the three
/// before calling — a duplicated rule with nothing pinning the two implementations together, plus a
/// second workaround in <c>MonitorPage.tsx</c> for the third cause. Both are deleted now: the server
/// states the reason, so no client has to derive it. This is the "more robust fix if a second client
/// ever needs the same distinction" both projects' own CLAUDE.md files recorded as deferred.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ComparisonResult), "comparison")]
[JsonDerivedType(typeof(NotAdjacentResult), "notAdjacent")]
[JsonDerivedType(typeof(NoComparableRuleResult), "noComparableRule")]
[JsonDerivedType(typeof(NoRepositoryResult), "noRepository")]
public abstract record MonitorComparisonResultEnvelope
{
    private MonitorComparisonResultEnvelope()
    {
    }

    public static MonitorComparisonResultEnvelope From(MonitorComparison comparison) =>
        new ComparisonResult { Comparison = MonitorComparisonEnvelope.From(comparison) };

    /// <summary>The pair exists and both versions are real, but other versions were in force between
    /// them — named here rather than left for a client to work out, since the server already
    /// computed them for <see cref="NonAdjacentRuleSetVersionsException.Intervening"/>.</summary>
    public static MonitorComparisonResultEnvelope NotAdjacent(IReadOnlyList<RuleSetVersion> intervening)
    {
        ArgumentNullException.ThrowIfNull(intervening);

        return new NotAdjacentResult
        {
            Intervening = [.. intervening.Select(RuleSetVersionEnvelope.From)],
        };
    }

    /// <summary>An adjacent pair whose <c>after</c> version carries no
    /// <see cref="RuleShapeKind.PreferAOverB"/> statement — the only shape
    /// <see cref="MonitorComparison.Compare"/> takes two operands for. Nothing is wrong; there is
    /// simply no rule whose adherence could be compared across this edit.</summary>
    public static MonitorComparisonResultEnvelope NoComparableRule { get; } = new NoComparableRuleResult();

    /// <summary>No session anywhere in the store records a repository, so there is no scope to
    /// resolve either version within. A store-level empty state, not a fact about this pair.</summary>
    public static MonitorComparisonResultEnvelope NoRepository { get; } = new NoRepositoryResult();

    public sealed record ComparisonResult : MonitorComparisonResultEnvelope
    {
        public required MonitorComparisonEnvelope Comparison { get; init; }
    }

    public sealed record NotAdjacentResult : MonitorComparisonResultEnvelope
    {
        public required IReadOnlyList<RuleSetVersionEnvelope> Intervening { get; init; }
    }

    public sealed record NoComparableRuleResult : MonitorComparisonResultEnvelope;

    public sealed record NoRepositoryResult : MonitorComparisonResultEnvelope;
}

/// <summary>
/// FR-39's served comparison (S-35, issue #43): adherence for one rule, before and after an adjacent
/// rule-set-version edit, under one shared resolution. <see cref="BeforeVersion"/>/
/// <see cref="AfterVersion"/> reuse <see cref="RuleSetVersionEnvelope"/> (S-22) rather than a bare
/// hash or display string, so <see cref="Rules.RuleSetVersion.SessionCount"/> — Scenario 2's sample
/// size — rides on the same required member as everything else describing that side, never a field a
/// client could omit while still rendering the percentage beside it.
///
/// <see cref="Before"/>/<see cref="After"/> carry <see cref="Findings.AdherenceFigure"/> directly, the
/// same domain type <c>Api.FindingEnvelope.Adherence.Figure</c> already serialises verbatim — there is
/// no separate figure envelope in this project to keep in sync with a second one.
/// </summary>
public sealed record MonitorComparisonEnvelope
{
    public required RuleSetVersionEnvelope BeforeVersion { get; init; }

    public required RuleSetVersionEnvelope AfterVersion { get; init; }

    public required AdherenceFigure Before { get; init; }

    public required AdherenceFigure After { get; init; }

    public static MonitorComparisonEnvelope From(MonitorComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        return new MonitorComparisonEnvelope
        {
            BeforeVersion = RuleSetVersionEnvelope.From(comparison.BeforeVersion),
            AfterVersion = RuleSetVersionEnvelope.From(comparison.AfterVersion),
            Before = comparison.Before,
            After = comparison.After,
        };
    }
}
