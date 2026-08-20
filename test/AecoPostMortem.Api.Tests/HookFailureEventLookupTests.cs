using AecoPostMortem.Data;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-17's error text (issue #27): <c>Data.Execution.Hook</c> carries no error column, so this
/// resolves failed <c>hook.start</c>/<c>hook.end</c> pairs straight from a session's own RAW events —
/// the same "GetSession reads the derived tables... this second read produces nothing that overlaps
/// with that" narrow-RAW-read discipline <c>StepEvidenceLookup</c> already documents.
/// </summary>
public sealed class HookFailureEventLookupTests
{
    static RawEvent Ev(long sequence, string eventType, string payload) =>
        new("s1", sequence, eventType, "2026-08-16T10:00:00Z", "1.0.0", "events.jsonl", sequence * 100, $"hash-{sequence}", payload);

    [Fact]
    public void No_hook_events_produce_no_failures()
    {
        Assert.Empty(HookFailureEventLookup.Find("s1", []));
    }

    [Fact]
    public void A_successful_pair_produces_no_failure()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"e1","data":{"hookInvocationId":"inv-1","hookType":"sessionStart"}}"""),
            Ev(2, "hook.end", """{"id":"e2","data":{"hookInvocationId":"inv-1","hookType":"sessionStart","success":true}}"""),
        };

        Assert.Empty(HookFailureEventLookup.Find("s1", events));
    }

    [Fact]
    public void A_failed_pair_carries_the_hook_name_and_the_errors_message()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"e1","data":{"hookInvocationId":"inv-1","hookType":"sessionStart"}}"""),
            Ev(2, "hook.end", """{"id":"e2","data":{"hookInvocationId":"inv-1","hookType":"sessionStart","success":false,"error":{"message":"ParserError: Unexpected token"}}}"""),
        };

        var failure = Assert.Single(HookFailureEventLookup.Find("s1", events));

        Assert.Equal("s1", failure.SessionId);
        Assert.Equal("sessionStart", failure.HookName);
        Assert.False(failure.Success);
        Assert.Equal("ParserError: Unexpected token", failure.Error);
    }

    [Fact]
    public void A_start_with_no_matching_end_produces_no_failure()
    {
        var events = new[]
        {
            Ev(1, "hook.start", """{"id":"e1","data":{"hookInvocationId":"inv-1","hookType":"sessionStart"}}"""),
        };

        Assert.Empty(HookFailureEventLookup.Find("s1", events));
    }
}
