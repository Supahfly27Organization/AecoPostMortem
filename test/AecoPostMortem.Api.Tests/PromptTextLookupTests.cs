using AecoPostMortem.Data;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// Findings.SessionRecording built a Prompt step's Label from the turn's own Outcome because
/// Turn carries no message text — this lookup closes that gap at the Api layer instead, joining a
/// real user.message event's own data.content to a Prompt step's StepId (Turn.TurnId) via
/// interactionId, the same narrow-RAW-read discipline HookFailureEventLookup already documents.
/// </summary>
public sealed class PromptTextLookupTests
{
    static RawEvent Ev(long sequence, string eventType, string payload) =>
        new("s1", sequence, eventType, "2026-08-16T10:00:00Z", "1.0.0", "events.jsonl", sequence * 100, $"hash-{sequence}", payload);

    [Fact]
    public void No_events_resolve_no_prompt_text()
    {
        Assert.Empty(PromptTextLookup.FindForPromptSteps([], ["t1"]));
    }

    [Fact]
    public void A_turn_start_joined_to_its_own_user_message_resolves_the_real_content()
    {
        var events = new[]
        {
            Ev(1, "user.message", """{"id":"e1","data":{"interactionId":"i1","content":"run ef database update"}}"""),
            Ev(2, "assistant.turn_start", """{"id":"e2","data":{"turnId":"t1","interactionId":"i1"}}"""),
        };

        var byStepId = PromptTextLookup.FindForPromptSteps(events, ["t1"]);

        Assert.Equal("run ef database update", byStepId["t1"]);
    }

    [Fact]
    public void A_turn_id_with_no_matching_turn_start_is_absent_from_the_result()
    {
        var events = Array.Empty<RawEvent>();

        var byStepId = PromptTextLookup.FindForPromptSteps(events, ["t-missing"]);

        Assert.False(byStepId.ContainsKey("t-missing"));
    }

    [Fact]
    public void A_turn_start_whose_interaction_id_resolves_no_user_message_is_absent_from_the_result()
    {
        var events = new[]
        {
            Ev(1, "assistant.turn_start", """{"id":"e1","data":{"turnId":"t1","interactionId":"i1"}}"""),
        };

        var byStepId = PromptTextLookup.FindForPromptSteps(events, ["t1"]);

        Assert.False(byStepId.ContainsKey("t1"));
    }

    [Fact]
    public void Only_the_requested_step_ids_are_resolved()
    {
        var events = new[]
        {
            Ev(1, "user.message", """{"id":"e1","data":{"interactionId":"i1","content":"first prompt"}}"""),
            Ev(2, "assistant.turn_start", """{"id":"e2","data":{"turnId":"t1","interactionId":"i1"}}"""),
            Ev(3, "user.message", """{"id":"e3","data":{"interactionId":"i2","content":"second prompt"}}"""),
            Ev(4, "assistant.turn_start", """{"id":"e4","data":{"turnId":"t2","interactionId":"i2"}}"""),
        };

        var byStepId = PromptTextLookup.FindForPromptSteps(events, ["t1"]);

        Assert.Single(byStepId);
        Assert.Equal("first prompt", byStepId["t1"]);
    }

    /// <summary>A real corpus finding, not a hypothetical: Turn.TurnId can repeat within one session
    /// (AecoPostMortem.Data/CLAUDE.md). The first occurrence's own interactionId wins, matching
    /// StepEvidenceLookup.FindByDataField's own first-match behaviour for the identical field.</summary>
    [Fact]
    public void A_repeated_turn_id_within_one_session_resolves_the_first_occurrences_prompt()
    {
        var events = new[]
        {
            Ev(1, "user.message", """{"id":"e1","data":{"interactionId":"i1","content":"first occurrence"}}"""),
            Ev(2, "assistant.turn_start", """{"id":"e2","data":{"turnId":"t1","interactionId":"i1"}}"""),
            Ev(3, "user.message", """{"id":"e3","data":{"interactionId":"i2","content":"second occurrence"}}"""),
            Ev(4, "assistant.turn_start", """{"id":"e4","data":{"turnId":"t1","interactionId":"i2"}}"""),
        };

        var byStepId = PromptTextLookup.FindForPromptSteps(events, ["t1"]);

        Assert.Equal("first occurrence", byStepId["t1"]);
    }
}
