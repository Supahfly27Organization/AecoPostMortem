using System.Globalization;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// Piece 3's fourth slice: orchestrates <see cref="UseAAfterBCheck"/> into
/// <see cref="FindingClass.RuleAdherenceToolChoice"/> findings — a later tool called with no earlier
/// call to its prerequisite anywhere before it in the same session. Both operands resolve against the
/// real <see cref="ToolInvocationShape"/> corpus the same way <see cref="BannedToolFinding"/>'s single
/// operand does; ordering comes straight from <see cref="ToolCall.StartedAt"/>, already a real,
/// populated column — no new RAW parsing needed, the same move <see cref="NeverReadPathFinding"/> made
/// for <see cref="ToolCall.Path"/>.
/// </summary>
public static class UseAAfterBFinding
{
    public const string CheckId = "use-a-after-b";

    public sealed record Result
    {
        public required IReadOnlyList<Finding> Findings { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }

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
            .Where(match => match.Kind == RuleShapeKind.UseAAfterB && match.OperandBText is not null)
            .Select(match => new UseAAfterBMention
            {
                SourceText = match.Statement.Text,
                LaterToolText = match.OperandAText,
                EarlierToolText = match.OperandBText!,
            })
            .ToArray();

        var calls = toolCalls
            .Select(call => new TimedToolCall
            {
                SessionId = call.SessionId,
                ToolCallId = call.ToolCallId,
                ToolName = call.ToolName,
                StartedAt = call.StartedAt,
            })
            .ToArray();

        var violations = UseAAfterBCheck.Run(mentions, calls, invocations);
        var findings = violations.Select(ToFinding).ToArray();

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = population,
            FindingCount = findings.Length,
            Provenance = Provenance.Derived,
        };

        return new Result { Findings = findings, RegistryEntry = registryEntry };
    }

    /// <summary>FR-57's recurrence key for <see cref="FindingClass.RuleAdherenceToolChoice"/> is "the
    /// rule statement" — the same identity a <see cref="UseAAfterBViolation"/> already carries as
    /// <see cref="UseAAfterBViolation.SourceText"/>.</summary>
    static Finding ToFinding(UseAAfterBViolation violation) => new()
    {
        Class = FindingClass.RuleAdherenceToolChoice,
        // Derived, not Observed: whether an earlier call satisfies the ordering is an interpretive
        // step over the raw call log — the same reasoning NeverReadPathFinding gives for its own
        // segment-boundary match.
        Provenance = Provenance.Derived,
        Headline = BuildHeadline(violation),
        Evidence =
        [
            new EvidenceItem { Field = "later_tool", Value = violation.LaterToolText },
            new EvidenceItem { Field = "earlier_tool", Value = violation.EarlierToolText },
            new EvidenceItem { Field = "violation_count", Value = violation.ViolationCount.ToString(CultureInfo.InvariantCulture) },
        ],
        Recurrence = new Recurrence
        {
            Key = violation.SourceText,
            Occurrences = violation.SessionIds.Select(id => new RecurrenceOccurrence { SessionId = id }).ToArray(),
        },
    };

    /// <summary>Mockup parity item #5: grounded in the same later/earlier tool pair and violation
    /// count <see cref="Evidence"/> already carries.</summary>
    static string BuildHeadline(UseAAfterBViolation violation) => string.Format(
        CultureInfo.InvariantCulture,
        "{0} was called without {1} first, {2} {3}.",
        violation.LaterToolText,
        violation.EarlierToolText,
        violation.ViolationCount,
        HeadlineText.Pluralize(violation.ViolationCount, "time"));
}
