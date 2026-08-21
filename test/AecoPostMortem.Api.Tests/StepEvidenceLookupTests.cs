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

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "e1");

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

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "e1");

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

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "e1");

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

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "e1");

        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(result.Thinking);
        Assert.NotEmpty(unavailable.Reason);
    }

    /// <summary>FR-23 (S-10, issue #19), Scenario 2: an encrypted step's reason names the model that
    /// wrote it, and carries the session's own measured readable share per model — never an average
    /// across models, and never a corpus-wide constant.</summary>
    [Fact]
    public void Provider_encrypted_reasoning_names_the_model_and_reports_the_sessions_own_readable_share_per_model()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"reasoningOpaque":"<encrypted>","model":"gpt-5.4"}}"""),
            Ev(3, "assistant.turn_end", """{"id":"e3","data":{"turnId":"t1"}}"""),
            Ev(4, "assistant.turn_start", """{"id":"e4","data":{"turnId":"t2"}}"""),
            Ev(5, "assistant.message", """{"id":"e5","data":{"reasoningText":"Considering the fix.","model":"claude-sonnet-4.5"}}"""),
            Ev(6, "assistant.turn_end", """{"id":"e6","data":{"turnId":"t2"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "e1");

        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(result.Thinking);
        Assert.Contains("gpt-5.4", unavailable.Reason);
        Assert.NotNull(unavailable.ReadabilityByModel);
        Assert.Equal(2, unavailable.ReadabilityByModel!.Count);

        var gpt = Assert.Single(unavailable.ReadabilityByModel, m => m.Model == "gpt-5.4");
        Assert.Equal(0, gpt.ReadableCount);
        Assert.Equal(1, gpt.TotalCount);
        Assert.Equal(0d, gpt.ReadableSharePercent);

        var claude = Assert.Single(unavailable.ReadabilityByModel, m => m.Model == "claude-sonnet-4.5");
        Assert.Equal(1, claude.ReadableCount);
        Assert.Equal(1, claude.TotalCount);
        Assert.Equal(100d, claude.ReadableSharePercent);
    }

    /// <summary>Mockup parity item #13 ("Prose in transcript"): resolving every prompt step's own
    /// thinking in one batch produces the identical result <see cref="StepEvidenceLookup.Find"/>
    /// would produce for each step on its own — this is a batching optimisation, not a second
    /// resolution rule.</summary>
    [Fact]
    public void Resolving_every_prompt_steps_thinking_at_once_matches_resolving_each_one_individually()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"reasoningText":"Considering the fix."}}"""),
            Ev(3, "assistant.turn_end", """{"id":"e3","data":{"turnId":"t1"}}"""),
            Ev(4, "assistant.turn_start", """{"id":"e4","data":{"turnId":"t2"}}"""),
            Ev(5, "assistant.message", """{"id":"e5","data":{"reasoningOpaque":"<encrypted>","model":"gpt-5.4"}}"""),
            Ev(6, "assistant.turn_end", """{"id":"e6","data":{"turnId":"t2"}}"""),
        };

        var byStepId = StepEvidenceLookup.FindThinkingForPromptSteps(events, ["e1", "e4"]);

        Assert.Equal(2, byStepId.Count);
        var present = Assert.IsType<ThinkingEnvelope.Present>(byStepId["e1"]);
        Assert.Equal("Considering the fix.", present.Text);
        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(byStepId["e4"]);
        Assert.Contains("gpt-5.4", unavailable.Reason);
    }

    /// <summary>A prompt step id with no matching <c>turn_start</c> at all still resolves — the
    /// batch lookup never throws for one bad id, matching <see cref="StepEvidenceLookup.Find"/>'s own
    /// "skipped, not blank" behaviour for a missing raw event.</summary>
    [Fact]
    public void A_prompt_step_id_with_no_matching_raw_event_resolves_as_unavailable_not_a_missing_entry()
    {
        var events = Array.Empty<RawEvent>();

        var byStepId = StepEvidenceLookup.FindThinkingForPromptSteps(events, ["t-missing"]);

        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(byStepId["t-missing"]);
        Assert.NotEmpty(unavailable.Reason);
    }

    /// <summary>The real defect a collision-free <c>StepId</c> closes, measured against the live
    /// 35-session reference corpus before this change: 20 of 25 sessions in the dominant repository
    /// repeat a <c>data.turnId</c> within one session, and not one of the corpus's readable-reasoning
    /// messages resolved as <c>present</c> through the served contract, because every colliding step
    /// anchored on whichever turn carried that display counter first. Anchoring on the
    /// <c>turn_start</c> envelope's own <c>id</c> gives each turn its own window.</summary>
    [Fact]
    public void Two_turn_starts_sharing_one_display_turn_id_each_resolve_their_own_reasoning()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"1"}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"reasoningOpaque":"<encrypted>","model":"gpt-5.4"}}"""),
            Ev(3, "assistant.turn_end", """{"id":"e3","data":{"turnId":"1"}}"""),
            Ev(4, "assistant.turn_start", """{"id":"e4","data":{"turnId":"1"}}"""),
            Ev(5, "assistant.message", """{"id":"e5","data":{"reasoningText":"The second turn's own reasoning."}}"""),
            Ev(6, "assistant.turn_end", """{"id":"e6","data":{"turnId":"1"}}"""),
        };

        var byStepId = StepEvidenceLookup.FindThinkingForPromptSteps(events, ["e1", "e4"]);

        // The first turn's own reason must name the model its own window's encrypted message
        // carried — a bare `Unavailable` assertion would also be satisfied by "no raw event was
        // found for this step", which is what a `turnId` match produces for an envelope id, so it
        // would pass for the wrong reason.
        var first = Assert.IsType<ThinkingEnvelope.Unavailable>(byStepId["e1"]);
        Assert.Contains("gpt-5.4", first.Reason);

        var second = Assert.IsType<ThinkingEnvelope.Present>(byStepId["e4"]);
        Assert.Equal("The second turn's own reasoning.", second.Text);
    }

    /// <summary>An empty envelope <c>id</c> is not an identity, so it matches nothing — the same
    /// discipline this change applies to <c>data.turnId</c>, one level down.
    /// <c>EventEnvelopeReader.TryRead</c> rejects a missing or non-string <c>id</c> but accepts
    /// <c>"id":""</c>, so without this guard every such event would collide on one empty
    /// <c>StepId</c>, which is precisely the defect class being closed here.</summary>
    [Fact]
    public void An_empty_envelope_id_matches_nothing_rather_than_colliding_on_the_first_such_event()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"","data":{"turnId":"1"}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"reasoningText":"Should not be reachable."}}"""),
            Ev(3, "assistant.turn_start", """{"id":"","data":{"turnId":"2"}}"""),
        };

        var byStepId = StepEvidenceLookup.FindThinkingForPromptSteps(events, [""]);

        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(byStepId[""]);
        Assert.Contains("No raw event was found", unavailable.Reason);
    }

    /// <summary>A message carrying no <c>model</c> field of its own cannot be attributed to any
    /// model's readable share — it is excluded from the breakdown rather than folded into an
    /// invented "unknown" bucket, and the reason falls back to generic wording rather than naming
    /// a model this event never identified.</summary>
    [Fact]
    public void Provider_encrypted_reasoning_with_no_model_field_falls_back_to_generic_wording_and_lists_no_share_for_it()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
            Ev(2, "assistant.message", """{"id":"e2","data":{"reasoningOpaque":"<encrypted>"}}"""),
            Ev(3, "assistant.turn_end", """{"id":"e3","data":{"turnId":"t1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "e1");

        var unavailable = Assert.IsType<ThinkingEnvelope.Unavailable>(result.Thinking);
        Assert.DoesNotContain("gpt", unavailable.Reason);
        Assert.NotNull(unavailable.ReadabilityByModel);
        Assert.Empty(unavailable.ReadabilityByModel!);
    }
}
