namespace AecoPostMortem.Findings;

/// <summary>
/// FR-33: every adherence figure renders with the resolution that produced it — the layer used per
/// operand and the resulting call counts — because a measured fivefold spread on one rule came from
/// that choice alone. Carried on <c>Finding.Resolution</c> only where one applies.
/// </summary>
public sealed record Resolution
{
    public required string OperandLayer { get; init; }

    public required int CallCount { get; init; }
}
