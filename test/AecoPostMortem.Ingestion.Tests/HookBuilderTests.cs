using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Builds one <c>Data.Execution.Hook</c> row per <c>hook.start</c>/<c>hook.end</c> pair, matched by
/// their shared <c>hookInvocationId</c> — the third piece of the derived-layer writer.
/// </summary>
public sealed class HookBuilderTests
{
    const string SessionId = "session-1";

    static RawEvent Event(
        long sequence, string type, string timestamp, string id, string? agentId, object data)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["id"] = id,
            ["data"] = data,
        };

        if (agentId is not null)
        {
            envelope["agentId"] = agentId;
        }

        var payload = JsonSerializer.Serialize(envelope);
        return new RawEvent(
            SessionId, sequence, type, timestamp, "1.0.40",
            @"~/.copilot/session-state/session-1/events.jsonl", sequence, $"hash-{sequence}", payload);
    }

    static RawEvent HookStart(long sequence, string invocationId, string timestamp, string? agentId = null) =>
        Event(sequence, "hook.start", timestamp, $"e{sequence}", agentId,
            new { hookInvocationId = invocationId, hookType = "sessionStart" });

    static RawEvent HookEnd(long sequence, string invocationId, string timestamp, bool success) =>
        Event(sequence, "hook.end", timestamp, $"e{sequence}", null,
            new { hookInvocationId = invocationId, hookType = "sessionStart", success });

    [Fact]
    public void No_hook_events_produce_no_rows()
    {
        Assert.Empty(HookBuilder.Build(SessionId, []));
    }

    [Fact]
    public void A_start_with_no_matching_end_is_still_reported_as_unfinished()
    {
        var events = new[] { HookStart(0, "inv-1", "2026-05-07T14:17:00.000Z") };

        var hook = Assert.Single(HookBuilder.Build(SessionId, events));

        Assert.Equal(SessionId, hook.SessionId);
        Assert.Equal("inv-1", hook.EventId);
        Assert.Equal("sessionStart", hook.Name);
        Assert.Equal("2026-05-07T14:17:00.000Z", hook.StartedAt);
        Assert.Null(hook.EndedAt);
        Assert.Null(hook.Success);
        Assert.Equal(OwnerKind.Main, hook.OwnerKind);
    }

    [Fact]
    public void A_matched_pair_carries_the_end_time_and_the_success_flag()
    {
        var events = new[]
        {
            HookStart(0, "inv-1", "2026-05-07T14:17:00.000Z"),
            HookEnd(1, "inv-1", "2026-05-07T14:17:31.963Z", success: false),
        };

        var hook = Assert.Single(HookBuilder.Build(SessionId, events));

        Assert.Equal("2026-05-07T14:17:00.000Z", hook.StartedAt);
        Assert.Equal("2026-05-07T14:17:31.963Z", hook.EndedAt);
        Assert.False(hook.Success);
    }

    [Fact]
    public void An_end_with_no_matching_start_produces_no_row()
    {
        var events = new[] { HookEnd(0, "inv-orphan", "2026-05-07T14:17:31.963Z", success: true) };

        Assert.Empty(HookBuilder.Build(SessionId, events));
    }

    [Fact]
    public void Pairs_are_matched_by_invocation_id_not_by_position()
    {
        var events = new[]
        {
            HookStart(0, "inv-1", "2026-05-07T14:17:00.000Z"),
            HookStart(1, "inv-2", "2026-05-07T14:18:00.000Z"),
            HookEnd(2, "inv-2", "2026-05-07T14:18:05.000Z", success: true),
            HookEnd(3, "inv-1", "2026-05-07T14:17:05.000Z", success: true),
        };

        var hooks = HookBuilder.Build(SessionId, events).ToDictionary(h => h.EventId);

        Assert.Equal("2026-05-07T14:17:05.000Z", hooks["inv-1"].EndedAt);
        Assert.Equal("2026-05-07T14:18:05.000Z", hooks["inv-2"].EndedAt);
    }

    [Fact]
    public void The_start_events_own_agent_ownership_is_used()
    {
        var events = new[]
        {
            HookStart(0, "inv-1", "2026-05-07T14:17:00.000Z", agentId: "agent-1"),
            HookEnd(1, "inv-1", "2026-05-07T14:17:05.000Z", success: true),
        };

        var hook = Assert.Single(HookBuilder.Build(SessionId, events));

        Assert.Equal(OwnerKind.Agent, hook.OwnerKind);
        Assert.Equal("agent-1", hook.AgentId);
    }
}
