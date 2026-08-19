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
