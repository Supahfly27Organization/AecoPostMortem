using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// One rule operand as S-23 resolved it (FR-31, issue #37), paired with the calls that resolution
/// produced. <see cref="Layer"/> is <see cref="OperandResolutionLayer"/> itself rather than a string
/// a caller could invent: the four layers are the only answers FR-31 admits, and
/// <see cref="OperandResolutionLayer.Unresolved"/> is one of them — an operand nothing matched still
/// appears on a figure, with a zero <see cref="CallCount"/>, because dropping it would silently
/// shrink the denominator.
/// </summary>
public sealed record OperandResolution
{
    public required string OperandText { get; init; }

    public required OperandResolutionLayer Layer { get; init; }

    public required int CallCount { get; init; }
}

/// <summary>
/// FR-33 (S-24, issue #38): an adherence percentage and the resolution that produced it, as one
/// type. A measured fivefold spread on one rule came from the resolution choice alone, so a figure
/// served without its per-operand layers is not a weaker figure — it is an unreadable one.
///
/// <para>The refusal is structural rather than validated: <see cref="Percentage"/> is a computed
/// property over <see cref="Adherent"/> and <see cref="Divergent"/>, with no setter, so a percentage
/// cannot exist apart from the operand call counts that produced it, and cannot disagree with them.
/// <see cref="RuleVersion"/>, <see cref="Adherent"/> and <see cref="Divergent"/> are all
/// <c>required</c>, so an object initializer omitting any of them is a compile error (CS9035) — the
/// same guarantee <c>Finding.Provenance</c> gives (issue #23) and the same reasoning
/// <c>Rules.FailureRate</c> gives for its own computed percentage. This is what lets
/// <c>Api.FindingEnvelope.Adherence</c> carry exactly one member and still satisfy FR-33: there is no
/// bare figure to refuse at run time, because none can be constructed.</para>
///
/// <para><see cref="Adherent"/> is a single operand rather than a second list because a rule's
/// adherent side is what the rule mandates — FR-32's <c>prefer-A-over-B</c> shape, where A is one
/// operand and everything counted against it is B. Making it a non-nullable <c>required</c> member
/// rather than the head of one list is also what makes "at least one operand's layer is always
/// stated" true in the type system: an empty list is representable, a missing required member is
/// not.</para>
/// </summary>
public sealed record AdherenceFigure
{
    /// <summary>FR-27/FR-28 (S-20, issue #33): the rule-set version this figure was computed
    /// within. Carried as <see cref="RuleSetVersionId"/> — repository plus content hash — rather
    /// than a display string, so S-35's Monitor comparison can tell whether two figures were even
    /// scoped to the same rule set before comparing them.</summary>
    public required RuleSetVersionId RuleVersion { get; init; }

    /// <summary>The operand the rule mandates, and the calls that followed it.</summary>
    public required OperandResolution Adherent { get; init; }

    /// <summary>Every operand whose calls count against the rule. Empty is a real case — a rule with
    /// nothing to diverge to reads 100% once it has any adherent call at all.</summary>
    public required IReadOnlyList<OperandResolution> Divergent { get; init; }

    /// <summary>Scenario 1's "the layer used per operand and the resulting call counts": every
    /// operand on this figure, adherent side first. Computed, so it can neither be omitted nor
    /// disagree with the two members it is derived from.</summary>
    public IReadOnlyList<OperandResolution> Operands => [Adherent, .. Divergent];

    public int AdherentCalls => Adherent.CallCount;

    public int TotalCalls => Operands.Sum(operand => operand.CallCount);

    /// <summary>
    /// <c>null</c>, never <c>0</c>, when the rule had no calls either way. PRD §5.5 tolerates zero
    /// occurrences, so the figure still ships — with its operands and their layers stated — and says
    /// plainly that there is no percentage, rather than reporting 0% of nothing. The same rule
    /// <c>Guardrail</c> follows for a share with no adjudicated findings behind it.
    /// </summary>
    public double? Percentage => TotalCalls == 0 ? null : 100d * AdherentCalls / TotalCalls;

    /// <summary>
    /// Builds the figure from S-23's own result (issue #37) and the corpus it was resolved against,
    /// so each operand's <see cref="OperandResolution.Layer"/> is the layer that actually resolved
    /// it rather than a label chosen here. FR-32's A-wins subtraction is already applied by
    /// <see cref="OperandResolver.ResolveTwoOperands"/> — a tool both operands would claim is
    /// counted once, on the adherent side — and this method only counts calls, never re-decides
    /// which tools an operand owns.
    /// </summary>
    public static AdherenceFigure FromTwoOperands(
        TwoOperandResolution resolution,
        IEnumerable<ToolInvocationShape> invocations,
        RuleSetVersionId ruleVersion)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(invocations);
        ArgumentNullException.ThrowIfNull(ruleVersion);

        var callsByTool = invocations
            .GroupBy(invocation => invocation.ToolName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new AdherenceFigure
        {
            RuleVersion = ruleVersion,
            Adherent = Count(resolution.OperandA, callsByTool),
            Divergent = [Count(resolution.OperandB, callsByTool)],
        };
    }

    static OperandResolution Count(ResolvedOperand operand, IReadOnlyDictionary<string, int> callsByTool) => new()
    {
        OperandText = operand.OperandText,
        Layer = operand.Layer,
        CallCount = operand.Tools.Sum(tool => callsByTool.TryGetValue(tool, out var count) ? count : 0),
    };
}
