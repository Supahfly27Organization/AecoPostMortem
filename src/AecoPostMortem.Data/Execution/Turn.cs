namespace AecoPostMortem.Data.Execution;

/// <summary>How a turn ended. A measured 2,384 turn starts against 2,375 ends means unfinished is a
/// real state rather than a defensive one.</summary>
public enum TurnOutcome
{
    Unfinished,
    Completed,
    Aborted,
}

/// <summary>
/// One assistant turn, bounded by <c>assistant.turn_start</c> and <c>assistant.turn_end</c>.
/// </summary>
/// <remarks>
/// Message text is not here. The latency research measured the Flight Recorder's tape against
/// <c>raw_event</c> directly, so NORMALIZED holds the execution skeleton and messages are read from
/// RAW.
/// </remarks>
public sealed record Turn : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    /// <summary>The key: the <c>assistant.turn_start</c> event's own envelope <c>id</c>. Measured
    /// against the live reference corpus, <c>data.turnId</c> repeats within a session on 27 of 35
    /// sessions — it is a small, cycling display counter, not a stable identity — so this entity is
    /// keyed the same way <see cref="Skill"/>/<see cref="Hook"/> already are: Copilot writes no
    /// natural id for the thing itself, so the event's own id is the local key.</summary>
    public required string EventId { get; init; }

    /// <summary>The displayed turn number Copilot itself reports — not unique within a session, and
    /// not this entity's key (see <see cref="EventId"/>). Carried verbatim for display only.</summary>
    public required string TurnId { get; init; }

    public required string StartedAt { get; init; }

    public string? EndedAt { get; init; }

    public required TurnOutcome Outcome { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="TurnOutcome.Aborted"/>.</summary>
    public string? AbortReason { get; init; }

    public long? OutputTokens { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}
