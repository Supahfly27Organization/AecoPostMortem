namespace AecoPostMortem.Data.Execution;

/// <summary>
/// One Copilot session. The scope every other derived entity is keyed within, which is why it is
/// the only one of the eight that carries no ownership: it is the scope rather than a thing owned.
/// </summary>
/// <remarks>
/// The token figures come from <c>session.shutdown.data.modelMetrics</c>, measured present on 31 of
/// 35 sessions — so they are nullable and never zero-filled, because a zero is a number a surface
/// would print. They are summed across models; <see cref="ModelCount"/> says how many were summed.
/// The per-model breakdown is a known gap, recorded in the design rather than hidden.
/// </remarks>
public sealed record Session : IDerivedEntity
{
    public required string SessionId { get; init; }

    public required string StartedAt { get; init; }

    /// <summary>Null when the session never wrote <c>session.shutdown</c> — measured 31 of 35 did.</summary>
    public string? EndedAt { get; init; }

    public required string CopilotVersion { get; init; }

    public required string EventSchemaVersion { get; init; }

    public required string SourceFile { get; init; }

    // session.start.data.context, measured present on 35 of 35 sessions.
    public required string Cwd { get; init; }

    public string? GitRoot { get; init; }

    public string? Branch { get; init; }

    public string? HeadCommit { get; init; }

    public string? Repository { get; init; }

    public string? HostType { get; init; }

    public string? BaseCommit { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? CacheReadTokens { get; init; }

    public long? CacheWriteTokens { get; init; }

    public long? ReasoningTokens { get; init; }

    public int? ModelCount { get; init; }
}
