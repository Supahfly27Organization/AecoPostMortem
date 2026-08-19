namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-15 / issue #25: group read events per (session, path) and report the groups a session read
/// four or more times. Pure check-shape logic — <see cref="ReadEvent"/> is generic (a session and a
/// path), never a tool name, per the invariant in this project's CLAUDE.md.
/// </summary>
public sealed class RepeatedReadCheckTests
{
    /// <summary>Scenario 1 (issue #25): a session that opened one path four or more times is
    /// reported with its read count for that session.</summary>
    [Fact]
    public void A_path_read_four_times_in_one_session_is_reported_with_its_count()
    {
        ReadEvent[] events =
        [
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
        ];

        var result = RepeatedReadCheck.Run(events);

        var occurrence = Assert.Single(result);
        Assert.Equal("session-1", occurrence.SessionId);
        Assert.Equal("src/Foo.cs", occurrence.Path);
        Assert.Equal(4, occurrence.ReadCount);
    }

    /// <summary>Scenario 3 (issue #25): a session where no path was read more than three times
    /// produces nothing — the boundary case for the "four or more" / "more than three" threshold
    /// stated two ways in the acceptance criteria.</summary>
    [Fact]
    public void A_path_read_only_three_times_produces_nothing()
    {
        ReadEvent[] events =
        [
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
        ];

        var result = RepeatedReadCheck.Run(events);

        Assert.Empty(result);
    }

    [Fact]
    public void Reads_are_grouped_per_session_not_across_sessions()
    {
        ReadEvent[] events =
        [
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
            new() { SessionId = "session-1", Path = "src/Foo.cs" },
            new() { SessionId = "session-2", Path = "src/Foo.cs" },
            new() { SessionId = "session-2", Path = "src/Foo.cs" },
        ];

        var result = RepeatedReadCheck.Run(events);

        Assert.Empty(result);
    }

    [Fact]
    public void Different_paths_in_the_same_session_are_reported_separately()
    {
        ReadEvent[] events =
        [
            .. Enumerable.Repeat(new ReadEvent { SessionId = "session-1", Path = "a.cs" }, 4),
            .. Enumerable.Repeat(new ReadEvent { SessionId = "session-1", Path = "b.cs" }, 5),
        ];

        var result = RepeatedReadCheck.Run(events);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, o => o.Path == "a.cs" && o.ReadCount == 4);
        Assert.Contains(result, o => o.Path == "b.cs" && o.ReadCount == 5);
    }

    [Fact]
    public void The_same_path_repeated_in_two_sessions_produces_two_occurrences()
    {
        ReadEvent[] events =
        [
            .. Enumerable.Repeat(new ReadEvent { SessionId = "session-1", Path = "a.cs" }, 4),
            .. Enumerable.Repeat(new ReadEvent { SessionId = "session-2", Path = "a.cs" }, 6),
        ];

        var result = RepeatedReadCheck.Run(events);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, o => o.SessionId == "session-1" && o.ReadCount == 4);
        Assert.Contains(result, o => o.SessionId == "session-2" && o.ReadCount == 6);
    }

    [Fact]
    public void No_events_produces_no_occurrences()
    {
        var result = RepeatedReadCheck.Run([]);

        Assert.Empty(result);
    }

    [Fact]
    public void The_result_is_ordered_deterministically_by_path_then_session()
    {
        ReadEvent[] events =
        [
            .. Enumerable.Repeat(new ReadEvent { SessionId = "session-2", Path = "b.cs" }, 4),
            .. Enumerable.Repeat(new ReadEvent { SessionId = "session-1", Path = "b.cs" }, 4),
            .. Enumerable.Repeat(new ReadEvent { SessionId = "session-1", Path = "a.cs" }, 4),
        ];

        var result = RepeatedReadCheck.Run(events);

        Assert.Equal(
            [("a.cs", "session-1"), ("b.cs", "session-1"), ("b.cs", "session-2")],
            result.Select(o => (o.Path, o.SessionId)));
    }
}
