using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-40's caller-supplied classify function (<see cref="RulesInventory.Build"/>'s own contract).
/// <see cref="RuleShapeKind.PreferAOverB"/>, <see cref="RuleShapeKind.ToolIsBanned"/> and
/// <see cref="RuleShapeKind.UseAAfterB"/> are the shapes this classifier actually watches:
/// a <c>PreferAOverB</c> or <c>UseAAfterB</c> match's operand pair is resolved via
/// <see cref="OperandResolver.ResolveTwoOperands"/> and watched when both operands resolve; a
/// <c>ToolIsBanned</c> match's single operand is resolved via <see cref="OperandResolver.Resolve"/>
/// and watched when that one operand resolves — no <see cref="ToolRole"/> involved, since
/// <c>BannedToolCheck</c> (<c>Rules/CLAUDE.md</c>) answers "was the named tool called at all" rather
/// than a role comparison, the question <c>ToolVocabularyMismatchCheck</c>'s own <c>TargetRole</c>
/// exists for. <see cref="RuleShapeKind.NeverReadPath"/> and <see cref="RuleShapeKind.AlwaysPassParam"/>
/// are both watched unconditionally instead (below) — neither operand is a tool name, so neither has
/// an "unresolved" state to gate on. Every other matched shape is
/// <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>. FR-34's own two unmatched
/// dispositions map onto the two remaining statuses this piece can answer honestly:
/// <see cref="UnmatchedStatementDisposition.CheckableNotBuilt"/> (an obligation, no shape fits) is
/// also <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>, and
/// <see cref="UnmatchedStatementDisposition.NotCheckable"/> (no obligation at all) is
/// <see cref="RuleStatementStatus.NotARule"/>. The caller-supplied <c>NotCheckable(reason)</c> stays
/// unreachable from this classifier — no shape's absence is attributed to what the logs cannot
/// record, only to what no check yet watches.
/// </summary>
public static class RulesInventoryClassifier
{
    public static Func<RuleStatement, RuleStatementStatus> BuildClassifier(
        RuleShapeMatching matching, IReadOnlyList<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(matching);
        ArgumentNullException.ThrowIfNull(invocations);

        var byStatement = new Dictionary<RuleStatement, RuleStatementStatus>();

        foreach (var match in matching.Matches)
        {
            byStatement[match.Statement] = ClassifyMatch(match, invocations);
        }

        foreach (var unmatched in matching.Unmatched)
        {
            byStatement[unmatched.Statement] = unmatched.Disposition switch
            {
                UnmatchedStatementDisposition.CheckableNotBuilt => RuleStatementStatus.CheckableNotYetBuilt,
                UnmatchedStatementDisposition.NotCheckable => RuleStatementStatus.NotARule,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(matching), unmatched.Disposition, "Unknown unmatched-statement disposition."),
            };
        }

        return statement => byStatement.TryGetValue(statement, out var status)
            ? status
            : throw new InvalidOperationException(
                $"No classification was computed for \"{statement.Text}\" — it was not part of the "
                + "matching this classifier was built from.");
    }

    static RuleStatementStatus ClassifyMatch(RuleShapeMatch match, IReadOnlyList<ToolInvocationShape> invocations)
    {
        if (match.Kind is RuleShapeKind.NeverReadPath or RuleShapeKind.AlwaysPassParam)
        {
            // Unlike a tool-name operand, a path operand (NeverReadPath) or an argument-key operand
            // (AlwaysPassParam) always produces a determinate present/absent verdict against its own
            // corpus (Rules/NeverReadPathCheck.cs, Rules/AlwaysPassParamCheck.cs) — there is no
            // "unresolved" state to fall through to, so a matched statement is Watched
            // unconditionally rather than gated on this classifier's invocation corpus.
            return RuleStatementStatus.Watched;
        }

        if (match.Kind == RuleShapeKind.ToolIsBanned)
        {
            var resolved = OperandResolver.Resolve(match.OperandAText, invocations);
            return resolved.Layer != OperandResolutionLayer.Unresolved
                ? RuleStatementStatus.Watched
                : RuleStatementStatus.CheckableNotYetBuilt;
        }

        // RuleShapeMatch's own contract guarantees OperandBText is non-null exactly for two-operand
        // shapes, PreferAOverB and UseAAfterB among them, so this also narrows the nullable type for
        // the call below. Both shapes classify identically: Watched only when both operands resolve.
        if (match.Kind is not (RuleShapeKind.PreferAOverB or RuleShapeKind.UseAAfterB) || match.OperandBText is null)
        {
            return RuleStatementStatus.CheckableNotYetBuilt;
        }

        var resolution = OperandResolver.ResolveTwoOperands(match.OperandAText, match.OperandBText, invocations);
        var bothResolved = resolution.OperandA.Layer != OperandResolutionLayer.Unresolved
            && resolution.OperandB.Layer != OperandResolutionLayer.Unresolved;

        return bothResolved ? RuleStatementStatus.Watched : RuleStatementStatus.CheckableNotYetBuilt;
    }
}
