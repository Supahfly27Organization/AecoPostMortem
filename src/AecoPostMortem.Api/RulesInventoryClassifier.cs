using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-40's caller-supplied classify function (<see cref="RulesInventory.Build"/>'s own contract),
/// consumed for real for the first time. Every statement <see cref="RuleShapeCatalogue.MatchAll"/>
/// matched to a shape is <see cref="RuleStatementStatus.CheckableNotYetBuilt"/> — no built check
/// watches any shape against the real corpus yet, the same gap <c>ToolVocabularyMismatchCheck</c> not
/// being wired into <c>ApiHost.GetDigest</c> documents, since doing so honestly needs a real
/// <see cref="ToolInvocationShape"/> corpus built from RAW tool-call arguments this codebase has never
/// verified against real payloads. FR-34's own two unmatched dispositions map onto the two remaining
/// statuses this narrower piece can answer honestly:
/// <see cref="UnmatchedStatementDisposition.CheckableNotBuilt"/> (an obligation, no shape fits) is
/// also <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>, and
/// <see cref="UnmatchedStatementDisposition.NotCheckable"/> (no obligation at all) is
/// <see cref="RuleStatementStatus.NotARule"/>. <see cref="RuleStatementStatus.Watched"/> and the
/// caller-supplied <c>NotCheckable(reason)</c> stay unreachable from this classifier until a real
/// check actually watches a matched shape's resolved operand.
/// </summary>
public static class RulesInventoryClassifier
{
    public static Func<RuleStatement, RuleStatementStatus> BuildClassifier(RuleShapeMatching matching)
    {
        ArgumentNullException.ThrowIfNull(matching);

        var byStatement = new Dictionary<RuleStatement, RuleStatementStatus>();

        foreach (var match in matching.Matches)
        {
            byStatement[match.Statement] = RuleStatementStatus.CheckableNotYetBuilt;
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
}
