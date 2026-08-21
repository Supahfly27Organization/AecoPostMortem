using System.Globalization;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// Piece 3's fifth and final slice: orchestrates <see cref="AlwaysPassParamCheck"/> into
/// <see cref="FindingClass.RuleAdherenceToolChoice"/> findings — a subagent-dispatch call that omitted
/// a parameter the rule requires on every such call. Unlike every other <c>RuleAdherenceToolChoice</c>
/// finding in this project, this one needs no separate <see cref="AecoPostMortem.Data.Execution.ToolCall"/>
/// read for session attribution: <see cref="ParamCarryingCall.SessionId"/> already carries it, since
/// this shape was built for exactly this check rather than reused from a general argument-shape corpus.
/// </summary>
public static class AlwaysPassParamFinding
{
    public const string CheckId = "always-pass-param";

    public sealed record Result
    {
        public required IReadOnlyList<Finding> Findings { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }

    public static Result Run(IReadOnlyList<RuleShapeMatch> matches, IReadOnlyList<ParamCarryingCall> calls)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(calls);

        var population = calls.Select(call => call.SessionId).Distinct(StringComparer.Ordinal).Count();

        var mentions = matches
            .Where(match => match.Kind == RuleShapeKind.AlwaysPassParam)
            .Select(match => new AlwaysPassParamMention { SourceText = match.Statement.Text, ParamName = match.OperandAText })
            .ToArray();

        var violations = AlwaysPassParamCheck.Run(mentions, calls);
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
    /// rule statement" — the same identity an <see cref="AlwaysPassParamViolation"/> already carries as
    /// <see cref="AlwaysPassParamViolation.SourceText"/>.</summary>
    static Finding ToFinding(AlwaysPassParamViolation violation) => new()
    {
        Class = FindingClass.RuleAdherenceToolChoice,
        // Derived, not Observed: whether a call's own arguments carried the named key is a direct
        // fact, but attributing it to the rule's own obligation is an interpretive step over the raw
        // call log — the same reasoning BannedToolFinding/NeverReadPathFinding give for their own
        // matches.
        Provenance = Provenance.Derived,
        Headline = BuildHeadline(violation),
        Evidence =
        [
            new EvidenceItem { Field = "param_name", Value = violation.ParamName },
            new EvidenceItem { Field = "violation_count", Value = violation.ViolationCount.ToString(CultureInfo.InvariantCulture) },
        ],
        Recurrence = new Recurrence
        {
            Key = violation.SourceText,
            Occurrences = violation.SessionIds.Select(id => new RecurrenceOccurrence { SessionId = id }).ToArray(),
        },
    };

    /// <summary>Mockup parity item #5: grounded in the same param-name/violation-count pair
    /// <see cref="Evidence"/> already carries.</summary>
    static string BuildHeadline(AlwaysPassParamViolation violation) => string.Format(
        CultureInfo.InvariantCulture,
        "The `{0}` parameter was omitted on {1} {2} that should have carried it.",
        violation.ParamName,
        violation.ViolationCount,
        HeadlineText.Pluralize(violation.ViolationCount, "call"));
}
