using System.Text.Json;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Builds one <c>Data.Execution.Session</c> row from a session's own <c>session.start</c> and
/// <c>session.shutdown</c> events — the first piece of the derived-layer writer that populates the
/// NORMALIZED tables at ingest time (previously nothing did; the Flight Recorder always 404'd).
/// </summary>
public sealed class SessionBuilderTests
{
    const string SessionId = "session-1";

    static RawEvent Event(
        long sequence, string type, string timestamp, object data, string providerVersion = "1.0.40")
    {
        var payload = JsonSerializer.Serialize(new { type, data });
        return new RawEvent(
            SessionId, sequence, type, timestamp, providerVersion,
            @"~/.copilot/session-state/session-1/events.jsonl", sequence, $"hash-{sequence}", payload);
    }

    static RawEvent SessionStart(long sequence = 0, string timestamp = "2026-05-07T14:16:48.682Z") =>
        Event(sequence, "session.start", timestamp, new
        {
            version = 1,
            copilotVersion = "1.0.40",
            context = new
            {
                cwd = @"F:\git\UpFront",
                gitRoot = @"F:\git\UpFront",
                branch = "master",
                headCommit = "d4cbd32279c8c4f979d3e7d2b5cfbec020c50036",
                repository = "supahfly27/UpFront",
                hostType = "github",
                baseCommit = "d4cbd32279c8c4f979d3e7d2b5cfbec020c50036",
            },
        });

    [Fact]
    public void No_events_produce_no_session()
    {
        Assert.Null(SessionBuilder.Build(SessionId, []));
    }

    [Fact]
    public void A_first_event_that_is_not_session_start_produces_no_session()
    {
        var events = new[] { Event(0, "assistant.turn_start", "2026-05-07T14:16:49.000Z", new { turnId = "t1" }) };

        Assert.Null(SessionBuilder.Build(SessionId, events));
    }

    [Fact]
    public void Identity_and_context_are_read_from_session_start()
    {
        var events = new[] { SessionStart() };

        var session = SessionBuilder.Build(SessionId, events)!;

        Assert.Equal(SessionId, session.SessionId);
        Assert.Equal("2026-05-07T14:16:48.682Z", session.StartedAt);
        Assert.Equal("1.0.40", session.CopilotVersion);
        Assert.Equal("1", session.EventSchemaVersion);
        Assert.Equal(@"~/.copilot/session-state/session-1/events.jsonl", session.SourceFile);
        Assert.Equal(@"F:\git\UpFront", session.Cwd);
        Assert.Equal(@"F:\git\UpFront", session.GitRoot);
        Assert.Equal("master", session.Branch);
        Assert.Equal("d4cbd32279c8c4f979d3e7d2b5cfbec020c50036", session.HeadCommit);
        Assert.Equal("supahfly27/UpFront", session.Repository);
        Assert.Equal("github", session.HostType);
        Assert.Equal("d4cbd32279c8c4f979d3e7d2b5cfbec020c50036", session.BaseCommit);
    }

    /// <summary>RAW never discards unknown or absent JSON — a line missing <c>data</c> entirely still
    /// reaches RAW so long as it parses and declares <c>type</c>/<c>timestamp</c>
    /// (<c>EventEnvelopeParsers</c>). A <c>session.start</c> row this bare must still produce a
    /// <see cref="AecoPostMortem.Data.Execution.Session"/> with sensible defaults, not throw — the
    /// same defensive discipline <see cref="SessionStartContext.ExtractCwd"/> already applies to
    /// <c>context.cwd</c> alone.</summary>
    [Fact]
    public void A_session_start_with_no_data_property_produces_defaults_rather_than_throwing()
    {
        var payload = """{"type":"session.start"}""";
        var events = new[]
        {
            new RawEvent(
                SessionId, 0, "session.start", "2026-05-07T14:16:48.682Z", "1.0.40",
                @"~/.copilot/session-state/session-1/events.jsonl", 0, "hash-0", payload),
        };

        var session = SessionBuilder.Build(SessionId, events)!;

        Assert.Equal(SessionId, session.SessionId);
        Assert.Equal(string.Empty, session.EventSchemaVersion);
        Assert.Equal(string.Empty, session.Cwd);
        Assert.Null(session.GitRoot);
    }

    [Fact]
    public void Without_a_shutdown_event_the_end_and_token_totals_are_null()
    {
        var events = new[] { SessionStart() };

        var session = SessionBuilder.Build(SessionId, events)!;

        Assert.Null(session.EndedAt);
        Assert.Null(session.InputTokens);
        Assert.Null(session.OutputTokens);
        Assert.Null(session.CacheReadTokens);
        Assert.Null(session.CacheWriteTokens);
        Assert.Null(session.ReasoningTokens);
        Assert.Null(session.ModelCount);
    }

    [Fact]
    public void A_shutdown_event_supplies_the_end_time_and_token_totals_summed_across_models()
    {
        var shutdown = Event(1, "session.shutdown", "2026-05-07T14:51:11.631Z", new
        {
            modelMetrics = new Dictionary<string, object>
            {
                ["gpt-5.4"] = new
                {
                    usage = new
                    {
                        inputTokens = 1_777_774,
                        outputTokens = 16_785,
                        cacheReadTokens = 1_672_448,
                        cacheWriteTokens = 0,
                        reasoningTokens = 6_402,
                    },
                },
                ["claude-sonnet-4.5"] = new
                {
                    usage = new
                    {
                        inputTokens = 1_000,
                        outputTokens = 200,
                        cacheReadTokens = 300,
                        cacheWriteTokens = 40,
                        reasoningTokens = 5,
                    },
                },
            },
        });
        var events = new[] { SessionStart(), shutdown };

        var session = SessionBuilder.Build(SessionId, events)!;

        Assert.Equal("2026-05-07T14:51:11.631Z", session.EndedAt);
        Assert.Equal(1_778_774, session.InputTokens);
        Assert.Equal(16_985, session.OutputTokens);
        Assert.Equal(1_672_748, session.CacheReadTokens);
        Assert.Equal(40, session.CacheWriteTokens);
        Assert.Equal(6_407, session.ReasoningTokens);
        Assert.Equal(2, session.ModelCount);
    }
}
