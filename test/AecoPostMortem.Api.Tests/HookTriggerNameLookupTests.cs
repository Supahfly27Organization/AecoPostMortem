using AecoPostMortem.Data;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// The tape's own eager, no-fetch fact for a hook step's Detail tab: the tool name a `postToolUse`
/// hook fired in response to, resolved once per session the same "additive, eager" shape
/// <see cref="PromptTextLookup"/> already established for a Prompt step's own text. The richer
/// trigger evidence (arguments, result) is a separate, on-demand read —
/// <see cref="StepEvidenceLookup.Find"/>'s own <c>Trigger</c> field.
///
/// A Hook step's own <c>StepId</c> is <c>data.hookInvocationId</c> — the pair's own natural key
/// <c>Ingestion.HookBuilder</c> keys <c>Data.Execution.Hook.EventId</c> by — never the envelope's
/// own <c>id</c>. A real hook.start's envelope id and its own hookInvocationId are two different
/// values (confirmed against the live reference corpus, the same real mismatch
/// <c>StepEvidenceLookupTests</c> documents for <see cref="StepEvidenceLookup.Find"/>'s own Hook
/// branch), so every fixture below keys its requested step id on <c>hookInvocationId</c>.
/// </summary>
public sealed class HookTriggerNameLookupTests
{
    static RawEvent Ev(long sequence, string eventType, string payload) =>
        new("s1", sequence, eventType, "2026-08-16T10:00:00Z", "1.0.0", "events.jsonl", sequence * 100, $"hash-{sequence}", payload);

    [Fact]
    public void No_events_resolve_no_trigger_names()
    {
        Assert.Empty(HookTriggerNameLookup.FindForHookSteps([], ["inv1"]));
    }

    [Fact]
    public void A_postToolUse_hooks_own_tool_name_resolves_by_its_own_hookInvocationId()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"edit","toolArgs":{"old_str":"a","new_str":"b"}}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, ["inv1"]);

        Assert.Equal("edit", byStepId["inv1"]);
    }

    /// <summary>The envelope id alone is not this step's identity — requesting it rather than the
    /// real hookInvocationId resolves nothing.</summary>
    [Fact]
    public void A_step_id_naming_the_envelope_id_instead_of_the_hookInvocationId_resolves_nothing()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"edit"}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, ["h1"]);

        Assert.False(byStepId.ContainsKey("h1"));
    }

    /// <summary>A `sessionStart` hook carries no `toolName` at all — absent from the result, the same
    /// "absence in, absence out" discipline `PromptTextLookup` already follows, never an empty
    /// string.</summary>
    [Fact]
    public void A_sessionStart_hook_is_absent_from_the_result()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"sessionStart","input":{"sessionId":"s1","source":"new","initialPrompt":"do the thing"}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, ["inv1"]);

        Assert.False(byStepId.ContainsKey("inv1"));
    }

    [Fact]
    public void A_step_id_with_no_matching_hook_start_is_absent_from_the_result()
    {
        var byStepId = HookTriggerNameLookup.FindForHookSteps([], ["inv-missing"]);

        Assert.False(byStepId.ContainsKey("inv-missing"));
    }

    [Fact]
    public void Only_the_requested_step_ids_are_resolved()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"edit"}}}"""),
            Ev(2, "hook.start", """{"id":"h2","data":{"hookInvocationId":"inv2","hookType":"postToolUse","input":{"toolName":"view"}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, ["inv1"]);

        Assert.Single(byStepId);
        Assert.Equal("edit", byStepId["inv1"]);
    }

    /// <summary>An empty <c>hookInvocationId</c> is not an identity, matching the same empty-id guard
    /// <see cref="PromptTextLookup"/> and <see cref="StepEvidenceLookup"/> already apply to their own
    /// envelope-id-keyed lookups.</summary>
    [Fact]
    public void An_empty_hookInvocationId_matches_nothing_rather_than_colliding_on_the_first_such_event()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"","hookType":"postToolUse","input":{"toolName":"edit"}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, [""]);

        Assert.False(byStepId.ContainsKey(""));
    }

    /// <summary>Code review: an empty `toolName` (present, string-typed, zero-length) is treated the
    /// same as a missing one, matching the identical guard `StepEvidenceLookup.FindTrigger` applies
    /// to the fuller, on-demand Raw-tab field — the two readers must agree on what counts as "a real
    /// trigger" for the same step.</summary>
    [Fact]
    public void An_empty_toolName_is_absent_from_the_result_not_a_blank_string()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":""}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, ["inv1"]);

        Assert.False(byStepId.ContainsKey("inv1"));
    }

    /// <summary>Code review: two `hook.start` events sharing one `hookInvocationId` must resolve to
    /// the *last* one's own tool name — matching `StepEvidenceLookup.FindByDataField`'s own
    /// overwrite-on-duplicate semantics, and critically, matching
    /// `StepEvidenceLookupTests.Two_hook_starts_sharing_one_hookInvocationId_resolve_to_the_last_ones_
    /// own_tool_name`'s identical scenario exactly, so this eager field and the fuller, on-demand
    /// Raw-tab read can never disagree about the same step's trigger.</summary>
    [Fact]
    public void Two_hook_starts_sharing_one_hookInvocationId_resolve_to_the_last_ones_own_tool_name()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"edit"}}}"""),
            Ev(2, "hook.start", """{"id":"h2","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"view"}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, ["inv1"]);

        Assert.Equal("view", byStepId["inv1"]);
    }

    /// <summary>The two-phase resolution (last matching envelope first, tool name second) must not
    /// let an earlier duplicate's real tool name survive underneath a later duplicate that carries
    /// none — the *last* envelope is authoritative even when it has nothing useful to say.</summary>
    [Fact]
    public void A_later_duplicate_with_no_toolName_overrides_an_earlier_duplicates_real_one()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"h1","data":{"hookInvocationId":"inv1","hookType":"postToolUse","input":{"toolName":"edit"}}}"""),
            Ev(2, "hook.start", """{"id":"h2","data":{"hookInvocationId":"inv1","hookType":"sessionStart","input":{"sessionId":"s1"}}}"""),
        };

        var byStepId = HookTriggerNameLookup.FindForHookSteps(events, ["inv1"]);

        Assert.False(byStepId.ContainsKey("inv1"));
    }
}
