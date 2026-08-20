using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

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
