using System.Text.Json;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-21's served masthead and tape (S-08, issue #15): the wire shape a client reads
/// <see cref="SessionRecording"/> through, assembled the same way <see cref="DigestEnvelope"/> is
/// assembled from a <see cref="ProcessDigest"/> (S-36).
/// </summary>
public sealed class SessionEnvelopeTests
{
    static Session SessionWith(string startedAt, string? endedAt, long? inputTokens = null, long? outputTokens = null) => new()
    {
        SessionId = "s1",
        StartedAt = startedAt,
        EndedAt = endedAt,
        CopilotVersion = "0.0.339",
        EventSchemaVersion = "1",
        SourceFile = @"~/.copilot/session-state/s1/events.jsonl",
        Cwd = @"C:\repo",
        Repository = "org/repo",
        Branch = "main",
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
    };

    static ToolCall ToolCall(string toolCallId, string toolName, string startedAt, string? mcpServerName = null) => new()
    {
        SessionId = "s1",
        ToolCallId = toolCallId,
        ToolName = toolName,
        StartedAt = startedAt,
        McpServerName = mcpServerName,
        OwnerKind = OwnerKind.Main,
    };

    [Fact]
    public void From_carries_the_masthead_and_maps_every_step_in_order()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", "2026-08-16T10:10:00Z", 100, 50);
        var toolCalls = new[]
        {
            ToolCall("tc1", "view", "2026-08-16T10:00:02Z"),
            ToolCall("tc2", "search_graph", "2026-08-16T10:00:01Z", mcpServerName: "codebase-memory"),
        };

        var recording = SessionRecording.Build(session, [], toolCalls, [], [], []);
        var envelope = SessionEnvelope.From(recording);

        Assert.Equal("s1", envelope.Masthead.SessionId);
        Assert.Equal("org/repo", envelope.Masthead.Repository);
        Assert.Equal("main", envelope.Masthead.Branch);
        Assert.Equal((long)TimeSpan.FromMinutes(10).TotalMilliseconds, envelope.Masthead.ElapsedMs);

        Assert.Equal(2, envelope.Steps.Count);
        Assert.Equal("tc2", envelope.Steps[0].StepId);
        Assert.Equal(SessionTapeStepKind.McpCall, envelope.Steps[0].Kind);
        Assert.Equal("tc1", envelope.Steps[1].StepId);
        Assert.Equal(SessionTapeStepKind.ToolCall, envelope.Steps[1].Kind);
        Assert.Equal(1_000, envelope.Steps[0].OffsetMs);
        Assert.Equal(2_000, envelope.Steps[1].OffsetMs);
    }

    [Fact]
    public void An_unrecorded_context_size_serialises_as_an_explicit_state_not_a_missing_field()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording);

        var json = JsonSerializer.Serialize(envelope.Masthead.ContextSize);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("kind", out var kind));
        Assert.Equal("notRecorded", kind.GetString());
    }

    [Fact]
    public void An_observed_context_size_carries_its_token_totals()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null, inputTokens: 12_345, outputTokens: 6_789);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording);

        var observed = Assert.IsType<SessionTokenFiguresEnvelope.Observed>(envelope.Masthead.ContextSize);
        Assert.Equal(12_345, observed.InputTokens);
        Assert.Equal(6_789, observed.OutputTokens);
    }

    [Fact]
    public void Elapsed_is_null_when_the_session_never_ended()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording);

        Assert.Null(envelope.Masthead.ElapsedMs);
    }

    [Fact]
    public void A_session_with_no_steps_serialises_an_empty_step_list()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", "2026-08-16T10:01:00Z");
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording);

        Assert.Empty(envelope.Steps);
    }

    /// <summary>FR-21 part 3 of 3 (S-53, issue #17): a complete session's status serialises to the
    /// closed union's "complete" shape, not a bare boolean.</summary>
    [Fact]
    public void A_complete_session_serialises_its_status_as_the_complete_shape()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", "2026-08-16T10:10:00Z");
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording);

        Assert.IsType<SessionRecordingStatusEnvelope.Complete>(envelope.Status);
    }

    /// <summary>Scenario 3: a session with no recorded end serialises a distinct
    /// "ingestIncomplete" kind, never folded into a generic error shape.</summary>
    [Fact]
    public void An_incomplete_session_serialises_a_distinct_kind_not_a_generic_error()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording);

        var json = JsonSerializer.Serialize(envelope.Status);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("ingestIncomplete", document.RootElement.GetProperty("kind").GetString());
    }

    /// <summary>Scenario 4: a reconstruction failure carries what was skipped on the wire, not only
    /// a discriminator.</summary>
    [Fact]
    public void A_reconstruction_failure_carries_what_was_skipped()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", "2026-08-16T10:10:00Z");
        var spawnResolution = new CheckRegistryEntry
        {
            CheckId = "unresolvable-spawn",
            Status = CheckRunStatus.Ran,
            Population = 5,
            FindingCount = 2,
        };
        var recording = SessionRecording.Build(session, [], [], [], [], [], spawnResolution);

        var envelope = SessionEnvelope.From(recording);

        var failed = Assert.IsType<SessionRecordingStatusEnvelope.ReconstructionFailed>(envelope.Status);
        Assert.Single(failed.Skipped);
    }

    [Fact]
    public void The_step_kind_serialises_in_camelCase()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var toolCalls = new[] { ToolCall("tc1", "view", "2026-08-16T10:00:01Z") };
        var recording = SessionRecording.Build(session, [], toolCalls, [], [], []);
        var envelope = SessionEnvelope.From(recording);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        var json = JsonSerializer.Serialize(envelope, options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("toolCall", document.RootElement.GetProperty("steps")[0].GetProperty("kind").GetString());
    }
}
