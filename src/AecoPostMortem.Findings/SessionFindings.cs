namespace AecoPostMortem.Findings;

/// <summary>
/// One finding chip on the Flight Recorder's chip row (FR-21 part 2 of 3, S-52, issue #16): the
/// finding itself, plus how many sessions across the corpus it affects. Reuses
/// <see cref="ProcessDigest.SessionsAffected"/> verbatim rather than inventing a second,
/// check-specific "count" concept — every check already reports a per-occurrence figure through its
/// own <see cref="Finding.Evidence"/> (e.g. <c>RepeatedFileReadFindingCheck</c>'s
/// <c>read_count:&lt;sessionId&gt;</c> item), and this chip's own number is deliberately the one
/// figure every finding class already carries the same way, not a per-check evidence lookup this
/// generic surface would have to special-case per class.
/// </summary>
public sealed record SessionFindingChip
{
    public required Finding Finding { get; init; }

    public required int SessionsAffected { get; init; }
}

/// <summary>
/// FR-21 part 2 of 3 (S-52, issue #16): the findings that affect one session — a different data path
/// from <see cref="SessionRecording"/>. A finding "affects" a session exactly when its
/// <see cref="Recurrence"/> carries a <see cref="RecurrenceOccurrence"/> naming that session (FR-57),
/// the same join <see cref="ProcessDigest"/> relies on to rank by sessions affected. Both an Inferred
/// finding and a rejected one still show here — provenance-and-response are for a chip's own
/// styling/interaction to read, not for this join to filter on.
/// </summary>
public sealed record SessionFindings
{
    public required IReadOnlyList<SessionFindingChip> Chips { get; init; }

    /// <summary>Takes <paramref name="findings"/> as a plain, already-resolved input — the same
    /// "not yet wired to a live corpus" pattern <see cref="ProcessDigest.Build"/> documents for its
    /// own <c>findings</c> parameter (`Findings/CLAUDE.md`): nothing in this repository yet runs
    /// every check orchestrator against the live store and hands the results here, so a caller
    /// supplies whatever it already has (today, an empty list from <c>AecoPostMortem.Api</c>).</summary>
    public static SessionFindings For(string sessionId, IReadOnlyList<Finding> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(findings);

        var chips = findings
            .Where(finding => finding.Recurrence.Occurrences.Any(
                occurrence => string.Equals(occurrence.SessionId, sessionId, StringComparison.Ordinal)))
            .Select(finding => new SessionFindingChip
            {
                Finding = finding,
                SessionsAffected = ProcessDigest.SessionsAffected(finding),
            })
            .ToList();

        return new SessionFindings { Chips = chips };
    }
}
