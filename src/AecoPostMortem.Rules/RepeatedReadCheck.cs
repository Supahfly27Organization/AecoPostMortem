namespace AecoPostMortem.Rules;

/// <summary>
/// One file-read event: a session and the path it opened. Generic by design — this project may
/// name no tool (FR-34), so the caller decides what counts as a "read" and passes only session and
/// path through. The vocabulary/role derivation that will eventually classify tool calls as reads
/// (S-21) produces exactly this shape; it is not a dependency of this check.
/// </summary>
public sealed record ReadEvent
{
    public required string SessionId { get; init; }

    public required string Path { get; init; }
}

/// <summary>One path a session opened at least <see cref="RepeatedReadCheck.Threshold"/> times.</summary>
public sealed record RepeatedReadOccurrence
{
    public required string SessionId { get; init; }

    public required string Path { get; init; }

    public required int ReadCount { get; init; }
}

/// <summary>
/// FR-15's check shape: group read events per (session, path) and report the groups that meet or
/// exceed the repeat threshold. Pure — it takes read events in and returns groupings out, with no
/// knowledge of storage, tools, or what produced the events (the invariant in this project's
/// CLAUDE.md).
/// </summary>
public static class RepeatedReadCheck
{
    /// <summary>"Four or more" per FR-15 / issue #25. The acceptance criteria state the same
    /// threshold two ways — "four or more times" and "more than three times" — so there is exactly
    /// one constant, not two conditions that could drift apart.</summary>
    public const int Threshold = 4;

    public static IReadOnlyList<RepeatedReadOccurrence> Run(IReadOnlyList<ReadEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .GroupBy(readEvent => (readEvent.SessionId, readEvent.Path))
            .Where(group => group.Count() >= Threshold)
            .Select(group => new RepeatedReadOccurrence
            {
                SessionId = group.Key.SessionId,
                Path = group.Key.Path,
                ReadCount = group.Count(),
            })
            .OrderBy(occurrence => occurrence.Path, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.SessionId, StringComparer.Ordinal)
            .ToArray();
    }
}
