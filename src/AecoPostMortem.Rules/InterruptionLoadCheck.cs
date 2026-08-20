namespace AecoPostMortem.Rules;

/// <summary>
/// One permission prompt's outcome, as a plain input — this project cannot see
/// <c>AecoPostMortem.Data.Execution.Permission</c>. <see cref="ResultKind"/> is whatever the
/// recorded result field says, carried verbatim; <c>null</c> means the prompt never resolved at
/// all (a measured 1,033 requested against 1,031 with a recorded outcome — two prompts that never
/// completed), which is a distinct state from any resolved outcome, denial included.
/// </summary>
public sealed record PermissionPromptOutcome
{
    public required string SessionId { get; init; }

    public string? ResultKind { get; init; }
}

/// <summary>One question put to the operator — plain input, no tool name (Repo Rule 6): this
/// project never sees which tool asked it.</summary>
public sealed record QuestionOutcome
{
    public required string SessionId { get; init; }
}

/// <summary>
/// FR-20's two distinct counts, plus the paired denominator issue #30's edge case requires:
/// permission prompts and questions are never summed into one "interruptions" figure (Scenario 1)
/// because they mean different things — a prompt can be denied, a question can only go answered or
/// not. <see cref="PermissionPromptsWithOutcome"/> pairs with
/// <see cref="PermissionPromptCount"/> the same way <c>HookFailureCounts</c> pairs both of its
/// denominators: both are <c>required</c>, so a caller cannot construct a result that states one
/// without the other. <see cref="PermissionPromptsWithoutOutcome"/> is computed, never stored,
/// mirroring <c>FailureRate.Percentage</c> — there is no constructor path that could disagree with
/// the two counts it is derived from.
/// </summary>
public sealed record InterruptionLoad
{
    public required int PermissionPromptCount { get; init; }

    public required int PermissionPromptsWithOutcome { get; init; }

    public required int QuestionCount { get; init; }

    public int PermissionPromptsWithoutOutcome => PermissionPromptCount - PermissionPromptsWithOutcome;
}

/// <summary>
/// Pure check logic: reduces a corpus of permission-prompt and question outcomes to FR-20's two
/// distinct counts. Takes plain inputs and returns a result — no tool name, no MCP server, no
/// repository (the non-negotiable invariant this project's own containment test enforces).
/// </summary>
public static class InterruptionLoadCheck
{
    public static InterruptionLoad Evaluate(
        IReadOnlyList<PermissionPromptOutcome> permissionPrompts,
        IReadOnlyList<QuestionOutcome> questions)
    {
        ArgumentNullException.ThrowIfNull(permissionPrompts);
        ArgumentNullException.ThrowIfNull(questions);

        return new InterruptionLoad
        {
            PermissionPromptCount = permissionPrompts.Count,
            PermissionPromptsWithOutcome = permissionPrompts.Count(prompt => prompt.ResultKind is not null),
            QuestionCount = questions.Count,
        };
    }
}
