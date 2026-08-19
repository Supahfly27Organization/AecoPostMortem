namespace AecoPostMortem.Data.Execution;

/// <summary>
/// One <c>skill.invoked</c> event — a measured 794 across 31 sessions, carrying a structured skill
/// boundary rather than a token-attributed inference.
/// </summary>
public sealed record Skill : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    /// <summary>The envelope <c>id</c>, measured present on 100% of events. Copilot writes no
    /// natural id for a skill invocation, so the event's own id is the local key.</summary>
    public required string EventId { get; init; }

    public required string Name { get; init; }

    public string? Path { get; init; }

    public string? Description { get; init; }

    public string? PluginName { get; init; }

    public string? PluginVersion { get; init; }

    public required string InvokedAt { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}

/// <summary>
/// One <c>hook.start</c> / <c>hook.end</c> pair. <see cref="Success"/> is a field rather than a
/// string match — a measured 35 failures across 3,027 pairs.
/// </summary>
public sealed record Hook : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string EventId { get; init; }

    public required string Name { get; init; }

    public required string StartedAt { get; init; }

    public string? EndedAt { get; init; }

    public bool? Success { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}

/// <summary>
/// One permission request. <see cref="ResultKind"/> comes from
/// <c>permission.completed.data.result.kind</c>, an enum on Copilot rather than a string match — a
/// measured 1,033 requested against 1,031 completed, so an unanswered request is a real state.
/// </summary>
public sealed record Permission : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string EventId { get; init; }

    public required string RequestedAt { get; init; }

    public string? CompletedAt { get; init; }

    public string? ResultKind { get; init; }

    public string? ToolCallId { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}

/// <summary>
/// A unit of content the agent wrote. Published and never populated in v1: FR-36 is Phase E, gated
/// out by PRD §3.4.3. The shape exists so the stories that will consume it have something to
/// compile against.
/// </summary>
public sealed record WriteUnit : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string EventId { get; init; }

    public required string ToolCallId { get; init; }

    public required string Path { get; init; }

    public required string AddedContent { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}
