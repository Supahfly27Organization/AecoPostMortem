using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Builds one <c>Data.Execution.Permission</c> row per <c>permission.requested</c>/
/// <c>permission.completed</c> pair, matched by their shared <c>requestId</c>.
/// </summary>
public sealed class PermissionBuilderTests
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

    static RawEvent Requested(
        long sequence, string requestId, string timestamp, string? toolCallId = null, string? agentId = null) =>
        Event(sequence, "permission.requested", timestamp, $"e{sequence}", agentId, new
        {
            requestId,
            permissionRequest = toolCallId is null ? null : new { toolCallId },
        });

    static RawEvent Completed(long sequence, string requestId, string timestamp, string kind, string toolCallId) =>
        Event(sequence, "permission.completed", timestamp, $"e{sequence}", null, new
        {
            requestId,
            toolCallId,
            result = new { kind },
        });

    [Fact]
    public void No_permission_events_produce_no_rows()
    {
        Assert.Empty(PermissionBuilder.Build(SessionId, []));
    }

    [Fact]
    public void A_matched_pair_carries_the_completion_time_and_result_kind()
    {
        var events = new[]
        {
            Requested(0, "req-1", "2026-05-07T14:18:32.245Z", toolCallId: "call_1"),
            Completed(1, "req-1", "2026-05-07T14:18:39.899Z", "approved", "call_1"),
        };

        var permission = Assert.Single(PermissionBuilder.Build(SessionId, events));

        Assert.Equal(SessionId, permission.SessionId);
        Assert.Equal("req-1", permission.EventId);
        Assert.Equal("2026-05-07T14:18:32.245Z", permission.RequestedAt);
        Assert.Equal("2026-05-07T14:18:39.899Z", permission.CompletedAt);
        Assert.Equal("approved", permission.ResultKind);
        Assert.Equal("call_1", permission.ToolCallId);
        Assert.Equal(OwnerKind.Main, permission.OwnerKind);
    }

    [Fact]
    public void A_request_with_no_matching_completion_is_still_reported_as_unresolved()
    {
        var events = new[] { Requested(0, "req-1", "2026-05-07T14:18:32.245Z", toolCallId: "call_1") };

        var permission = Assert.Single(PermissionBuilder.Build(SessionId, events));

        Assert.Null(permission.CompletedAt);
        Assert.Null(permission.ResultKind);
        Assert.Equal("call_1", permission.ToolCallId);
    }

    [Fact]
    public void A_completion_with_no_matching_request_produces_no_row()
    {
        var events = new[] { Completed(0, "req-orphan", "2026-05-07T14:18:39.899Z", "approved", "call_1") };

        Assert.Empty(PermissionBuilder.Build(SessionId, events));
    }

    [Fact]
    public void Pairs_are_matched_by_request_id_not_by_position()
    {
        var events = new[]
        {
            Requested(0, "req-1", "2026-05-07T14:18:00.000Z", toolCallId: "call_1"),
            Requested(1, "req-2", "2026-05-07T14:19:00.000Z", toolCallId: "call_2"),
            Completed(2, "req-2", "2026-05-07T14:19:05.000Z", "denied", "call_2"),
            Completed(3, "req-1", "2026-05-07T14:18:05.000Z", "approved", "call_1"),
        };

        var permissions = PermissionBuilder.Build(SessionId, events).ToDictionary(p => p.EventId);

        Assert.Equal("approved", permissions["req-1"].ResultKind);
        Assert.Equal("denied", permissions["req-2"].ResultKind);
    }

    [Fact]
    public void The_request_events_own_agent_ownership_is_used()
    {
        var events = new[] { Requested(0, "req-1", "2026-05-07T14:18:00.000Z", agentId: "agent-1") };

        var permission = Assert.Single(PermissionBuilder.Build(SessionId, events));

        Assert.Equal(OwnerKind.Agent, permission.OwnerKind);
        Assert.Equal("agent-1", permission.AgentId);
    }
}
