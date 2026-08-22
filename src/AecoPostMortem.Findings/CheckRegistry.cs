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

    /// <summary>FR-42's provenance badge (issue #46): the provenance every finding this check would
    /// have produced carries — a check has exactly one, fixed at the orchestrator (never mixed), so
    /// this is a plain, caller-stated fact rather than something derived from a finding that, on a
    /// clean run, was never built. Required for the same reason <see cref="Finding.Provenance"/> is:
    /// the compiler refuses to build an entry that omits it, rather than a runtime check catching a
    /// silently-defaulted value.</summary>
    public required Provenance Provenance { get; init; }
}

/// <summary>Scenario 4 of the finding contract (issue #23): every check appears here, whether or not
/// it fired.</summary>
public sealed record CheckRegistry
{
    public required IReadOnlyList<CheckRegistryEntry> Entries { get; init; }

    /// <summary>The size of the whole analysis scope every entry's own <see cref="CheckRegistryEntry.
    /// Population"/> was drawn from — e.g. sessions in the selected repository, further narrowed by a
    /// date-range filter — not a per-check number. FR-42's silent-check surface (issue #46, mockup
    /// parity item #6) needs this distinct fact: a date range matching zero sessions drives every
    /// entry's own <see cref="CheckRegistryEntry.Population"/> to zero simultaneously (nothing existed
    /// to build a candidate set from), which is a structurally different situation from one specific
    /// check's own narrower population happening to be zero within a real, non-empty scope (e.g. no
    /// session in scope ever called <c>report_intent</c>, so <c>phase-churn</c>'s own population is
    /// zero even though real sessions were genuinely examined). Both share the number zero; only the
    /// first is "nothing was looked at". Required, the same "a caller-stated fact, never guessed"
    /// discipline <see cref="CheckRegistryEntry.Provenance"/> already uses — a registry built without
    /// stating its own scope size is a compile error, not a silently-defaulted zero.</summary>
    public required int SessionsInScope { get; init; }
}
