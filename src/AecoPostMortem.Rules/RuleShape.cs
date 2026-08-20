namespace AecoPostMortem.Rules;

/// <summary>
/// FR-34's catalogue of check <i>shapes</i>. A shape is a phrasing pattern, never a rule: it says
/// what kind of obligation a statement expresses, and the statement's own text supplies what the
/// obligation is about. These five are the shapes FR-34 measured firing — 8 rules across 5 shapes,
/// with nothing hard-coded — and they are listed here in the order
/// <see cref="RuleShapeCatalogue.Shapes"/> tries them.
///
/// <para>Like <see cref="ToolRole"/>, this is a closed enum with no "unmatched" member: a statement
/// that matches no shape is reported as an <see cref="UnmatchedStatement"/> with a disposition and a
/// reason, so a future <c>switch</c> over this enum needs no default case meaning "we guessed
/// wrong".</para>
/// </summary>
public enum RuleShapeKind
{
    /// <summary>A prohibition whose object is a path — "never read <c>&lt;path&gt;</c>". Tried first
    /// because it is the most specific: a prohibition <i>and</i> a path-shaped operand.</summary>
    NeverReadPath,

    /// <summary>A comparison — "prefer A over B", "use A instead of B". Two operands, and FR-32's
    /// subtraction (<see cref="OperandResolver.ResolveTwoOperands"/>) is what keeps them
    /// disjoint.</summary>
    PreferAOverB,

    /// <summary>A bare prohibition — "never use A", "A is forbidden". One operand.</summary>
    ToolIsBanned,

    /// <summary>An ordering — "use A after B", or the same fact phrased "use B before A". A is
    /// always the later step, whichever way the statement phrased it.</summary>
    UseAAfterB,

    /// <summary>An obligation on an argument — "always pass an explicit A". One operand, naming a
    /// parameter rather than a tool.</summary>
    AlwaysPassParam,
}

/// <summary>
/// One statement matched to one shape. <see cref="OperandAText"/> and <see cref="OperandBText"/> are
/// text lifted out of <see cref="Statement"/> — this project never resolves them to tools itself;
/// <see cref="OperandResolver"/> does that against whatever corpus the caller passes in, which is
/// the seam S-26 (FR-35, "this rule names a tool your agent does not have") builds on.
/// <see cref="OperandBText"/> is null exactly for the single-operand shapes.
/// </summary>
public sealed record RuleShapeMatch
{
    public required RuleStatement Statement { get; init; }

    public required RuleShapeKind Kind { get; init; }

    public required string OperandAText { get; init; }

    public string? OperandBText { get; init; }
}

/// <summary>
/// FR-40's two middle inventory statuses, which S-25's fourth scenario requires a statement matching
/// no shape to be recorded as. Neither is "dropped": both carry an
/// <see cref="UnmatchedStatement.Reason"/>.
/// </summary>
public enum UnmatchedStatementDisposition
{
    /// <summary>The statement expresses an obligation, but no shape in the catalogue matches how it
    /// is phrased — FR-40's "Checkable — not yet built".</summary>
    CheckableNotBuilt,

    /// <summary>The statement expresses no obligation this catalogue could check — FR-40's "Not
    /// checkable", which that requirement also requires to state a reason.</summary>
    NotCheckable,
}

/// <summary>
/// A statement that matched no shape, with why. <see cref="Reason"/> is never empty: S-25's fourth
/// scenario is that such a statement "is not silently dropped", and a disposition with no reason is
/// the silent drop wearing a label.
/// </summary>
public sealed record UnmatchedStatement
{
    public required RuleStatement Statement { get; init; }

    public required UnmatchedStatementDisposition Disposition { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// The result of matching a set of statements against the catalogue. Every statement handed in
/// appears in exactly one of <see cref="Matches"/> or <see cref="Unmatched"/>;
/// <see cref="StatementCount"/> is computed from the two rather than stored, so it cannot disagree
/// with them — the same reasoning <see cref="FailureRate.Percentage"/> documents.
/// </summary>
public sealed record RuleShapeMatching
{
    public required IReadOnlyList<RuleShapeMatch> Matches { get; init; }

    public required IReadOnlyList<UnmatchedStatement> Unmatched { get; init; }

    public int StatementCount => Matches.Count + Unmatched.Count;
}
