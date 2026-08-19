using System.Globalization;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-20's orchestration (issue #30): reads <see cref="Permission"/> and <see cref="ToolCall"/>
/// through <c>AecoPostMortem.Data</c>, decides which tool calls are questions put to the operator,
/// feeds <see cref="InterruptionLoadCheck"/> the generic operands it needs, and folds the pure
/// result into one <see cref="FindingClass.Waste"/> finding plus a <see cref="CheckRegistryEntry"/>.
/// This is the orchestration <c>AecoPostMortem.Rules</c> deliberately cannot do — see the invariant
/// in that project's CLAUDE.md and the split it documents.
/// </summary>
public static class InterruptionLoadFinding
{
    public const string CheckId = "interruption-load";

    /// <summary>
    /// The tool name that stands in for "a question put to the operator" — the one place allowed
    /// to name it (Repo Rule 6 binds <c>AecoPostMortem.Rules</c> only). Per the ingestion data map,
    /// a question is a completed <c>ask_user</c> call carrying
    /// <c>arguments{question, choices, allow_freeform}</c> and a <c>"User selected: …"</c> result —
    /// measured 124 asked against 124 answered.
    /// </summary>
    const string QuestionToolName = "ask_user";

    /// <summary>
    /// The literal text an unresolved permission prompt renders as. Never "denied": issue #30's
    /// edge case is exactly a measured 1,033 requested against 1,031 with a recorded outcome — two
    /// prompts that never resolved, which is a different state from any recorded outcome.
    /// </summary>
    const string NoOutcomeRecorded = "no outcome recorded";

    public sealed record Result
    {
        public required IReadOnlyList<Finding> Findings { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }

    public static Result Run(IReadOnlyList<Permission> permissions, IReadOnlyList<ToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(toolCalls);

        var questions = QuestionsFrom(toolCalls);

        var load = InterruptionLoadCheck.Evaluate(
            permissions.Select(ToOutcome).ToArray(),
            questions.Select(call => new QuestionOutcome { SessionId = call.SessionId }).ToArray());

        var population = permissions
            .Select(permission => permission.SessionId)
            .Concat(questions.Select(question => question.SessionId))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var findings = load.PermissionPromptCount == 0 && load.QuestionCount == 0
            ? []
            : new[] { ToFinding(load, permissions, questions) };

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = population,
            FindingCount = findings.Length,
        };

        return new Result { Findings = findings, RegistryEntry = registryEntry };
    }

    /// <summary>The operand boundary named in the issue: generic outcomes in, no tool names past
    /// this point.</summary>
    static IReadOnlyList<ToolCall> QuestionsFrom(IEnumerable<ToolCall> toolCalls) =>
        toolCalls.Where(call => call.ToolName == QuestionToolName).ToArray();

    static PermissionPromptOutcome ToOutcome(Permission permission) => new()
    {
        SessionId = permission.SessionId,
        ResultKind = permission.ResultKind,
    };

    /// <summary>
    /// One finding per analysis run. <see cref="Provenance.Observed"/> throughout: FR-20 states
    /// denial is Observed because <c>permission.completed.data.result.kind</c> is a structured
    /// enum, never a string match, and the same is true of the two corpus-wide counts, each a plain
    /// count of a directly-recorded event rather than a heuristic aggregate.
    /// </summary>
    static Finding ToFinding(
        InterruptionLoad load,
        IReadOnlyList<Permission> permissions,
        IReadOnlyList<ToolCall> questions)
    {
        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Field = "permissionPromptCount",
                Value = load.PermissionPromptCount.ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Field = "permissionPromptsWithOutcome",
                Value = load.PermissionPromptsWithOutcome.ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Field = "questionCount",
                Value = load.QuestionCount.ToString(CultureInfo.InvariantCulture),
            },
        };
        evidence.AddRange(PermissionOutcomeBreakdown(permissions));

        var sessionIds = permissions
            .Select(permission => permission.SessionId)
            .Concat(questions.Select(question => question.SessionId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sessionId => sessionId, StringComparer.Ordinal)
            .ToArray();

        return new Finding
        {
            Class = FindingClass.Waste,
            Provenance = Provenance.Observed,
            Evidence = evidence,
            Recurrence = new Recurrence
            {
                Key = "interruption-load",
                Occurrences = sessionIds
                    .Select(sessionId => new RecurrenceOccurrence { SessionId = sessionId })
                    .ToArray(),
            },
            Suggestion = BuildSuggestion(load),
        };
    }

    /// <summary>
    /// Groups permission prompts by whatever <c>ResultKind</c> literally says, quoting it verbatim
    /// rather than matching against a specific denial string — this is what makes Scenario 2 ("the
    /// outcome comes from the recorded result kind... not inferred") hold without this project
    /// having to know Copilot's exact enum values. An unresolved prompt's <c>null</c>
    /// <c>ResultKind</c> is grouped under <see cref="NoOutcomeRecorded"/>, never silently merged
    /// into whatever outcome value happens to sort first.
    /// </summary>
    static IReadOnlyList<EvidenceItem> PermissionOutcomeBreakdown(IReadOnlyList<Permission> permissions) =>
        permissions
            .GroupBy(permission => permission.ResultKind ?? NoOutcomeRecorded, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new EvidenceItem
            {
                Field = $"result_kind:{group.Key}",
                Value = group.Count().ToString(CultureInfo.InvariantCulture),
            })
            .ToArray();

    /// <summary>
    /// FR-56's deterministic template, populated from <see cref="InterruptionLoad"/> as a whole —
    /// the only way to reach this text is through the paired type, so there is no code path that
    /// renders the two counts summed together (issue #30, Scenario 1).
    /// </summary>
    static Suggestion BuildSuggestion(InterruptionLoad load) => new()
    {
        Text = $"{load.PermissionPromptCount} permission prompts ({load.PermissionPromptsWithOutcome} with a "
            + $"recorded outcome, {load.PermissionPromptsWithoutOutcome} with none) and "
            + $"{load.QuestionCount} questions were put to the operator this run — reported as two "
            + "distinct counts, never summed.",
    };
}
