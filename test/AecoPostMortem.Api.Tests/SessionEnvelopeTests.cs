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

    static SessionFindings NoFindings() => SessionFindings.For("s1", []);

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
        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

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

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

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

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        var observed = Assert.IsType<SessionTokenFiguresEnvelope.Observed>(envelope.Masthead.ContextSize);
        Assert.Equal(12_345, observed.InputTokens);
        Assert.Equal(6_789, observed.OutputTokens);
    }

    [Fact]
    public void Elapsed_is_null_when_the_session_never_ended()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        Assert.Null(envelope.Masthead.ElapsedMs);
    }

    [Fact]
    public void A_session_with_no_steps_serialises_an_empty_step_list()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", "2026-08-16T10:01:00Z");
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        Assert.Empty(envelope.Steps);
    }

    /// <summary>S-12, Scenario 1 (FR-25, issue #21): a skill step's plugin name and version travel
    /// onto the wire alongside its name (<see cref="SessionTapeStepEnvelope.Label"/>), not folded
    /// into it.</summary>
    [Fact]
    public void A_skill_steps_plugin_name_and_version_are_carried_on_the_wire()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var skills = new[]
        {
            new Skill
            {
                SessionId = "s1",
                EventId = "sk1",
                Name = "code-review",
                InvokedAt = "2026-08-16T10:00:01Z",
                PluginName = "superpowers",
                PluginVersion = "6.3.0",
                OwnerKind = OwnerKind.Main,
            },
        };

        var recording = SessionRecording.Build(session, [], [], [], skills, []);
        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        var step = Assert.Single(envelope.Steps);
        Assert.Equal("code-review", step.Label);
        Assert.Equal("superpowers", step.PluginName);
        Assert.Equal("6.3.0", step.PluginVersion);
    }

    /// <summary>S-12, Scenario 2 (FR-25, issue #21): a skill invoked inside a subagent serialises
    /// its lane attribution (<c>ownerKind: "agent"</c> plus its <c>agentId</c>) rather than the main
    /// thread's.</summary>
    [Fact]
    public void A_skill_invoked_inside_a_subagent_serialises_that_agents_lane()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var skills = new[]
        {
            new Skill
            {
                SessionId = "s1",
                EventId = "sk1",
                Name = "test-driven-development",
                InvokedAt = "2026-08-16T10:00:01Z",
                OwnerKind = OwnerKind.Agent,
                AgentId = "a1",
            },
        };

        var recording = SessionRecording.Build(session, [], [], [], skills, []);
        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        var step = Assert.Single(envelope.Steps);
        Assert.Equal(OwnerKind.Agent, step.OwnerKind);
        Assert.Equal("a1", step.AgentId);
    }

    /// <summary>FR-21 part 3 of 3 (S-53, issue #17): a complete session's status serialises to the
    /// closed union's "complete" shape, not a bare boolean.</summary>
    [Fact]
    public void A_complete_session_serialises_its_status_as_the_complete_shape()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", "2026-08-16T10:10:00Z");
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        Assert.IsType<SessionRecordingStatusEnvelope.Complete>(envelope.Status);
    }

    /// <summary>Scenario 3: a session with no recorded end serialises a distinct
    /// "ingestIncomplete" kind, never folded into a generic error shape.</summary>
    [Fact]
    public void An_incomplete_session_serialises_a_distinct_kind_not_a_generic_error()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

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
            Provenance = Provenance.Observed,
        };
        var recording = SessionRecording.Build(session, [], [], [], [], [], spawnResolution);

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        var failed = Assert.IsType<SessionRecordingStatusEnvelope.ReconstructionFailed>(envelope.Status);
        Assert.Single(failed.Skipped);
    }

    [Fact]
    public void The_step_kind_serialises_in_camelCase()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var toolCalls = new[] { ToolCall("tc1", "view", "2026-08-16T10:00:01Z") };
        var recording = SessionRecording.Build(session, [], toolCalls, [], [], []);
        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        var json = JsonSerializer.Serialize(envelope, options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("toolCall", document.RootElement.GetProperty("steps")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void Findings_affecting_this_session_are_mapped_to_chips_with_their_sessions_affected_count()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var finding = new Finding
        {
            Class = FindingClass.Waste,
            Provenance = Provenance.Derived,
            Headline = "The pre-commit hook failed repeatedly",
            Evidence = [],
            Recurrence = new Recurrence
            {
                Key = "hook:pre-commit",
                Occurrences =
                [
                    new RecurrenceOccurrence { SessionId = "s1" },
                    new RecurrenceOccurrence { SessionId = "s2" },
                ],
            },
        };
        var findings = SessionFindings.For("s1", [finding]);

        var envelope = SessionEnvelope.From(recording, findings, FindingEnvelope.From);

        var chip = Assert.Single(envelope.Findings);
        Assert.Equal(2, chip.SessionsAffected);
        Assert.Equal(FindingClass.Waste, chip.Finding.Class);
    }

    [Fact]
    public void A_session_with_no_findings_serialises_an_empty_chip_list()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        Assert.Empty(envelope.Findings);
    }

    /// <summary>FR-22 (S-09, issue #18): a caller that supplies no lanes argument still serialises an
    /// empty list, never a missing field — the same "empty list is the designed state, not an
    /// omission" discipline <see cref="SessionEnvelope.Findings"/> already documents.</summary>
    [Fact]
    public void No_lanes_argument_serialises_an_empty_lane_list()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From);

        Assert.Empty(envelope.Lanes);
    }

    /// <summary>FR-22 (S-09, issue #18): supplied lanes travel onto the envelope unchanged — the
    /// caller (<c>ApiHost.GetSession</c>) is the one that resolves each subagent's own output, this
    /// factory only carries the result.</summary>
    [Fact]
    public void Supplied_lanes_are_carried_onto_the_envelope()
    {
        var session = SessionWith("2026-08-16T10:00:00Z", null);
        var recording = SessionRecording.Build(session, [], [], [], [], []);
        var agent = new Agent
        {
            SessionId = "s1",
            AgentId = "a1",
            SpawningToolCallId = "a1",
            Name = "general-purpose",
            DisplayName = "General Purpose Agent",
            StartedAt = "2026-08-16T10:00:01Z",
            Outcome = AgentOutcome.Completed,
        };
        var lane = SessionAgentLaneEnvelope.From(agent, new SubagentOutputEnvelope.Present { Text = "Done." });

        var envelope = SessionEnvelope.From(recording, NoFindings(), FindingEnvelope.From, [lane]);

        var servedLane = Assert.Single(envelope.Lanes);
        Assert.Equal("a1", servedLane.AgentId);
        Assert.Equal("General Purpose Agent", servedLane.DisplayName);
        Assert.Equal(AgentOutcome.Completed, servedLane.Outcome);
        var output = Assert.IsType<SubagentOutputEnvelope.Present>(servedLane.Output);
        Assert.Equal("Done.", output.Text);
    }
}
