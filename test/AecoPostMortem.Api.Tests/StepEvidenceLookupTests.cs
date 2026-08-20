using AecoPostMortem.Data;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-21 part 2 of 3 (S-52, issue #16): the Raw tab is "the provenance guarantee made clickable" —
/// the literal event that produced a tape step — and the Thinking tab is the readable reasoning
/// recorded for a prompt step, where any exists. <see cref="StepEvidenceLookup.Find"/> resolves both
/// from a session's own <see cref="RawEvent"/>s, the same envelope reading
/// <c>AecoPostMortem.Ingestion.ExecutionRecordBuilder</c> already does to build the tape itself.
/// </summary>
public sealed class StepEvidenceLookupTests
{
    static RawEvent Ev(long sequence, string eventType, string payload) =>
        new("s1", sequence, eventType, "2026-08-16T10:00:00Z", "1.0.0", "events.jsonl", sequence * 100, $"hash-{sequence}", payload);

    [Fact]
    public void A_tool_calls_raw_event_is_its_own_execution_start()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        var raw = Assert.IsType<RawStepEventEnvelope.Present>(result.Raw);
        Assert.Equal("tool.execution_start", raw.EventType);
        Assert.Contains("\"toolCallId\":\"tc1\"", raw.Payload);
    }

    [Fact]
    public void A_tool_call_carries_no_thinking_it_is_recorded_per_assistant_message()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        Assert.IsType<ThinkingEnvelope.Unavailable>(result.Thinking);
    }

    [Fact]
    public void A_skill_steps_raw_event_is_matched_by_the_envelopes_own_id()
    {
        var events = new[]
        {
            Ev(1, "skill.invoked", """{"id":"sk1","data":{"name":"code-review"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Skill, "sk1");

        var raw = Assert.IsType<RawStepEventEnvelope.Present>(result.Raw);
        Assert.Equal("skill.invoked", raw.EventType);
    }

    [Fact]
    public void A_hook_steps_raw_event_is_matched_by_the_envelopes_own_id()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"name":"pre-commit"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "h1");

        var raw = Assert.IsType<RawStepEventEnvelope.Present>(result.Raw);
        Assert.Equal("hook.start", raw.EventType);
    }

    [Fact]
    public void A_prompt_steps_raw_event_is_its_own_turn_start()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "t1");

        var raw = Assert.IsType<RawStepEventEnvelope.Present>(result.Raw);
        Assert.Equal("assistant.turn_start", raw.EventType);
    }

    [Fact]
    public void A_step_with_no_matching_raw_event_is_reported_skipped_not_blank()
    {
        var events = Array.Empty<RawEvent>();

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc-missing");

        var skipped = Assert.IsType<RawStepEventEnvelope.Skipped>(result.Raw);
        Assert.NotEmpty(skipped.Reason);
    }

    [Fact]
    public void Readable_reasoning_text_within_the_turn_is_the_thinking_tabs_content()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"reasoningText":"I should check the failing test first."}}"""),
            Ev(3, "assistant.turn_end", """{"id":"e3","data":{"turnId":"t1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "t1");

        var thinking = Assert.IsType<ThinkingEnvelope.Present>(result.Thinking);
        Assert.Equal("I should check the failing test first.", thinking.Text);
    }

    [Fact]
    public void Provider_encrypted_reasoning_reads_as_unavailable_with_a_stated_reason_not_a_blank_panel()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"reasoningOpaque":"<encrypted>"}}"""),
            Ev(3, "assistant.turn_end", """{"id":"e3","data":{"turnId":"t1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "t1");

        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(result.Thinking);
        Assert.NotEmpty(unavailable.Reason);
    }

    [Fact]
    public void A_prompt_with_no_assistant_message_at_all_states_none_was_recorded()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
            Ev(2, "assistant.turn_end", """{"id":"e2","data":{"turnId":"t1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "t1");

        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(result.Thinking);
        Assert.NotEmpty(unavailable.Reason);
    }
}
