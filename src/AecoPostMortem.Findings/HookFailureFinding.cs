using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// One failed <c>hook.start</c>/<c>hook.end</c> pair — the plain input this check's orchestration
/// turns into evidence. Deliberately not <c>AecoPostMortem.Data.Execution.Hook</c>: evidence
/// quotes the field Copilot wrote (<see cref="Evidence"/>'s "quoted from the event" guarantee), so
/// this carries only what a failed pair contributes to that quote.
/// </summary>
public sealed record HookFailureEvent
{
    public required string SessionId { get; init; }

    public required string HookName { get; init; }

    public required bool Success { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// FR-17's orchestration (issue #27): reads the corpus's session population and its failed hook
/// events, calls <see cref="AecoPostMortem.Rules.HookFailureCheck"/> for the paired denominators,
/// and writes one <c>Finding</c> per distinct hook identity — <c>Rules</c> stays pure counting,
/// this project is what names the hook and quotes its fields.
/// </summary>
public static class HookFailureFinding
{
    /// <summary>The check's own identity in <see cref="CheckRegistry"/> — an abstract id, not an
    /// enum, per <c>CheckRegistryEntry.CheckId</c>'s own remarks.</summary>
    public const string CheckId = "hook-failure";

    /// <summary>
    /// Builds every hook-failure finding for one analysis run, plus the check's registry entry.
    /// Returns no findings — a clean run, not a refused one — once <paramref name="failures"/> is
    /// empty: FR-17's finding is meant to disappear from the digest on its own the moment the
    /// operator fixes the hook.
    /// </summary>
    public static (IReadOnlyList<Finding> Findings, CheckRegistryEntry Registry) Build(
        IReadOnlyList<string> allSessionIds,
        IReadOnlySet<string> sessionsWithToolCall,
        IReadOnlyList<HookFailureEvent> failures)
    {
        ArgumentNullException.ThrowIfNull(allSessionIds);
        ArgumentNullException.ThrowIfNull(sessionsWithToolCall);
        ArgumentNullException.ThrowIfNull(failures);

        var failedSessionIds = failures.Select(failure => failure.SessionId).ToHashSet();

        var outcomes = allSessionIds
            .Select(sessionId => new SessionHookOutcome
            {
                SessionId = sessionId,
                HookFailed = failedSessionIds.Contains(sessionId),
                MadeToolCall = sessionsWithToolCall.Contains(sessionId),
            })
            .ToList();

        var counts = HookFailureCheck.Evaluate(outcomes);

        var findings = counts.OverAllSessions.Count == 0
            ? []
            : BuildFindings(failures, counts);

        var registry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = allSessionIds.Count,
            FindingCount = findings.Count,
        };

        return (findings, registry);
    }

    static IReadOnlyList<Finding> BuildFindings(
        IReadOnlyList<HookFailureEvent> failures,
        HookFailureCounts counts)
    {
        var suggestion = BuildSuggestion(counts);

        return failures
            .GroupBy(failure => failure.HookName, StringComparer.Ordinal)
            .Select(group =>
            {
                var evidenceSource = group.First();

                return new Finding
                {
                    Class = FindingClass.Waste,
                    Provenance = Provenance.Observed,
                    Evidence =
                    [
                        new EvidenceItem
                        {
                            Field = "data.success",
                            Value = evidenceSource.Success ? "true" : "false",
                        },
                        new EvidenceItem
                        {
                            Field = "data.error",
                            Value = evidenceSource.Error ?? string.Empty,
                        },
                    ],
                    Recurrence = new Recurrence
                    {
                        Key = group.Key,
                        Occurrences = group
                            .Select(failure => failure.SessionId)
                            .Distinct(StringComparer.Ordinal)
                            .Select(sessionId => new RecurrenceOccurrence { SessionId = sessionId })
                            .ToList(),
                    },
                    Suggestion = suggestion,
                };
            })
            .ToList();
    }

    /// <summary>
    /// FR-56's deterministic template, populated from <see cref="HookFailureCounts"/> as a whole —
    /// the only way to reach this text is through the paired type, so there is no code path that
    /// renders one denominator without the other (issue #27, Scenario 1).
    /// </summary>
    static Suggestion BuildSuggestion(HookFailureCounts counts) => new()
    {
        Text = $"This hook failed in {counts.OverAllSessions.Count} of "
            + $"{counts.OverAllSessions.Population} sessions overall, including "
            + $"{counts.OverSessionsWithToolCall.Count} of the "
            + $"{counts.OverSessionsWithToolCall.Population} sessions that made a tool call.",
    };
}
