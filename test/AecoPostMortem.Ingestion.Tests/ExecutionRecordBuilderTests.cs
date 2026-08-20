using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Issue #7 / S-06 (FR-8, FR-9): rebuilding one session's events into turns, tool calls and agents
/// with correct ownership. Each test below is one of the story's own Gherkin scenarios.
/// </summary>
public sealed class ExecutionRecordBuilderTests
{
    const string SessionId = "session-1";

    static RawEvent Event(
        long sequence,
        string type,
        string id,
        string? parentId,
        string? agentId,
        object data,
        string timestamp = "2026-05-07T00:00:00Z")
    {
        var envelope = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["ts"] = timestamp,
            ["id"] = id,
            ["parentId"] = parentId,
            ["data"] = data,
        };

        if (agentId is not null)
        {
            envelope["agentId"] = agentId;
        }

        var payload = JsonSerializer.Serialize(envelope);
        return new RawEvent(SessionId, sequence, type, timestamp, "1.0.0", "events.jsonl", sequence, $"hash-{sequence}", payload);
    }

    [Fact]
    public void Causality_is_reconstructed_from_the_envelope()
    {
        var events = new[]
        {
            Event(0, "session.start", "e0", null, null, new { copilotVersion = "1.0.0", version = 1 }),
            Event(1, "assistant.turn_start", "e1", "e0", null, new { turnId = "turn-1" }),
            Event(2, "tool.execution_start", "e2", "e1", null, new { toolCallId = "tc-1", toolName = "view", arguments = new { path = "/a" } }),
            Event(3, "assistant.turn_end", "e3", "e2", null, new { turnId = "turn-1" }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        // Every event's id and parentId form a chain across the whole session.
        Assert.Equal(events.Length, record.Causality.Count);
        Assert.Equal(new Dictionary<string, string?>
        {
            ["e0"] = null,
            ["e1"] = "e0",
            ["e2"] = "e1",
            ["e3"] = "e2",
        }, record.Causality);
        foreach (var (id, parentId) in record.Causality)
        {
            Assert.True(parentId is null || record.Causality.ContainsKey(parentId), $"{id}'s parent {parentId} is not in the chain");
        }

        // Turn boundaries come from the turn_start / turn_end pair.
        var turn = Assert.Single(record.Turns);
        Assert.Equal("turn-1", turn.TurnId);
        Assert.Equal(TurnOutcome.Completed, turn.Outcome);
        Assert.Equal(events[1].Timestamp, turn.StartedAt);
        Assert.Equal(events[3].Timestamp, turn.EndedAt);
    }

    [Fact]
    public void A_subagents_work_is_attributed_to_it_not_to_the_main_thread()
    {
        var events = new[]
        {
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "tc-task", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-1", new { toolCallId = "agent-1", agentName = "explorer", agentDisplayName = "Explorer", agentDescription = "looks around" }),
            Event(2, "tool.execution_start", "e2", "e1", "agent-1", new { toolCallId = "tc-agent-view", toolName = "view", arguments = new { path = "/agent-path" } }),
            Event(3, "tool.execution_start", "e3", "e2", null, new { toolCallId = "tc-main-view", toolName = "view", arguments = new { path = "/main-path" } }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var agentOwned = Assert.Single(record.ToolCalls, tc => tc.ToolCallId == "tc-agent-view");
        Assert.Equal(OwnerKind.Agent, agentOwned.OwnerKind);
        Assert.Equal("agent-1", agentOwned.AgentId);

        var mainOwned = Assert.Single(record.ToolCalls, tc => tc.ToolCallId == "tc-main-view");
        Assert.Equal(OwnerKind.Main, mainOwned.OwnerKind);
        Assert.Null(mainOwned.AgentId);
    }

    [Fact]
    public void Every_spawn_resolves_to_its_spawning_call()
    {
        var events = new[]
        {
            // The task call's own toolCallId is the value that later shows up as the subagent's
            // handle - subagent.started.data.toolCallId - not a separately allocated agent id.
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "agent-1", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-1", new { toolCallId = "agent-1", agentName = "explorer", agentDisplayName = "Explorer" }),
            // No matching task tool.execution_start for this one - an unresolvable spawn.
            Event(2, "subagent.started", "e2", null, "agent-orphan", new { toolCallId = "agent-orphan", agentName = "ghost", agentDisplayName = "Ghost" }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var resolved = Assert.Single(record.Agents);
        Assert.Equal("agent-1", resolved.AgentId);
        Assert.Equal("agent-1", resolved.SpawningToolCallId);

        // The unresolved spawn does not silently disappear - it is reported, not dropped.
        Assert.DoesNotContain(record.Agents, a => a.AgentId == "agent-orphan");
        Assert.Equal(2, record.SpawnResolutionCheck.Population);
        Assert.Equal(1, record.SpawnResolutionCheck.FindingCount);
    }

    [Fact]
    public void The_spawn_resolution_check_registers_itself_whether_or_not_anything_failed()
    {
        var cleanEvents = new[]
        {
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "agent-1", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-1", new { toolCallId = "agent-1", agentName = "explorer", agentDisplayName = "Explorer" }),
        };

        var cleanRecord = ExecutionRecordBuilder.Build(SessionId, cleanEvents);

        Assert.Equal(SpawnResolutionCheck.CheckId, cleanRecord.SpawnResolutionCheck.CheckId);
        Assert.Equal(CheckRunStatus.Ran, cleanRecord.SpawnResolutionCheck.Status);
        Assert.Equal(1, cleanRecord.SpawnResolutionCheck.Population);
        Assert.Equal(0, cleanRecord.SpawnResolutionCheck.FindingCount);

        var noSpawnsRecord = ExecutionRecordBuilder.Build(SessionId, []);

        Assert.Equal(SpawnResolutionCheck.CheckId, noSpawnsRecord.SpawnResolutionCheck.CheckId);
        Assert.Equal(CheckRunStatus.Ran, noSpawnsRecord.SpawnResolutionCheck.Status);
        Assert.Equal(0, noSpawnsRecord.SpawnResolutionCheck.Population);
        Assert.Equal(0, noSpawnsRecord.SpawnResolutionCheck.FindingCount);
    }

    [Fact]
    public void Nesting_is_derived_from_the_agentId_on_the_spawning_task_call()
    {
        var events = new[]
        {
            // Root agent: spawned from the main thread (no agentId on the spawning task call).
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "agent-root", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-root", new { toolCallId = "agent-root", agentName = "root", agentDisplayName = "Root" }),
            // Nested agent: the spawning task call itself carries agent-root's agentId.
            Event(2, "tool.execution_start", "e2", "e1", "agent-root", new { toolCallId = "agent-nested", toolName = "task" }),
            Event(3, "subagent.started", "e3", "e2", "agent-nested", new { toolCallId = "agent-nested", agentName = "nested", agentDisplayName = "Nested" }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var root = Assert.Single(record.Agents, a => a.AgentId == "agent-root");
        Assert.Null(root.ParentAgentId);

        var nested = Assert.Single(record.Agents, a => a.AgentId == "agent-nested");
        Assert.Equal("agent-root", nested.ParentAgentId);
    }

    [Fact]
    public void An_unmatched_abort_closes_the_open_turn_as_aborted()
    {
        var events = new[]
        {
            Event(0, "assistant.turn_start", "e0", null, null, new { turnId = "turn-1" }),
            Event(1, "abort", "e1", "e0", null, new { reason = "user_cancelled" }, timestamp: "2026-05-07T00:00:01Z"),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var turn = Assert.Single(record.Turns);
        Assert.Equal(TurnOutcome.Aborted, turn.Outcome);
        Assert.Equal("user_cancelled", turn.AbortReason);
        Assert.Equal(events[1].Timestamp, turn.EndedAt);
    }

    [Fact]
    public void A_turn_end_naming_a_different_turn_leaves_the_open_turn_open()
    {
        // Not observed in the reference corpus (measured 100% of ends matched their own start's
        // turnId), but the state machine must not silently invent a close for it either.
        var events = new[]
        {
            Event(0, "assistant.turn_start", "e0", null, null, new { turnId = "turn-1" }),
            Event(1, "assistant.turn_end", "e1", "e0", null, new { turnId = "turn-mismatched" }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var turn = Assert.Single(record.Turns);
        Assert.Equal("turn-1", turn.TurnId);
        Assert.Equal(TurnOutcome.Unfinished, turn.Outcome);
        Assert.Null(turn.EndedAt);
    }

    [Fact]
    public void A_turn_with_no_end_or_abort_is_unfinished()
    {
        var events = new[] { Event(0, "assistant.turn_start", "e0", null, null, new { turnId = "turn-1" }) };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var turn = Assert.Single(record.Turns);
        Assert.Equal(TurnOutcome.Unfinished, turn.Outcome);
        Assert.Null(turn.EndedAt);
    }

    /// <summary>RAW never discards unknown or absent JSON (FR-2) — a `turn_start` missing its own
    /// `turnId` still reaches RAW so long as it parsed. `Turn` is now keyed by its own event id, not
    /// `TurnId` (`AecoPostMortem.Data/CLAUDE.md`), so there is no longer a reason to drop this turn
    /// silently the way the identity-by-`TurnId` design once implied — it must still close and
    /// persist, with `TurnId` defaulting to empty rather than null (a `required` field).</summary>
    [Fact]
    public void A_turn_start_missing_its_own_turn_id_still_closes_with_an_empty_turn_id()
    {
        var events = new[]
        {
            Event(0, "assistant.turn_start", "e0", null, null, new { }),
            Event(1, "assistant.turn_end", "e1", "e0", null, new { }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var turn = Assert.Single(record.Turns);
        Assert.Equal(string.Empty, turn.TurnId);
        Assert.Equal(TurnOutcome.Completed, turn.Outcome);
    }

    [Fact]
    public void A_completed_tool_call_carries_success_and_a_derived_result_size()
    {
        var events = new[]
        {
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "tc-1", toolName = "view", arguments = new { path = "/a" } }),
            Event(1, "tool.execution_complete", "e1", "e0", null, new { toolCallId = "tc-1", success = false, result = new { content = "hello" } }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var toolCall = Assert.Single(record.ToolCalls);
        Assert.Equal(false, toolCall.Success);
        Assert.Equal(events[1].Timestamp, toolCall.CompletedAt);
        Assert.Equal(5, toolCall.ResultSizeBytes);
        Assert.Equal("/a", toolCall.Path);
    }

    [Fact]
    public void An_agent_completion_carrying_cost_data_is_Completed()
    {
        var events = new[]
        {
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "agent-1", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-1", new { toolCallId = "agent-1", agentName = "explorer", agentDisplayName = "Explorer" }),
            Event(2, "subagent.completed", "e2", "e1", "agent-1", new { totalTokens = 100, totalToolCalls = 2, durationMs = 500, model = "gpt-5" }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var agent = Assert.Single(record.Agents);
        Assert.Equal(AgentOutcome.Completed, agent.Outcome);
        Assert.Equal(100, agent.TotalTokens);
        Assert.Equal(2, agent.TotalToolCalls);
        Assert.Equal(500, agent.DurationMs);
        Assert.Equal("gpt-5", agent.Model);
    }

    [Fact]
    public void An_agent_completion_carrying_no_cost_fields_is_CompletedCostUnknown()
    {
        var events = new[]
        {
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "agent-1", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-1", new { toolCallId = "agent-1", agentName = "explorer", agentDisplayName = "Explorer" }),
            Event(2, "subagent.completed", "e2", "e1", "agent-1", new { }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var agent = Assert.Single(record.Agents);
        Assert.Equal(AgentOutcome.CompletedCostUnknown, agent.Outcome);
        Assert.Null(agent.TotalTokens);
        Assert.Null(agent.TotalToolCalls);
        Assert.Null(agent.DurationMs);
        Assert.Null(agent.Model);
    }

    [Fact]
    public void An_agent_with_neither_completion_nor_failure_is_Running()
    {
        var events = new[]
        {
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "agent-1", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-1", new { toolCallId = "agent-1", agentName = "explorer", agentDisplayName = "Explorer" }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var agent = Assert.Single(record.Agents);
        Assert.Equal(AgentOutcome.Running, agent.Outcome);
    }

    [Fact]
    public void An_agent_failure_carries_the_error_and_no_cost_fields()
    {
        var events = new[]
        {
            Event(0, "tool.execution_start", "e0", null, null, new { toolCallId = "agent-1", toolName = "task" }),
            Event(1, "subagent.started", "e1", "e0", "agent-1", new { toolCallId = "agent-1", agentName = "explorer", agentDisplayName = "Explorer" }),
            Event(2, "subagent.failed", "e2", "e1", "agent-1", new { error = "boom" }),
        };

        var record = ExecutionRecordBuilder.Build(SessionId, events);

        var agent = Assert.Single(record.Agents);
        Assert.Equal(AgentOutcome.Failed, agent.Outcome);
        Assert.Equal("boom", agent.Error);
        Assert.Null(agent.TotalTokens);
    }
}
