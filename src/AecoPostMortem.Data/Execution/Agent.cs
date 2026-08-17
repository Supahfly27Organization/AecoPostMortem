namespace AecoPostMortem.Data.Execution;

/// <summary>
/// What became of a subagent. <see cref="CompletedCostUnknown"/> exists because
/// <c>subagent.completed</c> carries tokens and duration on only a measured 215 of 462 completions —
/// collapsing it into <see cref="Completed"/> with zeroes would price 247 agents at nothing.
/// </summary>
public enum AgentOutcome
{
    Running,
    Completed,
    CompletedCostUnknown,
    Failed,
}

/// <summary>
/// One subagent. Its handle is <c>subagent.started.data.toolCallId</c>, which the data map measured
/// identical to the <c>agentId</c> on every event the subagent produced.
/// </summary>
/// <remarks>
/// This entity carries no <see cref="IOwned"/>: it is the owner, and its key column is already the
/// agent id.
/// </remarks>
public sealed record Agent : IDerivedEntity
{
    public required string SessionId { get; init; }

    public required string AgentId { get; init; }

    /// <summary>The <c>task</c> call that produced it — a measured 470 of 470 spawns resolve.</summary>
    public required string SpawningToolCallId { get; init; }

    /// <summary>Derived from the <c>agentId</c> on the spawning call; a measured 178 of 470 are
    /// nested, so null means "spawned from the main thread" rather than "unknown".</summary>
    public string? ParentAgentId { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required string StartedAt { get; init; }

    public required AgentOutcome Outcome { get; init; }

    public long? TotalTokens { get; init; }

    public int? TotalToolCalls { get; init; }

    public long? DurationMs { get; init; }

    public string? Model { get; init; }

    /// <summary>From <c>subagent.failed.data.error</c> — a measured 6 events across 2 sessions.</summary>
    public string? Error { get; init; }
}
