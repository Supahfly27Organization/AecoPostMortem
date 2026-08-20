namespace AecoPostMortem.Rules;

/// <summary>Plain input to <see cref="ToolVocabularyMismatchCheck"/>: one tool name a rule statement
/// named, and the role that rule targets. One value per named tool, never a list on one record — a
/// rule naming several tools is several of these, so the check can report one finding per name
/// (issue #40's edge case) instead of one aggregate finding.</summary>
public sealed record RuleToolMention
{
    public required string RuleText { get; init; }

    public required string NamedTool { get; init; }

    public required ToolRole TargetRole { get; init; }
}

/// <summary>One rule statement's named-tool mismatch. A sealed hierarchy rather than one type with
/// nullable fields: <see cref="MinorToolNamed"/> and <see cref="NonExistentToolNamed"/> carry
/// structurally different data, so there is no field that must be null for one kind and required for
/// the other — the same discipline this project's other check shapes give a paired denominator or a
/// distinct enum member instead of an optional one.</summary>
public abstract record ToolVocabularyMismatch
{
    public required string RuleText { get; init; }

    public required string NamedTool { get; init; }

    public required ToolRole TargetRole { get; init; }
}

/// <summary>The named tool exists in the corpus but is not the dominant tool of the role the rule
/// targets (Scenario 1) — the more actionable case, because the fix the corpus already points to is
/// naming the tool that already does the job.</summary>
public sealed record MinorToolNamed : ToolVocabularyMismatch
{
    public required int NamedToolCallCount { get; init; }

    public required string DominantTool { get; init; }

    public required int DominantToolCallCount { get; init; }
}

/// <summary>The named tool does not exist anywhere in the corpus (Scenario 2) — the agent this rule
/// was written for never had it.</summary>
public sealed record NonExistentToolNamed : ToolVocabularyMismatch;

/// <summary>
/// FR-35 (issue #40, S-26): flags a rule statement that names a tool your agent does not have —
/// either the name does not resolve to anything in the corpus at all, or it does but is not the
/// dominant tool of the role the rule targets. Built entirely on S-23's <see cref="OperandResolver"/>
/// and S-21's <see cref="ToolRoleDeriver"/> rather than reimplementing tool classification; takes no
/// adherence figure as input at all, so it runs and reports independently of one (Scenario 3).
/// </summary>
public static class ToolVocabularyMismatchCheck
{
    public static IReadOnlyList<ToolVocabularyMismatch> Run(
        IEnumerable<RuleToolMention> mentions,
        IEnumerable<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(mentions);
        ArgumentNullException.ThrowIfNull(invocations);

        var calls = invocations as IReadOnlyCollection<ToolInvocationShape> ?? invocations.ToList();
        var derivation = ToolRoleDeriver.Derive(calls);

        var results = new List<ToolVocabularyMismatch>();

        foreach (var mention in mentions)
        {
            var resolved = OperandResolver.Resolve(mention.NamedTool, calls);

            if (resolved.Layer == OperandResolutionLayer.Unresolved)
            {
                results.Add(new NonExistentToolNamed
                {
                    RuleText = mention.RuleText,
                    NamedTool = mention.NamedTool,
                    TargetRole = mention.TargetRole,
                });
                continue;
            }

            var dominant = derivation.Roles[mention.TargetRole].DominantTool;
            if (dominant is null || resolved.Tools.Contains(dominant.ToolName))
            {
                // No dominant tool to compare against, or the operand already resolves to it —
                // either way there is nothing to flag.
                continue;
            }

            var namedToolCallCount = calls.Count(call =>
                string.Equals(call.ToolName, mention.NamedTool, StringComparison.Ordinal));

            results.Add(new MinorToolNamed
            {
                RuleText = mention.RuleText,
                NamedTool = mention.NamedTool,
                TargetRole = mention.TargetRole,
                NamedToolCallCount = namedToolCallCount,
                DominantTool = dominant.ToolName,
                DominantToolCallCount = dominant.CallCount,
            });
        }

        return results;
    }
}
