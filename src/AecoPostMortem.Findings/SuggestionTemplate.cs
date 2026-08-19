namespace AecoPostMortem.Findings;

/// <summary>
/// FR-56: a deterministic template bound to a check shape. <see cref="CheckId"/> identifies the
/// check the same abstract way <see cref="CheckRegistryEntry.CheckId"/> does — the check-shape
/// catalogue in <c>AecoPostMortem.Rules</c> is open-ended, so this is a string, not an enum.
/// <see cref="Format"/> names its operands as <c>{PlaceholderName}</c> tokens; <see
/// cref="SuggestionRenderer"/> resolves each token against the finding's own evidence field names
/// and its resolution, never against anything else, which is what keeps the render deterministic.
/// </summary>
public sealed record SuggestionTemplate
{
    public required string CheckId { get; init; }

    public required string Format { get; init; }
}
