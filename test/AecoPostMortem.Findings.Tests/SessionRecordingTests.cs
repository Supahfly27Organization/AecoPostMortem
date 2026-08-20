using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-21, part 1 of 3 (S-08, issue #15): the Flight Recorder's masthead and time-ordered tape. Each
/// test name maps to one of the story's three Gherkin scenarios.
/// </summary>
public sealed class SessionRecordingTests
{
    /// <summary>Scenario 1: the masthead states session identity, repository, branch, CLI version,
    /// elapsed time, turns, tool calls, subagents, skills, models and context size at end.</summary>
    [Fact]
    public void The_masthead_states_what_the_session_was()
    {
        var session = SessionWith(
            startedAt: "2026-08-16T10:00:00Z",
            endedAt: "2026-08-16T10:30:00Z",
            repository: "Supahfly27Organization/AecoPostMortem",
            branch: "main",
            copilotVersion: "0.0.339",
            modelCount: 2,
            inputTokens: 12_345,
            outputTokens: 6_789);

        var turns = new[] { BuildTurn("s1", "t1", "2026-08-16T10:00:01Z") };
        var toolCalls = new[] { BuildToolCall("s1", "tc1", "view", "2026-08-16T10:00:02Z") };
        var agents = new[] { BuildAgent("s1", "a1") };
        var skills = new[] { BuildSkill("s1", "sk1", "code-review", "2026-08-16T10:00:03Z") };
        var hooks = new[] { BuildHook("s1", "h1", "pre-commit", "2026-08-16T10:00:04Z") };

        var recording = SessionRecording.Build(session, turns, toolCalls, agents, skills, hooks);

        var masthead = recording.Masthead;
        Assert.Equal("s1", masthead.SessionId);
        Assert.Equal("Supahfly27Organization/AecoPostMortem", masthead.Repository);
        Assert.Equal("main", masthead.Branch);
        Assert.Equal("0.0.339", masthead.CopilotVersion);
        Assert.Equal(TimeSpan.FromMinutes(30), masthead.Elapsed);
        Assert.Equal(1, masthead.TurnCount);
        Assert.Equal(1, masthead.ToolCallCount);
        Assert.Equal(1, masthead.SubagentCount);
        Assert.Equal(1, masthead.SkillCount);
        Assert.Equal(2, masthead.ModelCount);

        var contextSize = Assert.IsType<SessionTokenFigures.Observed>(masthead.ContextSize);
        Assert.Equal(12_345, contextSize.InputTokens);
        Assert.Equal(6_789, contextSize.OutputTokens);
    }

    /// <summary>A session that never wrote <c>session.shutdown</c> has an unknown elapsed time,
    /// never a zero one — the same "never zero-fill" discipline <c>SessionTokenFigures</c> uses.</summary>
    [Fact]
    public void Elapsed_time_is_null_when_the_session_never_ended()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);

        var recording = SessionRecording.Build(session, [], [], [], [], []);

        Assert.Null(recording.Masthead.Elapsed);
    }

    /// <summary>Scenario 2: hooks, prompts (turns), skills, tool calls and MCP calls all appear in
    /// the tape, ordered by wall-clock time regardless of the order they were supplied in, each
    /// carrying its offset from session start.</summary>
    [Fact]
    public void The_tape_is_ordered_by_wall_clock_time_with_offsets_from_session_start()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: "2026-08-16T10:10:00Z");

        // Supplied out of chronological order on purpose.
        var toolCalls = new[]
        {
            BuildToolCall("s1", "tc1", "view", "2026-08-16T10:00:05Z"),
            BuildToolCall("s1", "tc2", "search", "2026-08-16T10:00:01Z", mcpServerName: "codebase-memory", mcpToolName: "search_graph"),
        };
        var turns = new[] { BuildTurn("s1", "t1", "2026-08-16T10:00:00Z") };
        var skills = new[] { BuildSkill("s1", "sk1", "code-review", "2026-08-16T10:00:03Z") };
        var hooks = new[] { BuildHook("s1", "h1", "pre-commit", "2026-08-16T10:00:02Z") };

        var recording = SessionRecording.Build(session, turns, toolCalls, [], skills, hooks);

        var steps = recording.Tape.Steps;
        Assert.Equal(5, steps.Count);
        Assert.Equal(
            [
                SessionTapeStepKind.Prompt,
                SessionTapeStepKind.McpCall,
                SessionTapeStepKind.Hook,
                SessionTapeStepKind.Skill,
                SessionTapeStepKind.ToolCall,
            ],
            steps.Select(step => step.Kind).ToArray());

        Assert.Equal(TimeSpan.Zero, steps[0].Offset);
        Assert.Equal(TimeSpan.FromSeconds(1), steps[1].Offset);
        Assert.Equal(TimeSpan.FromSeconds(2), steps[2].Offset);
        Assert.Equal(TimeSpan.FromSeconds(3), steps[3].Offset);
        Assert.Equal(TimeSpan.FromSeconds(5), steps[4].Offset);
    }

    /// <summary>A tool call carrying an MCP server name is a distinct tape step kind from a plain
    /// tool call, never folded into it.</summary>
    [Fact]
    public void A_tool_call_naming_an_MCP_server_is_reported_as_an_MCP_call_not_a_plain_tool_call()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);
        var toolCalls = new[]
        {
            BuildToolCall("s1", "tc1", "view", "2026-08-16T10:00:01Z"),
            BuildToolCall("s1", "tc2", "search_graph", "2026-08-16T10:00:02Z", mcpServerName: "codebase-memory", mcpToolName: "search_graph"),
        };

        var recording = SessionRecording.Build(session, [], toolCalls, [], [], []);

        Assert.Equal(SessionTapeStepKind.ToolCall, recording.Tape.Steps.Single(s => s.StepId == "tc1").Kind);
        Assert.Equal(SessionTapeStepKind.McpCall, recording.Tape.Steps.Single(s => s.StepId == "tc2").Kind);
    }

    /// <summary>Two steps can share a timestamp (two hooks logged in the same millisecond); the
    /// tie is broken deterministically by kind, then by the step's own id — the same reasoning
    /// <c>AbortedTurnCheck</c> gives its own tie-break (PRD §3.8) — rather than by whatever order
    /// the caller happened to supply the rows in.</summary>
    [Fact]
    public void Steps_sharing_a_timestamp_are_ordered_deterministically_by_kind_then_step_id()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);

        // Supplied in the opposite of the expected output order, on purpose.
        var hooks = new[]
        {
            BuildHook("s1", "h2", "post-commit", "2026-08-16T10:00:01Z"),
            BuildHook("s1", "h1", "pre-commit", "2026-08-16T10:00:01Z"),
        };
        var skills = new[] { BuildSkill("s1", "sk1", "code-review", "2026-08-16T10:00:02Z") };
        var toolCalls = new[] { BuildToolCall("s1", "tc1", "view", "2026-08-16T10:00:02Z") };

        var recording = SessionRecording.Build(session, [], toolCalls, [], skills, hooks);

        Assert.Equal(
            ["h1", "h2", "sk1", "tc1"],
            recording.Tape.Steps.Select(step => step.StepId).ToArray());
    }

    /// <summary>Scenario 3: one of the measured 2 of 35 sessions that made no tool call still
    /// renders a masthead, and the tape carries no steps for the recorder to state that plainly.</summary>
    [Fact]
    public void A_session_with_no_steps_still_renders_a_masthead_and_an_empty_tape()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: "2026-08-16T10:01:00Z");

        var recording = SessionRecording.Build(session, [], [], [], [], []);

        Assert.NotNull(recording.Masthead);
        Assert.Equal(0, recording.Masthead.ToolCallCount);
        Assert.Empty(recording.Tape.Steps);
        Assert.False(recording.Tape.HasSteps);
    }

    [Fact]
    public void Build_rejects_a_null_session()
    {
        Assert.Throws<ArgumentNullException>(() => SessionRecording.Build(null!, [], [], [], [], []));
    }

    /// <summary>S-12, Scenario 1 (FR-25, issue #21): a skill step carries its plugin name and
    /// version, not only the skill's own name (already carried as <see cref="SessionTapeStep.Label"/>).</summary>
    [Fact]
    public void A_skill_step_carries_its_plugin_name_and_version()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);
        var skills = new[]
        {
            BuildSkill("s1", "sk1", "code-review", "2026-08-16T10:00:01Z", pluginName: "superpowers", pluginVersion: "6.3.0"),
        };

        var recording = SessionRecording.Build(session, [], [], [], skills, []);

        var step = Assert.Single(recording.Tape.Steps);
        Assert.Equal("code-review", step.Label);
        Assert.Equal("superpowers", step.PluginName);
        Assert.Equal("6.3.0", step.PluginVersion);
    }

    /// <summary>A skill invoked with no plugin recorded (Copilot's own built-in skills) carries no
    /// plugin name or version, rather than an empty string standing in for absence.</summary>
    [Fact]
    public void A_skill_step_with_no_recorded_plugin_carries_neither_field()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);
        var skills = new[] { BuildSkill("s1", "sk1", "code-review", "2026-08-16T10:00:01Z") };

        var recording = SessionRecording.Build(session, [], [], [], skills, []);

        var step = Assert.Single(recording.Tape.Steps);
        Assert.Null(step.PluginName);
        Assert.Null(step.PluginVersion);
    }

    /// <summary>S-12, Scenario 2 (FR-25, issue #21): a skill invoked inside a subagent is attributed
    /// to that subagent's lane (<see cref="OwnerKind.Agent"/> plus its <c>AgentId</c>) rather than
    /// the main thread — the same generic <c>BuildStep</c> attribution every other step kind already
    /// gets from S-06/S-08, exercised here for <see cref="SessionTapeStepKind.Skill"/> specifically.</summary>
    [Fact]
    public void A_skill_invoked_inside_a_subagent_is_attributed_to_that_subagents_lane()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);
        var skills = new[]
        {
            BuildSkill("s1", "sk1", "code-review", "2026-08-16T10:00:01Z", ownerKind: OwnerKind.Agent, agentId: "a1"),
            BuildSkill("s1", "sk2", "test-driven-development", "2026-08-16T10:00:02Z"),
        };

        var recording = SessionRecording.Build(session, [], [], [], skills, []);

        var subagentStep = Assert.Single(recording.Tape.Steps, step => step.StepId == "sk1");
        Assert.Equal(OwnerKind.Agent, subagentStep.OwnerKind);
        Assert.Equal("a1", subagentStep.AgentId);

        var mainThreadStep = Assert.Single(recording.Tape.Steps, step => step.StepId == "sk2");
        Assert.Equal(OwnerKind.Main, mainThreadStep.OwnerKind);
        Assert.Null(mainThreadStep.AgentId);
    }

    /// <summary>FR-21, part 3 of 3 (S-53, issue #17), Scenario 1: a session that has recorded its
    /// end and had no reconstruction problem reads as final.</summary>
    [Fact]
    public void A_session_that_ended_cleanly_reads_as_complete()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: "2026-08-16T10:10:00Z");

        var recording = SessionRecording.Build(session, [], [], [], [], []);

        Assert.IsType<SessionRecordingStatus.Complete>(recording.Status);
    }

    /// <summary>Scenario 3: a session with no recorded end has not concluded — nothing about it,
    /// including today's masthead and tape figures, is the final picture yet.</summary>
    [Fact]
    public void A_session_with_no_recorded_end_reads_as_ingest_incomplete()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);

        var recording = SessionRecording.Build(session, [], [], [], [], []);

        Assert.IsType<SessionRecordingStatus.IngestIncomplete>(recording.Status);
    }

    /// <summary>Scenario 4: a session that ended but whose reconstruction could not resolve every
    /// spawn states why, and what was skipped — never a bare count with no explanation.</summary>
    [Fact]
    public void A_session_whose_reconstruction_left_a_spawn_unresolved_reads_as_reconstruction_failed_and_names_what_was_skipped()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: "2026-08-16T10:10:00Z");
        var spawnResolution = SpawnResolutionCheckEntry(examined: 5, unresolved: 2);

        var recording = SessionRecording.Build(session, [], [], [], [], [], spawnResolution);

        var failed = Assert.IsType<SessionRecordingStatus.ReconstructionFailed>(recording.Status);
        var skipped = Assert.Single(failed.Skipped);
        Assert.Contains("2", skipped);
        Assert.Contains("5", skipped);
    }

    /// <summary>A clean reconstruction (no unresolved spawns) reads as complete, not failed — a
    /// zero-finding check is a clean run, the same "Ran with FindingCount 0 is clean" discipline
    /// <c>CheckRegistryEntry</c> documents elsewhere.</summary>
    [Fact]
    public void A_session_whose_reconstruction_resolved_every_spawn_reads_as_complete()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: "2026-08-16T10:10:00Z");
        var spawnResolution = SpawnResolutionCheckEntry(examined: 5, unresolved: 0);

        var recording = SessionRecording.Build(session, [], [], [], [], [], spawnResolution);

        Assert.IsType<SessionRecordingStatus.Complete>(recording.Status);
    }

    /// <summary>An incomplete session takes priority over a reconstruction problem — the broader,
    /// more urgent claim wins, the same ordering <c>ProcessDigest.Build</c> gives
    /// <c>IngestInProgress</c> over its own analysis-state check.</summary>
    [Fact]
    public void An_incomplete_session_takes_priority_over_a_reconstruction_failure()
    {
        var session = SessionWith(startedAt: "2026-08-16T10:00:00Z", endedAt: null);
        var spawnResolution = SpawnResolutionCheckEntry(examined: 5, unresolved: 2);

        var recording = SessionRecording.Build(session, [], [], [], [], [], spawnResolution);

        Assert.IsType<SessionRecordingStatus.IngestIncomplete>(recording.Status);
    }

    static CheckRegistryEntry SpawnResolutionCheckEntry(int examined, int unresolved) => new()
    {
        CheckId = "unresolvable-spawn",
        Status = CheckRunStatus.Ran,
        Population = examined,
        FindingCount = unresolved,
        Provenance = Provenance.Observed,
    };

    static Session SessionWith(
        string startedAt,
        string? endedAt,
        string? repository = null,
        string? branch = null,
        string copilotVersion = "0.0.339",
        int? modelCount = null,
        long? inputTokens = null,
        long? outputTokens = null) => new()
    {
        SessionId = "s1",
        StartedAt = startedAt,
        EndedAt = endedAt,
        CopilotVersion = copilotVersion,
        EventSchemaVersion = "1",
        SourceFile = @"~/.copilot/session-state/s1/events.jsonl",
        Cwd = @"C:\repo",
        Repository = repository,
        Branch = branch,
        ModelCount = modelCount,
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
    };

    static Turn BuildTurn(string sessionId, string turnId, string startedAt) => new()
    {
        SessionId = sessionId,
        EventId = $"e-{turnId}",
        TurnId = turnId,
        StartedAt = startedAt,
        Outcome = TurnOutcome.Completed,
        OwnerKind = OwnerKind.Main,
    };

    static ToolCall BuildToolCall(
        string sessionId,
        string toolCallId,
        string toolName,
        string startedAt,
        string? mcpServerName = null,
        string? mcpToolName = null) => new()
    {
        SessionId = sessionId,
        ToolCallId = toolCallId,
        ToolName = toolName,
        StartedAt = startedAt,
        McpServerName = mcpServerName,
        McpToolName = mcpToolName,
        OwnerKind = OwnerKind.Main,
    };

    static Agent BuildAgent(string sessionId, string agentId) => new()
    {
        SessionId = sessionId,
        AgentId = agentId,
        SpawningToolCallId = agentId,
        Name = "explore",
        DisplayName = "Explore",
        StartedAt = "2026-08-16T10:00:00Z",
        Outcome = AgentOutcome.Completed,
    };

    static Skill BuildSkill(
        string sessionId,
        string eventId,
        string name,
        string invokedAt,
        string? pluginName = null,
        string? pluginVersion = null,
        OwnerKind ownerKind = OwnerKind.Main,
        string? agentId = null) => new()
    {
        SessionId = sessionId,
        EventId = eventId,
        Name = name,
        InvokedAt = invokedAt,
        PluginName = pluginName,
        PluginVersion = pluginVersion,
        OwnerKind = ownerKind,
        AgentId = agentId,
    };

    static Hook BuildHook(string sessionId, string eventId, string name, string startedAt) => new()
    {
        SessionId = sessionId,
        EventId = eventId,
        Name = name,
        StartedAt = startedAt,
        OwnerKind = OwnerKind.Main,
    };
}
