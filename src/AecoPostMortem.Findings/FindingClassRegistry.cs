namespace AecoPostMortem.Findings;

/// <summary>FR-57: what one <see cref="FindingClass"/> declares makes two occurrences in two
/// sessions the same finding.</summary>
public sealed record FindingClassRegistration
{
    public required FindingClass Class { get; init; }

    public required string RecurrenceKeyDescription { get; init; }
}

/// <summary>
/// Scenario 3 of the finding contract (issue #23): every finding class is registered, and declares
/// its recurrence key. Four entries, fixed — <see cref="FindingClass"/> is a closed set.
/// </summary>
public static class FindingClassRegistry
{
    public static readonly IReadOnlyList<FindingClassRegistration> All =
    [
        new()
        {
            Class = FindingClass.RuleAdherenceToolChoice,
            RecurrenceKeyDescription = "the rule statement",
        },
        new()
        {
            Class = FindingClass.Waste,
            RecurrenceKeyDescription =
                "the file path for a repeated read, the hook identity for a hook failure, the "
                + "tool identity for a failed-tool-call rate, or the turn identity for an aborted "
                + "turn",
        },
        new()
        {
            Class = FindingClass.RuleAdherenceWrittenContent,
            RecurrenceKeyDescription = "the rule statement",
        },
        new()
        {
            Class = FindingClass.MissingCapability,
            RecurrenceKeyDescription = "the tool name",
        },
    ];
}
