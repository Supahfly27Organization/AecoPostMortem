using System.Globalization;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-15 / issue #25's orchestration: reads <see cref="ToolCall"/> operands through
/// <c>AecoPostMortem.Data</c>, decides which of them are read events, feeds
/// <see cref="RepeatedReadCheck"/> that generic operand, and folds the pure result into a
/// registered <see cref="Finding"/> per path plus a <see cref="CheckRegistryEntry"/>. This is the
/// orchestration <c>AecoPostMortem.Rules</c> deliberately cannot do — see the invariant in that
/// project's CLAUDE.md and the split it documents.
/// </summary>
public static class RepeatedFileReadFindingCheck
{
    public const string CheckId = "repeated-file-read";

    /// <summary>
    /// The tool name that stands in for "file read" until S-21's role/vocabulary derivation lands.
    /// This is the one place in the codebase allowed to name it (Repo Rule 6 binds
    /// <c>AecoPostMortem.Rules</c> only) — measured present on 5,201 of 5,201 <c>view</c> calls, so
    /// a call by this name with no path is a parser defect, excluded rather than trusted (see
    /// <see cref="ReadEventsFrom"/>). When the role layer lands, this constant and the filter below
    /// are what it replaces; <see cref="ReadEvent"/> and <see cref="RepeatedReadCheck"/> do not
    /// change.
    /// </summary>
    const string ReadToolName = "view";

    public sealed record Result
    {
        public required IReadOnlyList<Finding> Findings { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }

    public static Result Run(IReadOnlyList<ToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);

        var population = toolCalls.Select(call => call.SessionId).Distinct().Count();
        var readEvents = ReadEventsFrom(toolCalls);
        var occurrences = RepeatedReadCheck.Run(readEvents);

        var findings = occurrences
            .GroupBy(occurrence => occurrence.Path, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(ToFinding)
            .ToArray();

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = population,
            FindingCount = findings.Length,
        };

        return new Result { Findings = findings, RegistryEntry = registryEntry };
    }

    /// <summary>
    /// The operand boundary named in the issue: generic read events in, no tool names past this
    /// point. A call counts as a read when it names <see cref="ReadToolName"/> and carries a path —
    /// a call missing the path is excluded rather than treated as a zero-length read, per the
    /// parser-defect edge case in issue #25.
    /// </summary>
    static IReadOnlyList<ReadEvent> ReadEventsFrom(IEnumerable<ToolCall> toolCalls) =>
        toolCalls
            .Where(call => call.ToolName == ReadToolName && call.Path is not null)
            .Select(call => new ReadEvent { SessionId = call.SessionId, Path = call.Path! })
            .ToArray();

    /// <summary>FR-57's recurrence key for this class is the path: every occurrence for one path,
    /// across however many sessions, is one finding.</summary>
    static Finding ToFinding(IGrouping<string, RepeatedReadOccurrence> occurrencesForPath)
    {
        var path = occurrencesForPath.Key;
        var ordered = occurrencesForPath.OrderBy(o => o.SessionId, StringComparer.Ordinal).ToArray();

        var evidence = new List<EvidenceItem> { new() { Field = "data.path", Value = path } };
        evidence.AddRange(ordered.Select(occurrence => new EvidenceItem
        {
            Field = $"read_count:{occurrence.SessionId}",
            Value = occurrence.ReadCount.ToString(CultureInfo.InvariantCulture),
        }));

        return new Finding
        {
            Class = FindingClass.Waste,
            // Derived, not Observed: a repeat count is an aggregate over several raw read events,
            // not a single event's field (PRD §3.8; the digest mockup marks this finding "der").
            Provenance = Provenance.Derived,
            Evidence = evidence,
            Recurrence = new Recurrence
            {
                Key = path,
                Occurrences = ordered
                    .Select(occurrence => new RecurrenceOccurrence { SessionId = occurrence.SessionId })
                    .ToArray(),
            },
        };
    }
}
