namespace AecoPostMortem.Rules.Tests;

public sealed class UseAAfterBCheckTests
{
    static ToolInvocationShape[] Invocations(params string[] toolNames) =>
        toolNames.Select(name => new ToolInvocationShape { ToolName = name }).ToArray();

    [Fact]
    public void A_later_call_preceded_by_an_earlier_call_in_the_same_session_is_no_violation()
    {
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "glob", StartedAt = "2026-05-07T14:00:00.000Z" },
            new() { SessionId = "s1", ToolCallId = "c2", ToolName = "rg", StartedAt = "2026-05-07T14:00:01.000Z" },
        ];

        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("rg", "glob"));

        Assert.Empty(results);
    }

    [Fact]
    public void A_later_call_with_no_earlier_call_in_the_session_is_a_violation()
    {
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "rg", StartedAt = "2026-05-07T14:00:00.000Z" },
        ];

        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("rg", "glob"));

        var violation = Assert.Single(results);
        Assert.Equal("rg", violation.LaterToolText);
        Assert.Equal("glob", violation.EarlierToolText);
        Assert.Equal(1, violation.ViolationCount);
        Assert.Equal(["s1"], violation.SessionIds);
    }

    [Fact]
    public void A_later_call_followed_later_by_the_earlier_call_is_still_a_violation()
    {
        // The prerequisite arrives in the session, but only after the later call — ordering matters,
        // not merely co-occurrence.
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "rg", StartedAt = "2026-05-07T14:00:00.000Z" },
            new() { SessionId = "s1", ToolCallId = "c2", ToolName = "glob", StartedAt = "2026-05-07T14:00:01.000Z" },
        ];

        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("rg", "glob"));

        var violation = Assert.Single(results);
        Assert.Equal(1, violation.ViolationCount);
    }

    [Fact]
    public void Ordering_follows_started_at_never_input_order()
    {
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        // Handed in with the later call first, textually — the check must still order by StartedAt.
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c2", ToolName = "rg", StartedAt = "2026-05-07T14:00:01.000Z" },
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "glob", StartedAt = "2026-05-07T14:00:00.000Z" },
        ];

        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("rg", "glob"));

        Assert.Empty(results);
    }

    [Fact]
    public void A_second_later_call_in_the_same_session_is_satisfied_by_the_same_earlier_call()
    {
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "glob", StartedAt = "2026-05-07T14:00:00.000Z" },
            new() { SessionId = "s1", ToolCallId = "c2", ToolName = "rg", StartedAt = "2026-05-07T14:00:01.000Z" },
            new() { SessionId = "s1", ToolCallId = "c3", ToolName = "rg", StartedAt = "2026-05-07T14:00:02.000Z" },
        ];

        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("rg", "glob"));

        Assert.Empty(results);
    }

    [Fact]
    public void Violations_across_two_sessions_report_both_session_ids()
    {
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "rg", StartedAt = "2026-05-07T14:00:00.000Z" },
            new() { SessionId = "s2", ToolCallId = "c2", ToolName = "rg", StartedAt = "2026-05-07T14:00:00.000Z" },
        ];

        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("rg", "glob"));

        var violation = Assert.Single(results);
        Assert.Equal(2, violation.ViolationCount);
        Assert.Equal(["s1", "s2"], violation.SessionIds);
    }

    [Fact]
    public void A_mention_whose_earlier_operand_never_resolves_is_skipped()
    {
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "rg", StartedAt = "2026-05-07T14:00:00.000Z" },
        ];
        // "glob" was never called and matches no MCP server field or ToolRole name — Unresolved.
        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("rg"));

        Assert.Empty(results);
    }

    [Fact]
    public void A_mention_whose_later_operand_never_resolves_is_skipped()
    {
        var mentions = new[]
        {
            new UseAAfterBMention { SourceText = "Use rg after glob.", LaterToolText = "rg", EarlierToolText = "glob" },
        };
        TimedToolCall[] calls =
        [
            new() { SessionId = "s1", ToolCallId = "c1", ToolName = "glob", StartedAt = "2026-05-07T14:00:00.000Z" },
        ];
        var results = UseAAfterBCheck.Run(mentions, calls, Invocations("glob"));

        Assert.Empty(results);
    }
}
