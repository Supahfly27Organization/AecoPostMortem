namespace AecoPostMortem.Rules;

/// <summary>
/// One self-declared phase label, reduced to what phase churn needs: which session declared it, the
/// phase itself, and its position in the corpus' own chronological order. <see cref="Phase"/> is an
/// opaque label the corpus supplies — this project never assumes a fixed set of phases, the same way
/// <see cref="ToolInvocationShape.ToolName"/> is never read as one of a known set (FR-19, FR-34,
/// Repo Rule 6). <see cref="AecoPostMortem.Findings"/> is where the intent-declaring tool call this
/// project must never name is reduced to this shape.
/// </summary>
public sealed record DeclaredIntent
{
    public required string SessionId { get; init; }

    public required string Phase { get; init; }

    /// <summary>This intent's position in the corpus' own chronological order — a total order across
    /// every session, not just within one. The only ordering input this project trusts, since FR-19
    /// forbids hard-coding either the phase vocabulary or its ordering.</summary>
    public required long Sequence { get; init; }
}
