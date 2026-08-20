using AecoPostMortem.Data.Execution;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// Acceptance criterion 3: an event carrying no agent id means main thread, exactly — the data map
/// measured 115 of 115 agent ids resolving to a known subagent handle. So the shape says "main
/// thread" rather than leaving a null for a reader to guess at.
/// </summary>
public sealed class OwnershipTests
{
    [Fact]
    public void The_owner_kind_column_is_not_nullable()
    {
        using var context = new PostMortemContext();

        var ownerKind = context.Model.FindEntityType(typeof(Turn))!
            .GetProperties()
            .Single(property => property.GetColumnName() == "owner_kind");

        Assert.False(
            ownerKind.IsNullable,
            "owner_kind must be NOT NULL: a nullable one is exactly the null the criterion forbids.");
    }

    [Fact]
    public void Main_thread_ownership_carries_no_agent_id()
    {
        Assert.Equal(OwnerKind.Main, MainThreadTurn().OwnerKind);
        Assert.Null(MainThreadTurn().AgentId);
        Assert.True(MainThreadTurn().IsMainThread());
    }

    [Fact]
    public void Agent_ownership_carries_one()
    {
        var owned = MainThreadTurn() with { OwnerKind = OwnerKind.Agent, AgentId = "call_42" };

        Assert.False(owned.IsMainThread());
        Assert.Equal("call_42", owned.AgentId);
    }

    /// <summary>The pairing is enforced by the database, not by whoever writes the row.</summary>
    [Theory]
    [InlineData(OwnerKind.Main, "call_42")]
    [InlineData(OwnerKind.Agent, null)]
    public void A_mismatched_pair_is_refused_by_the_store(OwnerKind kind, string? agentId)
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Turns.Add(MainThreadTurn() with { OwnerKind = kind, AgentId = agentId });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_matched_pair_is_accepted()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Turns.Add(MainThreadTurn());
        context.SaveChanges();

        Assert.Equal(1, context.Turns.Count());
    }

    static Turn MainThreadTurn() => new()
    {
        SessionId = "session-1",
        EventId = "e1",
        TurnId = "turn-1",
        StartedAt = "2026-08-09T20:14:36.758Z",
        Outcome = TurnOutcome.Completed,
        OwnerKind = OwnerKind.Main,
    };
}
