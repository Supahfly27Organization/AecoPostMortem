namespace AecoPostMortem.Ingestion.Tests;

/// <summary>FR-3 and FR-6: per-line reading of <c>events.jsonl</c> — provider version from line 1
/// only, per-line malformed tolerance, and the partial-trailing-line rule.</summary>
public sealed class SessionEventReaderTests
{
    const string SessionStart =
        """{"type":"session.start","ts":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1}}""";

    const string TurnStart = """{"type":"assistant.turn_start","ts":"2026-05-07T14:16:49.000Z"}""";

    [Fact]
    public void Provider_version_is_read_from_line_1s_session_start_event()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);

        var result = SessionEventReader.Read("session-1", file);

        Assert.Equal("1.0.40", result.ProviderVersion);
        Assert.All(result.Events, raw => Assert.Equal("1.0.40", raw.ProviderVersion));
    }

    [Fact]
    public void Parsers_register_against_the_event_schema_version_line_1_declares()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart, TurnStart);

        var result = SessionEventReader.Read("session-1", file);

        Assert.Equal(1, result.EventSchemaVersion);
    }

    [Fact]
    public void The_file_is_not_scanned_beyond_line_1_for_a_declared_version()
    {
        using var workspace = new IngestionTestWorkspace();
        // Line 1 is not session.start; line 2 is. The version must not be picked up from line 2.
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, TurnStart, SessionStart);

        var result = SessionEventReader.Read("session-1", file);

        Assert.Equal(SessionEventReader.UnknownProviderVersion, result.ProviderVersion);
        Assert.Null(result.EventSchemaVersion);
    }

    [Fact]
    public void A_partial_trailing_line_stops_ingestion_without_being_counted_as_malformed()
    {
        using var workspace = new IngestionTestWorkspace();
        var completeLines = string.Join('\n', SessionStart, TurnStart) + "\n";
        var partialTail = """{"type":"assistant.message","ts":"2026-05-07T""";
        var file = workspace.WriteEventsFile(
            "session-1",
            System.Text.Encoding.UTF8.GetBytes(completeLines + partialTail));

        var result = SessionEventReader.Read("session-1", file);

        Assert.Equal(2, result.LinesRead);
        Assert.Equal(0, result.SkippedLines);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(completeLines), result.HighWaterOffset);
    }

    [Fact]
    public void A_malformed_line_is_skipped_and_every_other_line_is_persisted()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile(
            "session-1",
            trailingNewline: true,
            SessionStart,
            "not valid json",
            TurnStart);

        var result = SessionEventReader.Read("session-1", file);

        Assert.Equal(3, result.LinesRead);
        Assert.Equal(1, result.SkippedLines);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("session.start", result.Events[0].EventType);
        Assert.Equal(0, result.Events[0].Sequence);
        Assert.Equal("assistant.turn_start", result.Events[1].EventType);
        Assert.Equal(2, result.Events[1].Sequence);
    }

    [Fact]
    public void An_empty_events_file_is_a_valid_session_with_zero_events()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", []);

        var result = SessionEventReader.Read("session-1", file);

        Assert.Empty(result.Events);
        Assert.Equal(0, result.LinesRead);
        Assert.Equal(0, result.SkippedLines);
        Assert.Equal(0, result.HighWaterOffset);
    }

    [Fact]
    public void An_unmeasured_provider_version_still_ingests_rather_than_refusing()
    {
        using var workspace = new IngestionTestWorkspace();
        const string futureVersion =
            """{"type":"session.start","ts":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"9.9.999","version":1}}""";
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, futureVersion);

        var result = SessionEventReader.Read("session-1", file);

        Assert.Equal("9.9.999", result.ProviderVersion);
        Assert.Single(result.Events);
    }

    [Fact]
    public void An_unrecognised_event_schema_version_still_ingests_via_the_fallback_parser()
    {
        using var workspace = new IngestionTestWorkspace();
        const string futureSchema =
            """{"type":"session.start","ts":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.99","version":99}}""";
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, futureSchema, TurnStart);

        var result = SessionEventReader.Read("session-1", file);

        Assert.Equal(99, result.EventSchemaVersion);
        Assert.Equal(0, result.SkippedLines);
        Assert.Equal(2, result.Events.Count);
    }

    [Fact]
    public void The_content_hash_and_payload_round_trip_the_line_verbatim()
    {
        using var workspace = new IngestionTestWorkspace();
        var file = workspace.WriteEventsFile("session-1", trailingNewline: true, SessionStart);

        var result = SessionEventReader.Read("session-1", file);

        var raw = Assert.Single(result.Events);
        Assert.Equal(SessionStart, raw.Payload);
        Assert.Equal(
            AecoPostMortem.Data.RawPayload.ContentHashOfText(SessionStart),
            raw.ContentHash);
    }
}
