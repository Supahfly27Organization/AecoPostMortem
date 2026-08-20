namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-14: every ingest run states sessions found, ingested, excluded and why, lines parsed, lines
/// skipped, and events by type. <see cref="IngestionRun.Run"/> is the orchestrator that walks
/// <see cref="SessionDiscovery"/>'s own output through <see cref="SessionIngestor"/> — the same
/// per-session door a single-session caller uses — and rolls the results up into one
/// <see cref="CoverageReport"/>.
/// </summary>
public sealed class IngestionRunTests
{
    const string SessionStart =
        """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1,"context":{"cwd":"C:\\work\\feature-x"}}}""";

    const string ExcludedSessionStart =
        """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1,"context":{"cwd":"C:\\repo\\AecoPostMortem"}}}""";

    const string TurnStart = """{"type":"assistant.turn_start","timestamp":"2026-05-07T14:16:49.000Z"}""";

    [Fact]
    public void Sessions_found_counts_every_classified_directory()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart);
        workspace.CreateSessionDirectory("session-2"); // classified, but no events.jsonl

        using var context = workspace.Store.Open();
        var report = IngestionRun.Run(context, workspace.SessionStateRoot, []);

        Assert.Equal(2, report.SessionsFound);
    }

    [Fact]
    public void A_session_outside_every_excluded_root_is_ingested()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);

        using var context = workspace.Store.Open();
        var report = IngestionRun.Run(context, workspace.SessionStateRoot, [@"C:\repo\AecoPostMortem"]);

        Assert.Equal(1, report.SessionsIngested);
        Assert.Empty(report.SessionsExcluded);
        Assert.Equal(2, context.RawEvents.Count());
    }

    [Fact]
    public void An_excluded_session_is_named_with_its_reason_and_not_ingested()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, ExcludedSessionStart);

        using var context = workspace.Store.Open();
        var report = IngestionRun.Run(context, workspace.SessionStateRoot, [@"C:\repo\AecoPostMortem"]);

        Assert.Equal(0, report.SessionsIngested);
        var excluded = Assert.Single(report.SessionsExcluded);
        Assert.Equal("session-1", excluded.SessionId);
        Assert.Contains(@"C:\repo\AecoPostMortem", excluded.Reason);
        Assert.Empty(context.RawEvents);
    }

    [Fact]
    public void Lines_parsed_and_skipped_are_summed_across_every_session()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, "not valid json");
        workspace.WriteEventsFile("session-2", trailingNewline: true, SessionStart, TurnStart);

        using var context = workspace.Store.Open();
        var report = IngestionRun.Run(context, workspace.SessionStateRoot, []);

        Assert.Equal(4, report.LinesParsed);
        Assert.Equal(1, report.LinesSkipped);
    }

    [Fact]
    public void Events_are_counted_by_type_across_every_ingested_session()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);
        workspace.WriteEventsFile("session-2", trailingNewline: true, SessionStart, TurnStart, TurnStart);

        using var context = workspace.Store.Open();
        var report = IngestionRun.Run(context, workspace.SessionStateRoot, []);

        Assert.Equal(2, report.EventsByType["session.start"]);
        Assert.Equal(3, report.EventsByType["assistant.turn_start"]);
    }

    /// <summary>An excluded session's events are never counted toward <c>EventsByType</c> — they
    /// were never persisted, so they are not part of what this run covered.</summary>
    [Fact]
    public void An_excluded_sessions_events_are_not_counted_by_type()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, ExcludedSessionStart, TurnStart);

        using var context = workspace.Store.Open();
        var report = IngestionRun.Run(context, workspace.SessionStateRoot, [@"C:\repo\AecoPostMortem"]);

        Assert.Empty(report.EventsByType);
    }

    /// <summary>A rewrite-refused session (FR-5) is not FR-7's concern — it must not be counted as
    /// ingested, and its (unpersisted) events must not be folded into <c>EventsByType</c>, even
    /// though its lines were still read this run.</summary>
    [Fact]
    public void A_rewrite_refused_session_is_not_counted_as_ingested_or_folded_into_events_by_type()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);

        using var context = workspace.Store.Open();
        var firstRun = IngestionRun.Run(context, workspace.SessionStateRoot, []);
        Assert.Equal(1, firstRun.SessionsIngested);

        // Rewritten, not grown: line 1 is replaced by different bytes at the same byte offset (0).
        const string RewrittenSessionStart =
            """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"9.9.9","version":1}}""";
        File.WriteAllBytes(
            file,
            System.Text.Encoding.UTF8.GetBytes(string.Join('\n', RewrittenSessionStart, TurnStart) + "\n"));

        var secondRun = IngestionRun.Run(context, workspace.SessionStateRoot, []);

        Assert.Equal(0, secondRun.SessionsIngested);
        Assert.Empty(secondRun.SessionsExcluded);
        Assert.Empty(secondRun.EventsByType);
        Assert.Equal(2, secondRun.LinesParsed);
    }

    [Fact]
    public void An_empty_root_reports_zero_across_the_board()
    {
        using var workspace = new IngestionTestWorkspace();

        using var context = workspace.Store.Open();
        var report = IngestionRun.Run(context, workspace.SessionStateRoot, []);

        Assert.Equal(0, report.SessionsFound);
        Assert.Equal(0, report.SessionsIngested);
        Assert.Empty(report.SessionsExcluded);
        Assert.Equal(0, report.LinesParsed);
        Assert.Equal(0, report.LinesSkipped);
        Assert.Empty(report.EventsByType);
    }
}
