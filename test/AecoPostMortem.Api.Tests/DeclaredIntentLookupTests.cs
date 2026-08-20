using AecoPostMortem.Data;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-19's own not-yet-wired gap (issue #29): <c>report_intent</c>'s <c>arguments.intent</c> is read
/// straight from RAW, the same narrow-read discipline <see cref="HookFailureEventLookup"/> follows
/// for its own evidence not carried by the derived layer.
/// </summary>
public sealed class DeclaredIntentLookupTests
{
    static RawEvent Ev(long sequence, string timestamp, string payload) =>
        new("s1", sequence, "tool.execution_start", timestamp, "1.0.0", "events.jsonl", sequence * 100, $"hash-{sequence}", payload);

    [Fact]
    public void No_tool_calls_produce_no_intents()
    {
        Assert.Empty(DeclaredIntentLookup.Find("s1", []));
    }

    [Fact]
    public void A_report_intent_call_yields_its_own_declared_phase()
    {
        var events = new[]
        {
            Ev(1, "2026-05-07T14:18:16.713Z",
                """{"id":"e1","data":{"toolCallId":"tc1","toolName":"report_intent","arguments":{"intent":"Locating EF projects"}}}"""),
        };

        var intent = Assert.Single(DeclaredIntentLookup.Find("s1", events));

        Assert.Equal("s1", intent.SessionId);
        Assert.Equal("Locating EF projects", intent.Phase);
    }

    [Fact]
    public void A_non_report_intent_call_is_ignored()
    {
        var events = new[]
        {
            Ev(1, "2026-05-07T14:18:16.713Z",
                """{"id":"e1","data":{"toolCallId":"tc1","toolName":"view","arguments":{"path":"/a"}}}"""),
        };

        Assert.Empty(DeclaredIntentLookup.Find("s1", events));
    }

    [Fact]
    public void A_report_intent_call_missing_the_intent_argument_is_excluded()
    {
        var events = new[]
        {
            Ev(1, "2026-05-07T14:18:16.713Z",
                """{"id":"e1","data":{"toolCallId":"tc1","toolName":"report_intent","arguments":{}}}"""),
        };

        Assert.Empty(DeclaredIntentLookup.Find("s1", events));
    }

    /// <summary>FR-4's third argument shape (`ToolArguments.Kind`) is excluded rather than guessed
    /// at — a `report_intent` call is never measured to carry it in the real corpus, but the parser
    /// still has to fall through cleanly rather than throw.</summary>
    [Fact]
    public void A_report_intent_call_whose_arguments_are_string_shaped_is_excluded()
    {
        var events = new[]
        {
            Ev(1, "2026-05-07T14:18:16.713Z",
                """{"id":"e1","data":{"toolCallId":"tc1","toolName":"report_intent","arguments":"not an object"}}"""),
        };

        Assert.Empty(DeclaredIntentLookup.Find("s1", events));
    }

    [Fact]
    public void A_report_intent_call_whose_arguments_are_unparsed_shaped_is_excluded()
    {
        var events = new[]
        {
            Ev(1, "2026-05-07T14:18:16.713Z",
                """{"id":"e1","data":{"toolCallId":"tc1","toolName":"report_intent","arguments":42}}"""),
        };

        Assert.Empty(DeclaredIntentLookup.Find("s1", events));
    }

    [Fact]
    public void Sequence_orders_two_intents_by_their_own_timestamps()
    {
        var events = new[]
        {
            Ev(1, "2026-05-07T14:20:00.000Z",
                """{"id":"e1","data":{"toolCallId":"tc1","toolName":"report_intent","arguments":{"intent":"second"}}}"""),
            Ev(2, "2026-05-07T14:18:00.000Z",
                """{"id":"e2","data":{"toolCallId":"tc2","toolName":"report_intent","arguments":{"intent":"first"}}}"""),
        };

        var intents = DeclaredIntentLookup.Find("s1", events).OrderBy(i => i.Sequence).ToArray();

        Assert.Equal("first", intents[0].Phase);
        Assert.Equal("second", intents[1].Phase);
    }
}
