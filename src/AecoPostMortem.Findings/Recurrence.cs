namespace AecoPostMortem.Findings;

/// <summary>
/// FR-57's decided identity: a finding's recurrence is <c>(class, class-specific key)</c> and is
/// version-independent. A finding whose rule spans several rule-set versions is one finding, not
/// several — the per-version breakdown lives on <see cref="Occurrences"/>, an attribute of this one
/// value, so there is no constructor that could produce a second <c>Finding</c> for the same key.
/// </summary>
public sealed record Recurrence
{
    public required string Key { get; init; }

    public required IReadOnlyList<RecurrenceOccurrence> Occurrences { get; init; }
}

/// <summary>One session in which a finding's key recurred.</summary>
public sealed record RecurrenceOccurrence
{
    public required string SessionId { get; init; }

    /// <summary>Null for finding classes that carry no rule-set version, such as
    /// <c>FindingClass.Waste</c> and <c>FindingClass.MissingCapability</c>.</summary>
    public string? RuleSetVersion { get; init; }
}
