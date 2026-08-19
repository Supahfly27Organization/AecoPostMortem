using System.Text;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>FR-2/FR-6: reading a session file through to a persisted RAW row, and FR-6's retry rule
/// — a skipped line is never recorded as permanently bad, so it is picked up the next time the same
/// file is ingested.</summary>
public sealed class SessionIngestorTests
{
    const string SessionStart =
        """{"type":"session.start","ts":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1}}""";

    const string TurnStart = """{"type":"assistant.turn_start","ts":"2026-05-07T14:16:49.000Z"}""";

    [Fact]
    public void Ingesting_a_session_file_persists_its_events_to_RAW()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);

        using var context = workspace.Store.Open();
        var result = SessionIngestor.Ingest(context, "session-1", file);

        Assert.Equal(2, result.EventsInserted);
        Assert.Equal(2, context.RawEvents.Count());
    }

    [Fact]
    public void A_previously_skipped_line_is_retried_and_persisted_once_it_completes()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile(
            "session-1",
            trailingNewline: true,
            SessionStart,
            "not valid json yet");

        using var context = workspace.Store.Open();

        var firstRun = SessionIngestor.Ingest(context, "session-1", file);
        Assert.Equal(1, firstRun.Read.SkippedLines);
        Assert.Equal(1, firstRun.EventsInserted);

        // The line completes: events.jsonl is live-written, so this is what a resumed session
        // looks like once the write that was in flight finishes. Encoding.UTF8.GetBytes, not
        // File.WriteAllText(..., Encoding.UTF8) — the latter's static instance emits a BOM, which
        // would corrupt line 1's JSON.
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes(string.Join('\n', SessionStart, TurnStart) + "\n"));

        var secondRun = SessionIngestor.Ingest(context, "session-1", file);

        Assert.Equal(0, secondRun.Read.SkippedLines);
        Assert.Equal(1, secondRun.EventsInserted);
        Assert.Equal(2, context.RawEvents.Count());
        Assert.Contains(context.RawEvents, raw => raw.EventType == "assistant.turn_start");
    }
}
