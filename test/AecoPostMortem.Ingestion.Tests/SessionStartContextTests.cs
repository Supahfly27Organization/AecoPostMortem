using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>FR-7's key: <c>session.start.data.context.cwd</c>. <see cref="SessionStartContext"/>
/// reads it off the first event only, mirroring the "line 1 only" rule
/// <see cref="SessionEventReader.ReadDeclaredVersion"/> already applies to provider/schema
/// version.</summary>
public sealed class SessionStartContextTests
{
    [Fact]
    public void A_session_start_events_cwd_is_extracted()
    {
        var events = new[]
        {
            SessionStart("""{"cwd":"C:/repo/AecoPostMortem"}"""),
        };

        var cwd = SessionStartContext.ExtractCwd(events);

        Assert.Equal("C:/repo/AecoPostMortem", cwd);
    }

    [Fact]
    public void No_events_reads_as_unknown()
    {
        var cwd = SessionStartContext.ExtractCwd([]);

        Assert.Null(cwd);
    }

    [Fact]
    public void A_first_event_that_is_not_session_start_reads_as_unknown()
    {
        var events = new[]
        {
            Raw("""{"type":"assistant.turn_start"}""", eventType: "assistant.turn_start"),
        };

        var cwd = SessionStartContext.ExtractCwd(events);

        Assert.Null(cwd);
    }

    [Fact]
    public void A_session_start_with_no_context_reads_as_unknown()
    {
        var events = new[]
        {
            Raw("""{"type":"session.start","data":{"copilotVersion":"1.0.40"}}"""),
        };

        var cwd = SessionStartContext.ExtractCwd(events);

        Assert.Null(cwd);
    }

    /// <summary>Defensive only — a line already accepted into RAW always parsed as JSON once
    /// (<c>EventEnvelopeParsers.TryParse</c>), so this path should not be reachable in practice.
    /// Covered anyway so the <c>catch (JsonException)</c> branch is proven, not just assumed.</summary>
    [Fact]
    public void Malformed_payload_reads_as_unknown_rather_than_throwing()
    {
        var events = new[] { Raw("not valid json") };

        var cwd = SessionStartContext.ExtractCwd(events);

        Assert.Null(cwd);
    }

    static RawEvent SessionStart(string context) =>
        Raw("""{"type":"session.start","data":{"copilotVersion":"1.0.40","context":""" + context + "}}");

    static RawEvent Raw(string payload, string eventType = "session.start") => new(
        "session-1",
        Sequence: 0,
        eventType,
        "2026-05-07T14:16:48.682Z",
        "1.0.40",
        "events.jsonl",
        ByteOffset: 0,
        RawPayload.ContentHashOfText(payload),
        payload);
}
