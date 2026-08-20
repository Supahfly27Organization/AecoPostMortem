using System.Globalization;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// Piece 3's second slice: orchestrates <see cref="BannedToolCheck"/> into
/// <see cref="FindingClass.RuleAdherenceToolChoice"/> findings — <c>Rules/CLAUDE.md</c>'s own
/// remarks on why <c>ToolVocabularyMismatchCheck</c> does not fit a prohibition. Every
/// <see cref="BannedToolUsage"/> the check returns is already a violation (its own remarks), so this
/// orchestration does no further filtering — unlike <see cref="FailedToolCallsFinding"/>'s
/// <c>Failures &gt; 0</c> filter over a check that reports every candidate, clean or not.
/// </summary>
public static class BannedToolFinding
{
    public const string CheckId = "banned-tool-used";

    public sealed record Result
    {
        public required IReadOnlyList<Finding> Findings { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }

    /// <summary>
    /// <paramref name="matches"/> is whatever <c>RuleShapeCatalogue.MatchAll</c> produced — this
    /// filters to <see cref="RuleShapeKind.ToolIsBanned"/> itself, the same "the caller hands in
    /// whatever it has, the orchestration narrows it" shape <see cref="ToolFailureClusterFinding"/>
    /// documents for its own cross-reference input. <paramref name="toolCalls"/> is read through
    /// <c>AecoPostMortem.Data</c> only for session attribution — <see cref="ToolInvocationShape"/>
    /// carries no <c>SessionId</c> by design (<c>Rules/CLAUDE.md</c>) — the same split
    /// <see cref="RepeatedFileReadFindingCheck"/> draws between its generic operand and its own
    /// entity read.
    /// </summary>
    public static Result Run(
        IReadOnlyList<RuleShapeMatch> matches,
        IReadOnlyList<ToolInvocationShape> invocations,
        IReadOnlyList<ToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(invocations);
        ArgumentNullException.ThrowIfNull(toolCalls);

        var population = toolCalls.Select(call => call.SessionId).Distinct(StringComparer.Ordinal).Count();

        var mentions = matches
            .Where(match => match.Kind == RuleShapeKind.ToolIsBanned)
            .Select(match => new BannedToolMention { RuleText = match.Statement.Text, NamedTool = match.OperandAText })
            .ToArray();

        var usages = BannedToolCheck.Run(mentions, invocations);
        var findings = usages.Select(usage => ToFinding(usage, toolCalls)).ToArray();

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = population,
            FindingCount = findings.Length,
        };

        return new Result { Findings = findings, RegistryEntry = registryEntry };
    }

    /// <summary>FR-57's recurrence key for <see cref="FindingClass.RuleAdherenceToolChoice"/> is "the
    /// rule statement" — the statement's own text, the same identity a <see cref="BannedToolUsage"/>
    /// already carries as <see cref="BannedToolUsage.RuleText"/>.</summary>
    static Finding ToFinding(BannedToolUsage usage, IReadOnlyList<ToolCall> toolCalls)
    {
        var resolvedTools = usage.ResolvedTools.ToHashSet(StringComparer.Ordinal);
        var sessionIds = toolCalls
            .Where(call => resolvedTools.Contains(call.ToolName))
            .Select(call => call.SessionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var evidence = new List<EvidenceItem>
        {
            new() { Field = "named_tool", Value = usage.NamedTool },
            new() { Field = "call_count", Value = usage.CallCount.ToString(CultureInfo.InvariantCulture) },
        };
        evidence.AddRange(usage.ResolvedTools.Select(
            tool => new EvidenceItem { Field = "resolved_tool", Value = tool }));

        return new Finding
        {
            Class = FindingClass.RuleAdherenceToolChoice,
            // Derived, not Observed: the named tool resolves to a real tool through OperandResolver's
            // layered matching, an interpretive step over the raw call log — the same reasoning
            // RepeatedFileReadFindingCheck gives for its own aggregate count.
            Provenance = Provenance.Derived,
            Headline = BuildHeadline(usage, sessionIds.Length),
            Evidence = evidence,
            Recurrence = new Recurrence
            {
                Key = usage.RuleText,
                Occurrences = sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id }).ToArray(),
            },
        };
    }

    /// <summary>Mockup parity item #5: grounded in the same named-tool/call-count pair
    /// <see cref="Evidence"/> already carries, plus the session count this method already computed
    /// for attribution.</summary>
    static string BuildHeadline(BannedToolUsage usage, int sessionCount) => string.Format(
        CultureInfo.InvariantCulture,
        "The banned tool {0} was called {1} {2} across {3} {4}, despite the rule against it.",
        usage.NamedTool,
        usage.CallCount,
        HeadlineText.Pluralize(usage.CallCount, "time"),
        sessionCount,
        HeadlineText.Pluralize(sessionCount, "session"));
}
