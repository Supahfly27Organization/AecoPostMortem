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

    public required string TurnId { get; init; }

    public required string Reason { get; init; }

    public required int Position { get; init; }

    public required int SessionTurnCount { get; init; }
}

/// <summary>
/// FR-18's check shape (issue #28, S-16): finds every aborted turn and states where it fell among
/// the turns of its own session. Turns are ordered by <see cref="TurnRecord.StartedAt"/>, with
/// <see cref="TurnRecord.TurnId"/> (ordinal string comparison) breaking a tie deterministically, so
/// two runs over the same input always agree on position (PRD §3.8). Pure: takes every turn
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
                    .ThenBy(turn => turn.TurnId, StringComparer.Ordinal)
                    .ToArray();

                return ordered
                    .Select((turn, index) => (Turn: turn, Position: index + 1))
                    .Where(entry => entry.Turn.Aborted)
                    .Select(entry => new AbortedTurnOccurrence
                    {
                        SessionId = entry.Turn.SessionId,
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
