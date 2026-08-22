namespace AecoPostMortem.Rules;

/// <summary>
/// One turn, whether or not it aborted — the check needs every turn in a session to place an
/// abort's <see cref="AbortedTurnOccurrence.Position"/> among the turns around it, not only the
/// turns that aborted. Plain input, no tool name (Repo Rule 6): this project cannot see
/// <c>AecoPostMortem.Data.Execution.Turn</c>, so the caller reduces it to exactly this shape.
/// </summary>
public sealed record TurnRecord
{
    public required string SessionId { get; init; }

    /// <summary>
    /// The id of the event that opened this turn — opaque here, and the only field on this record
    /// that identifies the turn. Paired with <see cref="SessionId"/> it is unique; on its own it is
    /// not assumed to be. <see cref="TurnId"/> is deliberately <em>not</em> an identity (see below),
    /// so every ordering tiebreak and every downstream key is built from this field instead.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// The turn number the session itself displays — carried for reporting only, never as an
    /// identity: it is a small counter that cycles and repeats <em>within</em> a single session, so
    /// two different turns of one session routinely carry the same value. Use <see cref="EventId"/>
    /// to tell two turns apart.
    /// </summary>
    public required string TurnId { get; init; }

    public required string StartedAt { get; init; }

    public required bool Aborted { get; init; }

    /// <summary>Set only when <see cref="Aborted"/> is true — mirrors
    /// <c>AecoPostMortem.Data.Execution.Turn.AbortReason</c>'s own nullability rule.</summary>
    public string? AbortReason { get; init; }
}

/// <summary>
/// One aborted turn's position among the turns of its own session — 1-based, paired with the
/// session's own total turn count the same way <see cref="HookFailureCounts"/> pairs a count with
/// its population, so "position 3" never renders without "of how many" beside it.
/// </summary>
public sealed record AbortedTurnOccurrence
{
    public required string SessionId { get; init; }

    /// <summary>This occurrence's identity, carried straight from <see cref="TurnRecord.EventId"/>.
    /// A caller building a per-abort key pairs it with <see cref="SessionId"/>; pairing with
    /// <see cref="TurnId"/> instead would merge distinct aborts, since that value repeats within a
    /// session.</summary>
    public required string EventId { get; init; }

    /// <summary>Reporting only — see <see cref="TurnRecord.TurnId"/>.</summary>
    public required string TurnId { get; init; }

    public required string Reason { get; init; }

    public required int Position { get; init; }

    public required int SessionTurnCount { get; init; }
}

/// <summary>
/// FR-18's check shape (issue #28, S-16): finds every aborted turn and states where it fell among
/// the turns of its own session. Turns are ordered by <see cref="TurnRecord.StartedAt"/>, with
/// <see cref="TurnRecord.EventId"/> (ordinal string comparison) breaking a tie deterministically, so
/// two runs over the same input always agree on position (PRD §3.8). The tiebreak is the event id
/// rather than <see cref="TurnRecord.TurnId"/> precisely because a display counter is the field two
/// turns of one session are most likely to share — tie-breaking on it can leave a genuine tie
/// unbroken, which is the non-determinism the tiebreak exists to remove. Pure: takes every turn
/// considered, aborted or not, and reports only the ones that aborted — the invariant in this
/// project's CLAUDE.md.
/// </summary>
public static class AbortedTurnCheck
{
    public static IReadOnlyList<AbortedTurnOccurrence> Run(IReadOnlyList<TurnRecord> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        return turns
            .GroupBy(turn => turn.SessionId, StringComparer.Ordinal)
            .SelectMany(session =>
            {
                var ordered = session
                    .OrderBy(turn => turn.StartedAt, StringComparer.Ordinal)
                    .ThenBy(turn => turn.EventId, StringComparer.Ordinal)
                    .ToArray();

                return ordered
                    .Select((turn, index) => (Turn: turn, Position: index + 1))
                    .Where(entry => entry.Turn.Aborted)
                    .Select(entry => new AbortedTurnOccurrence
                    {
                        SessionId = entry.Turn.SessionId,
                        EventId = entry.Turn.EventId,
                        TurnId = entry.Turn.TurnId,
                        Reason = entry.Turn.AbortReason ?? string.Empty,
                        Position = entry.Position,
                        SessionTurnCount = ordered.Length,
                    });
            })
            .OrderBy(occurrence => occurrence.SessionId, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.Position)
            .ToArray();
    }
}
