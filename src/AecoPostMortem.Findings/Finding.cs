namespace AecoPostMortem.Findings;

/// <summary>
/// PRD §3.3's four finding classes, numbered to match that table rather than build order — §3.4.3
/// records the build order as 2, 1, 4, with 3 gated out of v1 and waiting on input.
/// </summary>
public enum FindingClass
{
    RuleAdherenceToolChoice = 1,
    Waste = 2,
    RuleAdherenceWrittenContent = 3,
    MissingCapability = 4,
}

/// <summary>PRD §3.8: the three levels the UI must render distinguishably.</summary>
public enum Provenance
{
    Observed = 1,
    Derived,
    Inferred,
}

/// <summary>
/// FR-45's three responses. A finding nobody has looked at yet is <see cref="Ignored"/> — there is
/// no separate "pending" state, because FR-45 names exactly three.
/// </summary>
public enum OperatorResponse
{
    Ignored,
    Accepted,
    Rejected,
}

/// <summary>
/// One finding. <see cref="Provenance"/> is <c>required</c>, so an object initializer that omits it
/// is a compile error (CS9035) — that is what "construction fails" means for a type with no runtime
/// validation to fail at (issue #23, Scenario 1).
/// </summary>
public sealed record Finding
{
    public required FindingClass Class { get; init; }

    public required Provenance Provenance { get; init; }

    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }

    public required Recurrence Recurrence { get; init; }

    /// <summary>FR-33: only adherence figures carry a resolution.</summary>
    public Resolution? Resolution { get; init; }

    /// <summary>FR-56: a finding class with no template ships with its evidence and no suggestion,
    /// never a generic one.</summary>
    public Suggestion? Suggestion { get; init; }

    public OperatorResponse OperatorResponse { get; init; } = OperatorResponse.Ignored;
}
