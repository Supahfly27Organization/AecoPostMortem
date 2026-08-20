using System.Globalization;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-16 (S-14, issue #26): turns <see cref="FailedToolCallsCheck"/>'s per-tool rates into
/// <see cref="FindingClass.Waste"/> findings plus the check-registry entry for this run. This is
/// the orchestration <c>AecoPostMortem.Rules</c> deliberately cannot do itself — <c>Rules</c> takes
/// plain <see cref="ToolCallOutcome"/> inputs and returns every tool identity observed, including
/// clean ones; deciding that only a tool with at least one recorded failure is worth surfacing as
/// a finding belongs here, in the project that is allowed to make that call.
/// </summary>
public static class FailedToolCallsFinding
{
    /// <summary>The abstract check identifier recorded on <see cref="CheckRegistryEntry.CheckId"/>
    /// (issue #23, Scenario 4/5).</summary>
    public const string CheckId = "failed-tool-calls";

    public static FailedToolCallsResult Run(IReadOnlyList<ToolCallOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var rates = FailedToolCallsCheck.Run(outcomes);

        var findings = rates
            .Where(rate => rate.FailureRate.Failures > 0)
            .Select(rate => ToFinding(rate, outcomes))
            .ToArray();

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = outcomes.Count,
            FindingCount = findings.Length,
        };

        return new FailedToolCallsResult
        {
            Findings = findings,
            RegistryEntry = registryEntry,
        };
    }

    /// <summary>
    /// Builds one Waste finding. Evidence — not <see cref="Finding.Resolution"/> — is where the
    /// rate and its counts land: <c>Resolution</c> is FR-33's layer-used-per-operand figure, scoped
    /// to adherence findings only, and this is not one. The rate's numerator/denominator are
    /// structurally required together on <see cref="Rules.FailureRate"/> itself (issue #26,
    /// Scenario 1); here they are quoted alongside the session count so a rendered finding can
    /// never show one without the other (Scenario 2).
    /// </summary>
    static Finding ToFinding(ToolFailureRate rate, IReadOnlyList<ToolCallOutcome> outcomes)
    {
        var failedSessions = outcomes
            .Where(outcome => outcome.ToolIdentity == rate.ToolIdentity && !outcome.Succeeded)
            .Select(outcome => outcome.SessionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sessionId => sessionId, StringComparer.Ordinal)
            .ToArray();

        return new Finding
        {
            Class = FindingClass.Waste,
            Provenance = Provenance.Derived,
            Headline = BuildHeadline(rate, failedSessions.Length),
            Evidence =
            [
                new EvidenceItem { Field = "toolIdentity", Value = rate.ToolIdentity },
                new EvidenceItem
                {
                    Field = "failures",
                    Value = rate.FailureRate.Failures.ToString(CultureInfo.InvariantCulture),
                },
                new EvidenceItem
                {
                    Field = "calls",
                    Value = rate.FailureRate.Calls.ToString(CultureInfo.InvariantCulture),
                },
                new EvidenceItem
                {
                    Field = "percentage",
                    Value = FormatPercentage(rate.FailureRate.Percentage),
                },
                new EvidenceItem
                {
                    Field = "sessionCount",
                    Value = rate.SessionCount.ToString(CultureInfo.InvariantCulture),
                },
            ],
            Recurrence = new Recurrence
            {
                Key = rate.ToolIdentity,
                Occurrences = failedSessions
                    .Select(sessionId => new RecurrenceOccurrence { SessionId = sessionId })
                    .ToArray(),
            },
        };
    }

    static string FormatPercentage(double percentage) =>
        percentage.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Mockup parity item #5: grounded in the same rate <see cref="Evidence"/> already
    /// quotes — the tool identity, the failure count, the call count — plus <paramref
    /// name="failedSessionCount"/>, the same distinct-failed-session count that feeds
    /// <see cref="Recurrence.Occurrences"/> (and therefore the row's own leading "sessions affected"
    /// badge, `ProcessDigest.SessionsAffected`) — not <see cref="ToolFailureRate.SessionCount"/>,
    /// which (per that type's own remarks) counts every session that called the tool at all,
    /// including ones where every call succeeded. Reusing the wider figure here would let this
    /// sentence claim more sessions were affected than the badge beside it on the same row does —
    /// caught in code review, the same "a served figure and the ranking cannot come from two
    /// different rules" discipline `Api.FindingEnvelope.SessionsAffected`'s own remarks document.
    /// </summary>
    static string BuildHeadline(ToolFailureRate rate, int failedSessionCount) => string.Format(
        CultureInfo.InvariantCulture,
        "{0} failed {1} of {2} calls ({3}%) across {4} {5}.",
        rate.ToolIdentity,
        rate.FailureRate.Failures,
        rate.FailureRate.Calls,
        FormatPercentage(rate.FailureRate.Percentage),
        failedSessionCount,
        HeadlineText.Pluralize(failedSessionCount, "session"));
}

/// <summary>One run's output: the findings this check produced, and the registry entry that
/// records the run happened whether or not it found anything (issue #23, Scenario 4).</summary>
public sealed record FailedToolCallsResult
{
    public required IReadOnlyList<Finding> Findings { get; init; }

    public required CheckRegistryEntry RegistryEntry { get; init; }
}
