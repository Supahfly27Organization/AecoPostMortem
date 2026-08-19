using System.Reflection;
using System.Runtime.CompilerServices;

namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// The pure check logic for issue #26 (S-14, FR-16): a failure rate per tool identity, carrying
/// the counts that produced it and the session count that keeps a rarely used tool from reading
/// as a common one. Takes plain <see cref="ToolCallOutcome"/> inputs and groups by whatever tool
/// identity the operand carries — no tool name is ever written into this check (Repo Rule 6).
/// </summary>
public sealed class FailedToolCallsCheckTests
{
    [Fact]
    public void A_tool_s_rate_carries_its_failures_and_calls_together()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "some-tool", succeeded: false),
            Outcome("session-1", "some-tool", succeeded: false),
            Outcome("session-2", "some-tool", succeeded: true),
        };

        var results = FailedToolCallsCheck.Run(outcomes);

        var rate = Assert.Single(results);
        Assert.Equal("some-tool", rate.ToolIdentity);
        Assert.Equal(2, rate.FailureRate.Failures);
        Assert.Equal(3, rate.FailureRate.Calls);
    }

    [Fact]
    public void The_percentage_is_derived_from_the_counts()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "another-tool", succeeded: false),
            Outcome("session-1", "another-tool", succeeded: true),
            Outcome("session-1", "another-tool", succeeded: true),
            Outcome("session-1", "another-tool", succeeded: true),
        };

        var results = FailedToolCallsCheck.Run(outcomes);

        var rate = Assert.Single(results);
        Assert.Equal(1, rate.FailureRate.Failures);
        Assert.Equal(4, rate.FailureRate.Calls);
        Assert.Equal(25d, rate.FailureRate.Percentage, precision: 5);
    }

    [Fact]
    public void The_check_groups_by_whatever_tool_identity_the_operand_carries()
    {
        // Deliberately unusual identities — an mcp-qualified name and a made-up one — to prove the
        // grouping is generic rather than keyed off any specific, hardcoded tool name.
        var outcomes = new[]
        {
            Outcome("session-1", "mcp__weird-server__do_thing", succeeded: false),
            Outcome("session-1", "zzz-not-a-real-tool-9000", succeeded: true),
        };

        var results = FailedToolCallsCheck.Run(outcomes);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, rate => rate.ToolIdentity == "mcp__weird-server__do_thing");
        Assert.Contains(results, rate => rate.ToolIdentity == "zzz-not-a-real-tool-9000");
    }

    [Fact]
    public void Session_count_is_distinct_sessions_not_the_call_count()
    {
        // 6 calls, all inside the same 2 sessions — SessionCount must read 2, not 6, so a tool
        // hammered within a couple of sessions is not mistaken for a widely used one.
        var outcomes = new[]
        {
            Outcome("session-1", "hammered-tool", succeeded: false),
            Outcome("session-1", "hammered-tool", succeeded: false),
            Outcome("session-1", "hammered-tool", succeeded: true),
            Outcome("session-2", "hammered-tool", succeeded: false),
            Outcome("session-2", "hammered-tool", succeeded: true),
            Outcome("session-2", "hammered-tool", succeeded: true),
        };

        var results = FailedToolCallsCheck.Run(outcomes);

        var rate = Assert.Single(results);
        Assert.Equal(6, rate.FailureRate.Calls);
        Assert.Equal(2, rate.SessionCount);
    }

    /// <summary>The edge case named in issue #26: a measured 61.2% failure rate on a tool used in
    /// only 4 sessions is exactly the case where a bare percentage misleads — this shape carries
    /// the session count alongside the rate rather than a number alone.</summary>
    [Fact]
    public void A_high_rate_on_a_few_sessions_still_carries_the_session_count()
    {
        var outcomes = new List<ToolCallOutcome>
        {
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-2", "flaky-tool", succeeded: false),
            Outcome("session-3", "flaky-tool", succeeded: false),
            Outcome("session-4", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: true),
            Outcome("session-1", "flaky-tool", succeeded: true),
            Outcome("session-2", "flaky-tool", succeeded: true),
        };

        var results = FailedToolCallsCheck.Run(outcomes);

        var rate = Assert.Single(results);
        Assert.Equal(4, rate.FailureRate.Failures);
        Assert.Equal(7, rate.FailureRate.Calls);
        Assert.Equal(4, rate.SessionCount);
    }

    [Fact]
    public void A_tool_with_no_failures_still_reports_a_zero_rate_not_an_absent_one()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "clean-tool", succeeded: true),
            Outcome("session-1", "clean-tool", succeeded: true),
        };

        var results = FailedToolCallsCheck.Run(outcomes);

        var rate = Assert.Single(results);
        Assert.Equal(0, rate.FailureRate.Failures);
        Assert.Equal(0d, rate.FailureRate.Percentage);
    }

    [Fact]
    public void No_outcomes_produce_no_rates()
    {
        var results = FailedToolCallsCheck.Run([]);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(nameof(FailureRate.Failures))]
    [InlineData(nameof(FailureRate.Calls))]
    public void The_rate_s_counts_are_required_members(string propertyName)
    {
        var property = typeof(FailureRate).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void The_percentage_is_computed_never_a_settable_member()
    {
        var property = typeof(FailureRate).GetProperty(nameof(FailureRate.Percentage));

        Assert.NotNull(property);
        Assert.Null(property!.GetCustomAttribute<RequiredMemberAttribute>());
        Assert.False(
            property.CanWrite,
            "Percentage must be derived from Failures/Calls, never set independently — that is " +
            "what makes a bare percentage structurally impossible (issue #26, Scenario 1).");
    }

    [Theory]
    [InlineData(nameof(ToolFailureRate.ToolIdentity))]
    [InlineData(nameof(ToolFailureRate.FailureRate))]
    [InlineData(nameof(ToolFailureRate.SessionCount))]
    public void A_tool_s_result_requires_its_rate_and_session_count_together(string propertyName)
    {
        var property = typeof(ToolFailureRate).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    static ToolCallOutcome Outcome(string sessionId, string toolIdentity, bool succeeded) =>
        new()
        {
            SessionId = sessionId,
            ToolIdentity = toolIdentity,
            Succeeded = succeeded,
        };
}
