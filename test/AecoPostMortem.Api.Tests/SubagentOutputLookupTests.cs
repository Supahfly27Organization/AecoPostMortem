using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-22 (S-09, issue #18): a subagent's real report is recoverable only from its own message
/// stream — the parent's <c>read_agent</c> completion is a truncated stub (a measured 200 completions,
/// median 48 characters, ending in the literal marker <c>"(Full response provided to agent)"</c>,
/// per <c>docs/product-superpowers/discovery/2026-08-16-copilot-ingestion-data-map.md</c>).
/// <see cref="SubagentOutputLookup.Find"/> reads only <c>assistant.message</c> events carrying the
/// subagent's own <c>agentId</c> — it never reads a <c>tool.execution_complete</c> result at all, so
/// the parent's stub cannot leak through by construction, not by a filter that has to remember to
/// exclude it.
/// </summary>
public sealed class SubagentOutputLookupTests
{
    static RawEvent Ev(long sequence, string eventType, string payload) =>
        new("s1", sequence, eventType, "2026-08-16T10:00:00Z", "1.0.0", "events.jsonl", sequence * 100, $"hash-{sequence}", payload);

    static Agent BuildAgent(string agentId, AgentOutcome outcome = AgentOutcome.Completed, string? error = null) =>
        new()
        {
            SessionId = "s1",
            AgentId = agentId,
            SpawningToolCallId = agentId,
            ParentAgentId = null,
            Name = "general-purpose",
            DisplayName = "General Purpose Agent",
            StartedAt = "2026-08-16T10:00:00Z",
            Outcome = outcome,
            Error = error,
        };

    [Fact]
    public void The_last_assistant_message_under_the_agents_own_id_is_the_report()
    {
        var events = new[]
        {
            Ev(1, "assistant.message", """{"id":"e1","data":{"content":"Starting the task."},"agentId":"a1"}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"content":"Task 1 is complete. Full report follows: everything worked."},"agentId":"a1"}"""),
        };

        var result = SubagentOutputLookup.Find(events, BuildAgent("a1"));

        var present = Assert.IsType<SubagentOutputEnvelope.Present>(result);
        Assert.Equal("Task 1 is complete. Full report follows: everything worked.", present.Text);
    }

    [Fact]
    public void The_parents_truncated_read_agent_result_is_never_shown_as_the_output()
    {
        var events = new[]
        {
            Ev(1, "assistant.message", """{"id":"e1","data":{"content":"Here is the subagent's real, much longer report about everything it did."},"agentId":"a1"}"""),
            Ev(2, "tool.execution_complete", """{"id":"e2","data":{"toolName":"read_agent","result":{"content":"Perfect! Task 1 is complete.\n\n(Full response provided to agent)"}}}"""),
        };

        var result = SubagentOutputLookup.Find(events, BuildAgent("a1"));

        var present = Assert.IsType<SubagentOutputEnvelope.Present>(result);
        Assert.Equal("Here is the subagent's real, much longer report about everything it did.", present.Text);
        Assert.DoesNotContain("Full response provided to agent", present.Text);
    }

    [Fact]
    public void A_subagent_with_no_messages_of_its_own_states_no_output_was_recorded()
    {
        var events = new[]
        {
            Ev(1, "assistant.message", """{"id":"e1","data":{"content":"Main thread narration."}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"content":"A different subagent's report."},"agentId":"a2"}"""),
        };

        var result = SubagentOutputLookup.Find(events, BuildAgent("a1"));

        Assert.IsType<SubagentOutputEnvelope.NotRecorded>(result);
    }

    [Fact]
    public void A_failed_subagent_reports_its_recorded_error_rather_than_any_output()
    {
        var events = new[]
        {
            Ev(1, "assistant.message", """{"id":"e1","data":{"content":"Some partial progress."},"agentId":"a1"}"""),
        };

        var result = SubagentOutputLookup.Find(events, BuildAgent("a1", AgentOutcome.Failed, "MCP tool timed out"));

        var failed = Assert.IsType<SubagentOutputEnvelope.Failed>(result);
        Assert.Equal("MCP tool timed out", failed.Error);
    }

    [Fact]
    public void A_failed_subagent_with_no_recorded_error_still_states_the_failure()
    {
        var result = SubagentOutputLookup.Find(Array.Empty<RawEvent>(), BuildAgent("a1", AgentOutcome.Failed));

        var failed = Assert.IsType<SubagentOutputEnvelope.Failed>(result);
        Assert.NotEmpty(failed.Error);
    }
}
