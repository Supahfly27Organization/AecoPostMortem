using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-40's caller-supplied classify function (<see cref="RulesInventory.Build"/>'s own contract).
/// <see cref="RuleShapeKind.PreferAOverB"/> is the one shape this classifier actually watches: its
/// operand pair is resolved against a real <see cref="ToolInvocationShape"/> corpus via
/// <see cref="OperandResolver.ResolveTwoOperands"/>, and a match whose both operands resolve to at
/// least one real tool is <see cref="RuleStatementStatus.Watched"/>. Every other matched shape is
/// <see cref="RuleStatementStatus.CheckableNotYetBuilt"/> — <see cref="RuleShapeKind.ToolIsBanned"/>
/// deliberately included: turning a ban into a real verdict needs deciding which <see cref="ToolRole"/>
/// a banned tool "targets" for <c>ToolVocabularyMismatchCheck</c>, which nothing in this codebase has
/// ever decided (a separate design question, not wired here), and <see cref="RuleShapeKind.NeverReadPath"/>/
/// <see cref="RuleShapeKind.UseAAfterB"/>/<see cref="RuleShapeKind.AlwaysPassParam"/> have no built
/// check at all. FR-34's own two unmatched dispositions map onto the two remaining statuses this
/// piece can answer honestly: <see cref="UnmatchedStatementDisposition.CheckableNotBuilt"/> (an
/// obligation, no shape fits) is also <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>, and
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
        // RuleShapeMatch's own contract guarantees OperandBText is non-null exactly for two-operand
        // shapes, PreferAOverB among them, so this also narrows the nullable type for the call below.
        if (match.Kind != RuleShapeKind.PreferAOverB || match.OperandBText is null)
        {
            return RuleStatementStatus.CheckableNotYetBuilt;
        }

        var resolution = OperandResolver.ResolveTwoOperands(match.OperandAText, match.OperandBText, invocations);
        var bothResolved = resolution.OperandA.Layer != OperandResolutionLayer.Unresolved
            && resolution.OperandB.Layer != OperandResolutionLayer.Unresolved;

        return bothResolved ? RuleStatementStatus.Watched : RuleStatementStatus.CheckableNotYetBuilt;
    }
}
