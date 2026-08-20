using System.Text;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>FR-2/FR-6: reading a session file through to a persisted RAW row, and FR-6's retry rule
/// — a skipped line is never recorded as permanently bad, so it is picked up the next time the same
/// file is ingested.</summary>
public sealed class SessionIngestorTests
{
    const string SessionStart =
        """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1}}""";

    const string TurnStart = """{"type":"assistant.turn_start","timestamp":"2026-05-07T14:16:49.000Z"}""";

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

    /// <summary>Issue #5, Scenario 1: re-running ingestion over a fully ingested corpus, with no
    /// new sessions, adds nothing and the run itself reports zero new events.</summary>
    [Fact]
    public void Re_running_ingestion_with_no_new_events_adds_nothing_and_reports_zero()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);

        using var context = workspace.Store.Open();
        SessionIngestor.Ingest(context, "session-1", file);
        var countAfterFirstRun = context.RawEvents.Count();

        var secondRun = SessionIngestor.Ingest(context, "session-1", file);

        Assert.Equal(0, secondRun.EventsInserted);
        Assert.Equal(countAfterFirstRun, context.RawEvents.Count());
    }

    /// <summary>Issue #5, Scenario 2: a session already ingested whose file has since grown by
    /// appended events ingests only its new tail — the previously stored rows are untouched, not
    /// just unchanged in count.</summary>
    [Fact]
    public void A_grown_session_ingests_only_its_new_tail_and_leaves_stored_rows_untouched()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart);

        using var context = workspace.Store.Open();
        SessionIngestor.Ingest(context, "session-1", file);
        var storedBeforeGrowth = context.RawEvents.AsNoTracking().OrderBy(raw => raw.Id).ToArray();

        File.AppendAllText(file, TurnStart + "\n");

        var secondRun = SessionIngestor.Ingest(context, "session-1", file);

        Assert.Equal(1, secondRun.EventsInserted);
        var storedAfterGrowth = context.RawEvents.AsNoTracking().OrderBy(raw => raw.Id).ToArray();
        Assert.Equal(storedBeforeGrowth, storedAfterGrowth.Take(storedBeforeGrowth.Length));
        Assert.Equal(2, storedAfterGrowth.Length);
    }

    /// <summary>Issue #5, Scenario 3: a file whose existing bytes no longer match their stored
    /// content hash is a rewrite, not a growth. The mismatch is reported and nothing from that read
    /// is appended over the rows already stored for it.</summary>
    [Fact]
    public void A_rewritten_file_is_reported_rather_than_appended_over()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);

        using var context = workspace.Store.Open();
        SessionIngestor.Ingest(context, "session-1", file);
        var countBeforeRewrite = context.RawEvents.Count();

        // The file is rewritten, not grown: line 1 is replaced by different bytes at the same
        // byte offset (0), which is what a truncate-and-rewrite looks like, as opposed to a
        // resumed session continuing the same byte stream.
        const string RewrittenSessionStart =
            """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"9.9.9","version":1}}""";
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes(string.Join('\n', RewrittenSessionStart, TurnStart) + "\n"));

        var secondRun = SessionIngestor.Ingest(context, "session-1", file);

        Assert.True(secondRun.RewriteDetected);
        Assert.NotEmpty(secondRun.RewriteMismatches);
        Assert.Equal(0, secondRun.EventsInserted);
        Assert.Equal(countBeforeRewrite, context.RawEvents.Count());
    }
}
