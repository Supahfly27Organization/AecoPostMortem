namespace AecoPostMortem.Findings;

/// <summary>
/// One field/value pair quoted from the event that produced a finding. A pair rather than a raw
/// string so a UI can render "the actual event fields" (PRD Part 4) as fields, not as an opaque
/// blob — the Raw tab is "the provenance guarantee made clickable," which needs to know what it is
/// pointing at.
/// </summary>
public sealed record EvidenceItem
{
    public required string Field { get; init; }

    public required string Value { get; init; }
}
