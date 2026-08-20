using System.Globalization;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// Piece 3's third slice: orchestrates <see cref="NeverReadPathCheck"/> into
/// <see cref="FindingClass.RuleAdherenceToolChoice"/> findings — the same class
/// <see cref="BannedToolFinding"/> uses, since both answer "was a prohibited target touched at all."
/// Unlike <see cref="BannedToolFinding"/>, no <see cref="ToolInvocationShape"/> corpus is involved:
/// <see cref="ReadEvent"/>s are built straight from every <see cref="ToolCall"/> carrying a non-null
/// <see cref="ToolCall.Path"/>, regardless of its own tool name — <c>NeverReadPath</c>'s own grammar
/// covers read/open/access/modify/edit/list, broader than <see cref="RepeatedFileReadFindingCheck"/>'s
/// narrower "view only" mapping for its own, different question.
/// </summary>
public static class NeverReadPathFinding
{
    public const string CheckId = "never-read-path-used";

    /// <summary>NeverReadPath's own grammar (<c>Rules.RuleShapeCatalogue</c>) covers
    /// read/open/access/modify/edit/list — never "create". A <c>create</c> call writes a brand-new
    /// file; it reads nothing and touches no pre-existing content the rule could plausibly be
    /// protecting, so counting it would mislabel a fresh write as a violation of a "never read" rule.
    /// This is the one tool name this file is allowed to name (Repo Rule 6 binds
    /// <c>AecoPostMortem.Rules</c> only) — the same "Findings decides which raw calls count"
    /// discipline <c>RepeatedFileReadFindingCheck</c> already documents for its own "view"-only
    /// mapping.</summary>
    const string CreateToolName = "create";

    public sealed record Result
    {
        public required IReadOnlyList<Finding> Findings { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }

    public static Result Run(IReadOnlyList<RuleShapeMatch> matches, IReadOnlyList<ToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(toolCalls);

        var population = toolCalls.Select(call => call.SessionId).Distinct(StringComparer.Ordinal).Count();

        var mentions = matches
            .Where(match => match.Kind == RuleShapeKind.NeverReadPath)
            .Select(match => new NeverReadPathMention { SourceText = match.Statement.Text, NamedPath = match.OperandAText })
            .ToArray();

        var events = toolCalls
            .Where(call => call.Path is not null && call.ToolName != CreateToolName)
            .Select(call => new ReadEvent { SessionId = call.SessionId, Path = call.Path! })
            .ToArray();

        var violations = NeverReadPathCheck.Run(mentions, events);
        var findings = violations.Select(ToFinding).ToArray();

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
    /// rule statement" — the statement's own text, the same identity a
    /// <see cref="NeverReadPathViolation"/> already carries as
    /// <see cref="NeverReadPathViolation.SourceText"/>.</summary>
    static Finding ToFinding(NeverReadPathViolation violation) => new()
    {
        Class = FindingClass.RuleAdherenceToolChoice,
        // Derived, not Observed: whether an observed path falls under the banned operand is a
        // segment-boundary match over the raw call log, an interpretive step — the same reasoning
        // BannedToolFinding gives for its own OperandResolver-driven match.
        Provenance = Provenance.Derived,
        Evidence =
        [
            new EvidenceItem { Field = "named_path", Value = violation.NamedPath },
            new EvidenceItem { Field = "access_count", Value = violation.AccessCount.ToString(CultureInfo.InvariantCulture) },
        ],
        Recurrence = new Recurrence
        {
            Key = violation.SourceText,
            Occurrences = violation.SessionIds.Select(id => new RecurrenceOccurrence { SessionId = id }).ToArray(),
        },
    };
}
