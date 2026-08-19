namespace AecoPostMortem.Findings;

/// <summary>
/// FR-56: a deterministic template's rendered text, bound to a check shape and populated from the
/// same operands and resolution the finding used. Never generated — §3.8 forbids a model call. A
/// finding class with no template ships with its evidence and no suggestion, never a generic one, so
/// <c>Finding.Suggestion</c> is nullable rather than defaulting to this type.
/// </summary>
public sealed record Suggestion
{
    public required string Text { get; init; }
}
