namespace AecoPostMortem.Data.Execution;

/// <summary>
/// One tool invocation, from its <c>tool.execution_start</c> to the matching
/// <c>tool.execution_complete</c>. A measured 16,085 starts against 16,076 completions means an
/// unfinished call is a real state, so the completion fields are nullable.
/// </summary>
public sealed record ToolCall : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string ToolCallId { get; init; }

    public required string ToolName { get; init; }

    public required string StartedAt { get; init; }

    public string? CompletedAt { get; init; }

    /// <summary>From <c>tool.execution_complete.data.success</c>, measured present on 16,076 of
    /// 16,076 completions — so null means "not completed", never "completed, outcome unknown".</summary>
    public bool? Success { get; init; }

    /// <summary>The path a read or write touched, measured present on 5,201 of 5,201 <c>view</c>
    /// calls. Null for tools that name no path.</summary>
    public string? Path { get; init; }

    public long? ResultSizeBytes { get; init; }

    public string? McpServerName { get; init; }

    public string? McpToolName { get; init; }

    public string? TurnId { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}
