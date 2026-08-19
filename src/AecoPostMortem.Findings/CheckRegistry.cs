namespace AecoPostMortem.Findings;

/// <summary>Scenario 5 of the finding contract (issue #23): a refused check and a check that ran and
/// found nothing are distinct states, not both zero.</summary>
public enum CheckRunStatus
{
    Ran,
    Refused,
}

/// <summary>
/// One check's outcome for one completed analysis run. <see cref="FindingCount"/> is <c>null</c> when
/// <see cref="Status"/> is <see cref="CheckRunStatus.Refused"/> and a real integer — including
/// <c>0</c> — when it is <see cref="CheckRunStatus.Ran"/>, so a refused check is never read as a
/// clean one that happened to find nothing. <see cref="CheckId"/> is an abstract identifier rather
/// than an enum: a check is open-ended — the eventual <c>AecoPostMortem.Rules</c> check-shape
/// catalogue plus PRD §3.9's special-purpose checks (contradiction, unresolvable-spawn,
/// malformed-line) — while <see cref="AecoPostMortem.Findings.FindingClass"/> is a closed set of four.
/// </summary>
public sealed record CheckRegistryEntry
{
    public required string CheckId { get; init; }

    public required CheckRunStatus Status { get; init; }

    /// <summary>The candidate set the check considered — e.g. sessions in the corpus — defined
    /// whether or not the check went on to run cleanly (Scenario 4).</summary>
    public required int Population { get; init; }

    public int? FindingCount { get; init; }

    public string? RefusalReason { get; init; }
}

/// <summary>Scenario 4 of the finding contract (issue #23): every check appears here, whether or not
/// it fired.</summary>
public sealed record CheckRegistry
{
    public required IReadOnlyList<CheckRegistryEntry> Entries { get; init; }
}
