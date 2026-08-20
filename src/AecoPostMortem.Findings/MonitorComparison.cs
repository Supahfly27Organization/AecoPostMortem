using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-39 (S-35, issue #43): "the Monitor comparison" — adherence for one rule, computed on either
/// side of an adjacent rule-set-version edit, under one shared resolution. The reference edit
/// measured 41.8% before against 71.7% after on a measured 3 and 4 sessions (PRD discovery finding
/// 4) — that demonstrates the method rather than settling the edit, which is why
/// <see cref="BeforeVersion"/> and <see cref="AfterVersion"/> are full <see cref="RuleSetVersion"/>
/// values (identity, window, <em>and</em> <see cref="RuleSetVersion.SessionCount"/>) rather than bare
/// <see cref="RuleSetVersionId"/>s: a comparison cannot be served without the sample size that
/// produced each side's percentage.
///
/// <para>"Under one stated resolution" (Scenario 1) is structural, the same way FR-33's own refusal
/// is: <see cref="Compare"/> calls <see cref="OperandResolver.ResolveTwoOperands"/> exactly once,
/// against both sides' invocations combined, and reuses that identical
/// <see cref="TwoOperandResolution"/> to build both <see cref="Before"/> and <see cref="After"/> via
/// <see cref="AdherenceFigure.FromTwoOperands"/>. The layer that resolved each operand — and which
/// tools it resolved to — therefore cannot differ between the two sides; only the call counts each
/// side's own sessions produced can.</para>
/// </summary>
public sealed record MonitorComparison
{
    public required RuleSetVersion BeforeVersion { get; init; }

    public required RuleSetVersion AfterVersion { get; init; }

    public required AdherenceFigure Before { get; init; }

    public required AdherenceFigure After { get; init; }

    /// <summary>
    /// Refuses (via <see cref="RuleSetVersionAdjacency.RequireAdjacentPair"/>) unless
    /// <paramref name="before"/> and <paramref name="after"/> are the same repository and immediately
    /// adjacent within <paramref name="versionsInRepository"/> — no averaged figure is ever offered
    /// for a pair that fails that check (Scenario 3). On success, resolves
    /// <paramref name="operandAText"/>/<paramref name="operandBText"/> once against both sides'
    /// invocations combined, then counts each side's own calls under that one resolution.
    /// </summary>
    public static MonitorComparison Compare(
        IReadOnlyList<RuleSetVersion> versionsInRepository,
        RuleSetVersionId before,
        RuleSetVersionId after,
        string operandAText,
        string operandBText,
        IReadOnlyList<ToolInvocationShape> beforeInvocations,
        IReadOnlyList<ToolInvocationShape> afterInvocations)
    {
        ArgumentNullException.ThrowIfNull(versionsInRepository);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(operandAText);
        ArgumentNullException.ThrowIfNull(operandBText);
        ArgumentNullException.ThrowIfNull(beforeInvocations);
        ArgumentNullException.ThrowIfNull(afterInvocations);

        var (beforeVersion, afterVersion) =
            RuleSetVersionAdjacency.RequireAdjacentPair(versionsInRepository, before, after);

        var resolution = OperandResolver.ResolveTwoOperands(
            operandAText, operandBText, beforeInvocations.Concat(afterInvocations));

        return new MonitorComparison
        {
            BeforeVersion = beforeVersion,
            AfterVersion = afterVersion,
            Before = AdherenceFigure.FromTwoOperands(resolution, beforeInvocations, beforeVersion.Id),
            After = AdherenceFigure.FromTwoOperands(resolution, afterInvocations, afterVersion.Id),
        };
    }
}
