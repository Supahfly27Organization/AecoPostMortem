using System.Text.RegularExpressions;
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
/// <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>.
///
/// <para>FR-34's own two unmatched dispositions map onto the two remaining statuses this piece can
/// answer honestly. <see cref="UnmatchedStatementDisposition.NotCheckable"/> (no obligation at all —
/// a heading, an index entry) is <see cref="RuleStatementStatus.NotARule"/>; do not confuse it with
/// the <em>caller-supplied</em> <see cref="RuleStatementStatus.NotCheckable"/> this file constructs
/// below, a completely different concept (a real obligation this project can never verify, not "no
/// obligation at all"). <see cref="UnmatchedStatementDisposition.CheckableNotBuilt"/> (an obligation,
/// no catalogue shape fits) is <see cref="RuleStatementStatus.CheckableNotYetBuilt"/> — except for
/// <see cref="TaskRelevanceObligation"/> below, mockup parity item #18's one narrow, real-corpus-
/// grounded carve-out: a statement whose obligation turns on whether an action was truly needed or
/// relevant <em>to the task at hand</em>, which is a judgment about intent Copilot's own event logs
/// never carry (they record which tool was called with which argument, never why), gets
/// <see cref="RuleStatementStatus.NotCheckable"/> instead. This is the first classification path in
/// this file that ever constructs it — see this class's own remarks for the real statement that
/// motivated it and why every other real-corpus neighbour of that statement stays
/// <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>.</para>
/// </summary>
public static class RulesInventoryClassifier
{
    /// <summary>
    /// Mockup parity item #18's narrow boundary: an obligation gated on whether an action was
    /// <em>needed</em>, <em>necessary</em> or <em>relevant</em> to "the task" (or "the request"/"the
    /// ticket") itself. Motivated by a real statement in this repository's own root
    /// <c>CLAUDE.md</c> — "Read ONLY files directly needed for the current task" — found by dumping
    /// every distinct statement <see cref="RuleShapeCatalogue.MatchAll"/> classifies
    /// <see cref="UnmatchedStatementDisposition.CheckableNotBuilt"/> against the live local store
    /// during this story's own investigation, not guessed. Judging whether a read (or any other call)
    /// was truly needed <em>for the task</em> requires knowing what the task was and whether that call
    /// served it — content and intent, not which tool was invoked with what argument in what order,
    /// the only vocabulary a future check over these logs could ever grow into (paths, parameters,
    /// tool names, call ordering — the five <see cref="RuleShapeKind"/>s this project already knows
    /// how to check). No extension of that vocabulary answers "was this necessary", so this is
    /// structurally different from a real, adjacent-corpus neighbour like "Do not re-read files
    /// already in context" (a repeated path is directly observable) or "Never explore the codebase
    /// broadly before starting" (a call count is directly observable) — both stay
    /// <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>, and this pattern is written narrowly
    /// enough that neither one — nor a third real neighbour, "...based on topical relevance" (which
    /// never gates on "the task" itself) — matches it.
    /// </summary>
    static readonly Regex TaskRelevanceObligation = new(
        @"\b(?:directly\s+)?(?:needed|necessary|relevant)\s+(?:for|to)\s+"
        + @"(?:the\s+|this\s+|current\s+)*(?:task|request|ticket)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    const string TaskRelevanceReason =
        "This statement asks whether an action was truly needed for the task at hand — Copilot's own "
        + "event logs record which tool was called with which argument, never why, so no future check "
        + "over those logs could ever verify a call's relevance to task intent.";

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
            byStatement[unmatched.Statement] = ClassifyUnmatched(unmatched);
        }

        return statement => byStatement.TryGetValue(statement, out var status)
            ? status
            : throw new InvalidOperationException(
                $"No classification was computed for \"{statement.Text}\" — it was not part of the "
                + "matching this classifier was built from.");
    }

    static RuleStatementStatus ClassifyUnmatched(UnmatchedStatement unmatched) => unmatched.Disposition switch
    {
        UnmatchedStatementDisposition.CheckableNotBuilt => TaskRelevanceObligation.IsMatch(unmatched.Statement.Text)
            ? RuleStatementStatus.NotCheckable(TaskRelevanceReason)
            : RuleStatementStatus.CheckableNotYetBuilt,
        UnmatchedStatementDisposition.NotCheckable => RuleStatementStatus.NotARule,
        _ => throw new ArgumentOutOfRangeException(
            nameof(unmatched), unmatched.Disposition, "Unknown unmatched-statement disposition."),
    };

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
