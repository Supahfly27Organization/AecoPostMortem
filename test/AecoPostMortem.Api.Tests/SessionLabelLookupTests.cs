using AecoPostMortem.Data;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// A session's own display label (Digest session-naming Slice 2): the first 5 words of its earliest
/// user.message, so a Digest session link reads as something other than a bare GUID. Simpler than
/// PromptTextLookup — a session's first prompt needs no turn_start/interactionId join, only the
/// earliest user.message event by RawEvent.Sequence.
/// </summary>
public sealed class SessionLabelLookupTests
{
    static RawEvent Ev(long sequence, string eventType, string payload) =>
        new("s1", sequence, eventType, "2026-08-16T10:00:00Z", "1.0.0", "events.jsonl", sequence * 100, $"hash-{sequence}", payload);

    [Fact]
    public void No_events_resolve_no_label()
    {
        Assert.Null(SessionLabelLookup.Find("s1", []));
    }

    [Fact]
    public void A_short_prompt_is_returned_verbatim_with_no_ellipsis()
    {
        var events = new[]
        {
            Ev(1, "user.message", """{"id":"e1","data":{"content":"fix the bug"}}"""),
        };

        Assert.Equal("fix the bug", SessionLabelLookup.Find("s1", events));
    }

    [Fact]
    public void A_longer_prompt_is_truncated_to_five_words_with_an_ellipsis()
    {
        var events = new[]
        {
            Ev(1, "user.message", """{"id":"e1","data":{"content":"run ef database update for both auth and regular projects"}}"""),
        };

        Assert.Equal("run ef database update for…", SessionLabelLookup.Find("s1", events));
    }

    [Fact]
    public void Exactly_five_words_has_no_ellipsis()
    {
        var events = new[]
        {
            Ev(1, "user.message", """{"id":"e1","data":{"content":"one two three four five"}}"""),
        };

        Assert.Equal("one two three four five", SessionLabelLookup.Find("s1", events));
    }

    [Fact]
    public void The_earliest_user_message_by_sequence_wins_even_when_the_events_are_out_of_order()
    {
        var events = new[]
        {
            Ev(3, "user.message", """{"id":"e3","data":{"content":"second message"}}"""),
            Ev(1, "user.message", """{"id":"e1","data":{"content":"first message"}}"""),
        };

        Assert.Equal("first message", SessionLabelLookup.Find("s1", events));
    }

    [Fact]
    public void A_user_message_with_empty_content_resolves_no_label()
    {
        var events = new[]
        {
            Ev(1, "user.message", """{"id":"e1","data":{"content":""}}"""),
        };

        Assert.Null(SessionLabelLookup.Find("s1", events));
    }

    [Fact]
    public void Non_user_message_events_are_ignored()
    {
        var events = new[]
        {
            Ev(1, "session.start", """{"id":"e1","data":{}}"""),
            Ev(2, "assistant.turn_start", """{"id":"e2","data":{"turnId":"t1"}}"""),
        };

        Assert.Null(SessionLabelLookup.Find("s1", events));
    }
}
