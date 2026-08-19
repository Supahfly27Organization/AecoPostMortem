using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// The story's own edge case: <c>subagent.completed</c> carries tokens and duration on a measured
/// 215 of 462 completions, so "completed, cost unknown" has to be distinguishable from "did not
/// complete" — and neither may be readable as zero tokens.
/// </summary>
public sealed class AgentOutcomeTests
{
    [Fact]
    public void The_four_outcomes_are_distinct_states()
    {
        Assert.Equal(4, Enum.GetValues<AgentOutcome>().Length);
        Assert.Contains(AgentOutcome.Running, Enum.GetValues<AgentOutcome>());
        Assert.Contains(AgentOutcome.Completed, Enum.GetValues<AgentOutcome>());
        Assert.Contains(AgentOutcome.CompletedCostUnknown, Enum.GetValues<AgentOutcome>());
        Assert.Contains(AgentOutcome.Failed, Enum.GetValues<AgentOutcome>());
    }

    [Fact]
    public void A_cost_unknown_completion_reports_absence_rather_than_zero()
    {
        var agent = Spawned() with { Outcome = AgentOutcome.CompletedCostUnknown };

        Assert.Null(agent.TotalTokens);
        Assert.NotEqual(0, agent.TotalTokens ?? -1);
    }

    /// <summary>Enforced by the database: metrics may only accompany a priced completion.</summary>
    [Theory]
    [InlineData(AgentOutcome.CompletedCostUnknown, 1000L)]
    [InlineData(AgentOutcome.Running, 1000L)]
    [InlineData(AgentOutcome.Failed, 1000L)]
    public void Metrics_on_any_outcome_but_completed_are_refused(AgentOutcome outcome, long tokens)
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Agents.Add(Spawned() with { Outcome = outcome, TotalTokens = tokens });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_priced_completion_is_accepted()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Agents.Add(Spawned() with
        {
            Outcome = AgentOutcome.Completed,
            TotalTokens = 1000,
            TotalToolCalls = 7,
            DurationMs = 4200,
            Model = "claude-opus-5",
        });
        context.SaveChanges();

        Assert.Equal(1000, context.Agents.Single().TotalTokens);
    }

    static Agent Spawned() => new()
    {
        SessionId = "session-1",
        AgentId = "call_42",
        SpawningToolCallId = "call_42",
        Name = "explore",
        DisplayName = "Explore",
        StartedAt = "2026-08-09T20:14:36.758Z",
        Outcome = AgentOutcome.Running,
    };
}
