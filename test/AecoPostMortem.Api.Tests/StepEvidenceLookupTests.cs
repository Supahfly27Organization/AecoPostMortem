using AecoPostMortem.Data;
using AecoPostMortem.Findings;
using AecoPostMortem.Ingestion;

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
    public void A_tool_calls_result_is_its_own_execution_complete()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}"""),
            Ev(2, "tool.execution_complete", """{"id":"e2","data":{"toolCallId":"tc1","success":true,"result":{"content":"ok","detailedContent":"details"}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        var evidenceResult = Assert.IsType<RawStepEventEnvelope.Present>(result.Result);
        Assert.Equal("tool.execution_complete", evidenceResult.EventType);
        Assert.Contains("\"content\":\"ok\"", evidenceResult.Payload);
    }

    /// <summary>The real 373/16,076-event failure case measured in the live corpus: a failed call
    /// carries no <c>result</c> key at all, only <c>error</c> — still a real, present
    /// <c>tool.execution_complete</c> event, so this is <c>Present</c> with the error payload, not
    /// <c>Absent</c>.</summary>
    [Fact]
    public void A_failed_tool_calls_result_still_serves_its_own_execution_complete_payload()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"grep","toolCallId":"tc1"}}"""),
            Ev(2, "tool.execution_complete", """{"id":"e2","data":{"toolCallId":"tc1","success":false,"error":{"message":"boom","code":"failure"}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        var evidenceResult = Assert.IsType<RawStepEventEnvelope.Present>(result.Result);
        Assert.Contains("\"success\":false", evidenceResult.Payload);
    }

    /// <summary>A call that never recorded its own <c>tool.execution_complete</c> — still running, or
    /// the session ended mid-call — is a distinct, stated state, never an empty string rendered as
    /// "the result was empty."</summary>
    [Fact]
    public void A_tool_call_with_no_recorded_completion_reports_its_result_as_a_stated_absence()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        var skipped = Assert.IsType<RawStepEventEnvelope.Skipped>(result.Result);
        Assert.Contains("may still be running", skipped.Reason);
    }

    /// <summary>Only a tool or MCP call produces a <c>tool.execution_complete</c> at all — a prompt
    /// step's own result is "not applicable", a reason distinguishable from "still running" — not
    /// merely a second non-empty string (code review: a shared reason for both branches would leave
    /// this requirement unprotected).</summary>
    [Fact]
    public void A_prompt_step_reports_its_result_as_not_applicable_to_this_step_kind()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Prompt, "e1");

        var skipped = Assert.IsType<RawStepEventEnvelope.Skipped>(result.Result);
        Assert.Contains("this step kind does not", skipped.Reason);
        Assert.DoesNotContain("may still be running", skipped.Reason);
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

    /// <summary>A hook step's own StepId is <c>data.hookInvocationId</c> — the pair's own natural
    /// key <c>Ingestion.HookBuilder</c> deliberately keys <c>Data.Execution.Hook.EventId</c> by
    /// (that file's own doc comment: "unlike Skill, neither event's own envelope id ties the two
    /// together") — never the envelope's own <c>id</c>. This was a real, verified mismatch this
    /// task's own real-corpus check caught: a live hook.start's envelope id and its own
    /// hookInvocationId are two different values, so matching on the envelope id (as this lookup
    /// once did) resolved zero of 3,027 real hook steps.</summary>
    [Fact]
    public void A_hook_steps_raw_event_is_matched_by_its_own_hookInvocationId_not_the_envelopes_id()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv-1","hookType":"preCommit"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv-1");

        var raw = Assert.IsType<RawStepEventEnvelope.Present>(result.Raw);
        Assert.Equal("hook.start", raw.EventType);
    }

    /// <summary>The envelope id alone is not this step's identity — a request naming it rather than
    /// the real hookInvocationId must not resolve, the same "wrong field, no match" discipline this
    /// project's other identity fixes (`data.turnId` vs. a turn's own envelope id) already prove.</summary>
    [Fact]
    public void A_hook_step_requested_by_its_envelope_id_instead_of_its_hookInvocationId_resolves_nothing()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv-1","hookType":"preCommit"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "h1");

        Assert.IsType<RawStepEventEnvelope.Skipped>(result.Raw);
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
        var resultSkipped = Assert.IsType<RawStepEventEnvelope.Skipped>(result.Result);
        Assert.NotEmpty(resultSkipped.Reason);
    }

    /// <summary>An MCP call resolves its result the same way a plain tool call does — the join is on
    /// <c>toolCallId</c>, and <c>tool.execution_complete</c> carries the full result for every tool
    /// call, MCP or not (verified against the live 35-session reference corpus).</summary>
    [Fact]
    public void An_mcp_calls_result_is_its_own_execution_complete_too()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"codebase-memory-mcp-search_code","toolCallId":"tc1"}}"""),
            Ev(2, "tool.execution_complete", """{"id":"e2","data":{"toolCallId":"tc1","success":true,"result":{"content":"{\"hits\":3}","detailedContent":"{\"hits\":3}"}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.McpCall, "tc1");

        var evidenceResult = Assert.IsType<RawStepEventEnvelope.Present>(result.Result);
        Assert.Equal("tool.execution_complete", evidenceResult.EventType);
    }

    /// <summary>Two <c>tool.execution_complete</c> events sharing one <c>toolCallId</c> — essentially
    /// theoretical on the live corpus (every measured id is unique per event type), but this lookup
    /// resolves the <em>last</em> one, matching <c>Ingestion.ExecutionRecordBuilder.BuildToolCalls</c>'s
    /// own overwrite-on-duplicate dictionary semantics (code review) — so the Raw tab's own Result and
    /// the Detail tab's derived <c>ToolCall.Success</c>/<c>.CompletedAt</c> can never disagree about
    /// which of two same-id events is authoritative.</summary>
    [Fact]
    public void Two_completions_sharing_one_toolCallId_resolve_to_the_last_one_not_the_first()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}"""),
            Ev(2, "tool.execution_complete", """{"id":"e2","data":{"toolCallId":"tc1","success":false,"error":{"message":"first, stale"}}}"""),
            Ev(3, "tool.execution_complete", """{"id":"e3","data":{"toolCallId":"tc1","success":true,"result":{"content":"second, authoritative"}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        var evidenceResult = Assert.IsType<RawStepEventEnvelope.Present>(result.Result);
        Assert.Contains("second, authoritative", evidenceResult.Payload);
        Assert.DoesNotContain("first, stale", evidenceResult.Payload);
    }

    /// <summary>A missing <c>tool.execution_start</c> (e.g. skipped at ingest) does not imply a
    /// missing <c>tool.execution_complete</c> — the two are independent events. A real, present
    /// result must still surface on the Result field even though the Raw (call) field reports
    /// skipped, rather than inheriting the call's own absence (code review).</summary>
    [Fact]
    public void A_result_still_resolves_even_when_the_calls_own_start_event_is_missing()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_complete", """{"id":"e1","data":{"toolCallId":"tc1","success":true,"result":{"content":"ok"}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        Assert.IsType<RawStepEventEnvelope.Skipped>(result.Raw);
        var evidenceResult = Assert.IsType<RawStepEventEnvelope.Present>(result.Result);
        Assert.Equal("tool.execution_complete", evidenceResult.EventType);
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

    /// <summary>What triggered a hook — real `hook.start.data.input` payloads confirmed against the
    /// live 35-session reference corpus: a `postToolUse` hook carries `toolName`/`toolArgs`/
    /// `toolResult`, all three joined here from the identical `hook.start` event `Raw` already
    /// resolved, never a second raw-event lookup.</summary>
    [Fact]
    public void A_postToolUse_hooks_trigger_names_the_tool_and_carries_its_arguments_and_result()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"skill","toolArgs":{"skill":"using-superpowers"},"toolResult":{"resultType":"success","sessionLog":"Skill loaded"}}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv1");

        var trigger = Assert.IsType<HookTriggerEnvelope.ToolInvocation>(result.Trigger);
        Assert.Equal("skill", trigger.ToolName);
        Assert.Equal(ToolArgumentKind.Object, trigger.Arguments.Kind);
        Assert.Contains("\"skill\":\"using-superpowers\"", trigger.Arguments.Raw);
        Assert.NotNull(trigger.Result);
        Assert.Contains("Skill loaded", trigger.Result);
    }

    /// <summary>The real, measured wrinkle: 840 of 2,992 real `postToolUse` `hook.start` events in the
    /// live corpus carry a string-shaped `toolArgs` (`apply_patch`'s own whole patch body), never an
    /// object — the identical `Object`/`String`/`Unparsed` distinction `Ingestion.ToolArguments`
    /// already makes for `tool.execution_start.data.arguments` (FR-4), reused here rather than
    /// assuming an object.</summary>
    [Fact]
    public void A_string_shaped_toolArgs_is_recorded_as_string_not_coerced_into_an_object()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"apply_patch","toolArgs":"*** Begin Patch\n+hello\n*** End Patch","toolResult":{"resultType":"success"}}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv1");

        var trigger = Assert.IsType<HookTriggerEnvelope.ToolInvocation>(result.Trigger);
        Assert.Equal(ToolArgumentKind.String, trigger.Arguments.Kind);
        Assert.Contains("Begin Patch", trigger.Arguments.Raw);
    }

    /// <summary>A `sessionStart` hook carries `initialPrompt`/`source`/`cwd`, never `toolName` —
    /// verified against the live corpus (35 real `sessionStart` hook.start events, none with a
    /// `toolName`). "No trigger" is this shape's own distinct, stated `Absent` case, never an empty
    /// string standing in for "blank" or "unknown."</summary>
    [Fact]
    public void A_sessionStart_hook_has_no_tool_trigger_and_states_why()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"sessionStart","input":{"sessionId":"s1","cwd":"C:\\repo","source":"new","initialPrompt":"do the thing"}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv1");

        var absent = Assert.IsType<HookTriggerEnvelope.Absent>(result.Trigger);
        Assert.Contains("sessionStart", absent.Reason);
        Assert.Contains("no tool trigger", absent.Reason);
    }

    /// <summary>Only a hook step has a trigger at all — every other step kind states that plainly
    /// rather than attempting a lookup, the same short-circuit `Result` already applies to a
    /// non-tool-call step kind.</summary>
    [Fact]
    public void A_non_hook_step_reports_its_trigger_as_not_applicable_to_this_step_kind()
    {
        var events = new[]
        {
            Ev(1, "tool.execution_start", """{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.ToolCall, "tc1");

        var absent = Assert.IsType<HookTriggerEnvelope.Absent>(result.Trigger);
        Assert.Contains("this step kind does not", absent.Reason);
    }

    /// <summary>A hook step whose own `hook.start` cannot be found still answers with a stated
    /// absence, never a blank Trigger block.</summary>
    [Fact]
    public void A_hook_step_with_no_matching_raw_event_reports_its_trigger_as_a_stated_absence()
    {
        var events = Array.Empty<RawEvent>();

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "h-missing");

        var absent = Assert.IsType<HookTriggerEnvelope.Absent>(result.Trigger);
        Assert.NotEmpty(absent.Reason);
    }

    /// <summary>Measured 100% of real `postToolUse` `hook.start` events in the live corpus carry a
    /// `toolResult` — but this is still modelled as nullable rather than assumed, so a call whose
    /// trigger genuinely carries none states that fact rather than an empty string.</summary>
    [Fact]
    public void A_postToolUse_hook_with_no_recorded_toolResult_reports_a_null_result()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"report_intent","toolArgs":{"intent":"Locating EF projects"}}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv1");

        var trigger = Assert.IsType<HookTriggerEnvelope.ToolInvocation>(result.Trigger);
        Assert.Null(trigger.Result);
    }

    /// <summary>Code review: `GetRawText()` on a JSON `null` value returns the four-character text
    /// `"null"`, not a C# `null` — a `toolResult` that is present but explicitly JSON-null must still
    /// report a C# `null`, the same "no result was recorded" state a genuinely missing key
    /// reports.</summary>
    [Fact]
    public void A_postToolUse_hook_with_an_explicit_json_null_toolResult_still_reports_a_null_result()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"report_intent","toolArgs":{},"toolResult":null}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv1");

        var trigger = Assert.IsType<HookTriggerEnvelope.ToolInvocation>(result.Trigger);
        Assert.Null(trigger.Result);
    }

    /// <summary>Code review: an empty `toolName` (present, string-typed, zero-length) is treated the
    /// same as a missing one — never a `ToolInvocation` with a blank name — matching the identical
    /// guard `HookTriggerNameLookup.GetToolName` applies to the eager Detail-tab field, so the two
    /// readers agree on what counts as "a real trigger" for the same step.</summary>
    [Fact]
    public void A_postToolUse_hook_with_an_empty_toolName_reports_a_stated_absence_not_a_blank_name()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":""}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv1");

        Assert.IsType<HookTriggerEnvelope.Absent>(result.Trigger);
    }

    /// <summary>Code review: two `hook.start` events sharing one `hookInvocationId` must resolve to
    /// the *last* one's own tool name, matching `FindByDataField`'s own overwrite-on-duplicate
    /// semantics (the same rule `Two_completions_sharing_one_toolCallId_resolve_to_the_last_one_not_
    /// the_first` already proves for a tool call's own result) — and, critically, must resolve
    /// identically to `HookTriggerNameLookupTests`' own regression case for the same scenario, so the
    /// eager Detail-tab field and this on-demand Raw-tab field can never disagree about the same
    /// step's trigger.</summary>
    [Fact]
    public void Two_hook_starts_sharing_one_hookInvocationId_resolve_to_the_last_ones_own_tool_name()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"edit"}}}"""),
            Ev(2, "hook.start", """{"id":"h2","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"view"}}}"""),
        };

        var result = StepEvidenceLookup.Find(events, SessionTapeStepKind.Hook, "inv1");

        var trigger = Assert.IsType<HookTriggerEnvelope.ToolInvocation>(result.Trigger);
        Assert.Equal("view", trigger.ToolName);
    }
}
