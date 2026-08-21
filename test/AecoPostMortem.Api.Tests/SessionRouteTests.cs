using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-21's session endpoint (S-08, issue #15): serves <see cref="SessionEnvelope"/> for one
/// session, read through <c>Data.Execution</c> — the minimal read path this story needs, since
/// nothing yet writes those rows at ingest time (`AecoPostMortem.Ingestion/CLAUDE.md`, "not yet
/// wired into the store"). Tests write directly through <c>PostMortemContext</c> to stand in for
/// that future writer, the same way <c>OwnershipTests</c> (`AecoPostMortem.Data.Tests`) do.
/// </summary>
public sealed class SessionRouteTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static readonly JsonSerializerOptions ClientOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task A_reconstructed_session_serves_its_masthead_and_tape()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s1",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s1/events.jsonl",
                Cwd = @"C:\repo",
                Repository = "org/repo",
                Branch = "main",
            });
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc1",
                ToolName = "view",
                StartedAt = "2026-08-16T10:00:01Z",
                OwnerKind = OwnerKind.Main,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s1"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("s1", envelope!.Masthead.SessionId);
            Assert.Equal("org/repo", envelope.Masthead.Repository);
            var step = Assert.Single(envelope.Steps);
            Assert.Equal("tc1", step.StepId);
            // Mockup parity item #4: a session with no real violation gets an honestly empty chip
            // row, not a placeholder — proves the real wiring below doesn't spuriously fire.
            Assert.Empty(envelope.Findings);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Mockup parity item #4 (`docs/product-superpowers/prioritization/2026-08-21-mockup-
    /// parity-gaps.md`, item #4): the chip row is wired to a real corpus scan — the same
    /// hook-failure check <c>DigestRouteTests</c> already exercises for <c>ApiHost.GetDigest</c>,
    /// here scoped to this session's own repository via <c>Session.Repository</c> rather than an
    /// explicitly-selected one.</summary>
    [Fact]
    public async Task A_session_with_a_real_violation_serves_a_non_empty_finding_chip_row()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s8",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s8/events.jsonl",
                Cwd = @"C:\repo",
                Repository = "org/repo",
            });
            const string startPayload = """{"id":"h1","data":{"hookInvocationId":"inv-1","hookType":"sessionStart"}}""";
            const string endPayload = """{"id":"h2","data":{"hookInvocationId":"inv-1","hookType":"sessionStart","success":false,"error":{"message":"ParserError: bad token"}}}""";
            context.RawEvents.Add(new RawEvent(
                "s8", 0, "hook.start", "2026-08-16T10:00:01Z", "0.0.339",
                "events.jsonl", 0, "hash-0", startPayload));
            context.RawEvents.Add(new RawEvent(
                "s8", 1, "hook.end", "2026-08-16T10:00:02Z", "0.0.339",
                "events.jsonl", 100, "hash-1", endPayload));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s8"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var chip = Assert.Single(envelope!.Findings);
            Assert.Equal(1, chip.SessionsAffected);
            Assert.Contains(
                chip.Finding.Evidence, item => item.Field == "data.error" && item.Value == "ParserError: bad token");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>A violation in a different repository than this session's own must never leak into
    /// this session's chip row — the same real-filter guarantee <c>DigestRouteTests</c>' own
    /// <c>A_finding_in_the_non_selected_repository_never_appears_in_the_ranked_list</c> proves for
    /// <c>ApiHost.GetDigest</c>.</summary>
    [Fact]
    public async Task A_violation_in_a_different_repository_never_appears_in_this_sessions_chip_row()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s9",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s9/events.jsonl",
                Cwd = @"C:\repo",
                Repository = "org/this-repo",
            });
            context.Sessions.Add(new Session
            {
                SessionId = "s10",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s10/events.jsonl",
                Cwd = @"C:\repo",
                Repository = "org/other-repo",
            });
            const string startPayload = """{"id":"h1","data":{"hookInvocationId":"inv-1","hookType":"sessionStart"}}""";
            const string endPayload = """{"id":"h2","data":{"hookInvocationId":"inv-1","hookType":"sessionStart","success":false,"error":{"message":"ParserError: bad token"}}}""";
            context.RawEvents.Add(new RawEvent(
                "s10", 0, "hook.start", "2026-08-16T10:00:01Z", "0.0.339",
                "events.jsonl", 0, "hash-0", startPayload));
            context.RawEvents.Add(new RawEvent(
                "s10", 1, "hook.end", "2026-08-16T10:00:02Z", "0.0.339",
                "events.jsonl", 100, "hash-1", endPayload));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s9"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Empty(envelope!.Findings);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>S-12 (FR-25, issue #21): a skill invocation is served as its own tape step carrying
    /// its name, plugin and plugin version (Scenario 1), and a skill invoked inside a subagent
    /// serves that subagent's lane rather than the main thread's (Scenario 2) — read through the
    /// real <c>Data.Execution.Skill</c> table, the same read path <c>ApiHost.GetSession</c> already
    /// exercises for every other step kind.</summary>
    [Fact]
    public async Task A_reconstructed_sessions_skill_invocations_serve_as_their_own_steps_with_lane_attribution()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s3",
                StartedAt = "2026-08-16T10:00:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s3/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.Skills.Add(new Skill
            {
                SessionId = "s3",
                EventId = "sk1",
                Name = "code-review",
                InvokedAt = "2026-08-16T10:00:01Z",
                PluginName = "superpowers",
                PluginVersion = "6.3.0",
                OwnerKind = OwnerKind.Agent,
                AgentId = "a1",
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s3"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var step = Assert.Single(envelope!.Steps);
            Assert.Equal(SessionTapeStepKind.Skill, step.Kind);
            Assert.Equal("code-review", step.Label);
            Assert.Equal("superpowers", step.PluginName);
            Assert.Equal("6.3.0", step.PluginVersion);
            Assert.Equal(OwnerKind.Agent, step.OwnerKind);
            Assert.Equal("a1", step.AgentId);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_session_with_no_recorded_steps_still_serves_its_masthead_with_an_empty_tape()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s2",
                StartedAt = "2026-08-16T10:00:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s2/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s2"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("s2", envelope!.Masthead.SessionId);
            Assert.Empty(envelope.Steps);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>FR-21 part 3 of 3 (S-53, issue #17), Scenario 3: a session with no recorded end
    /// states that it is incomplete rather than serving its partial tape as final.</summary>
    [Fact]
    public async Task A_session_with_no_recorded_end_is_served_as_ingest_incomplete()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s3",
                StartedAt = "2026-08-16T10:00:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s3/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s3"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.IsType<SessionRecordingStatusEnvelope.IngestIncomplete>(envelope!.Status);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Scenario 4: a session whose reconstruction leaves a subagent spawn unresolved
    /// states that reconstruction failed and what was skipped — read from the session's own RAW
    /// events, the same events <c>ExecutionRecordBuilder</c> reconstructs from at ingest time.</summary>
    [Fact]
    public async Task A_session_with_an_unresolvable_spawn_is_served_as_reconstruction_failed()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s4",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s4/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.SaveChanges();
        }

        // A subagent.started event whose toolCallId never appears as a task tool.execution_start's
        // own toolCallId — ExecutionRecordBuilder counts it as an unresolvable spawn (Ingestion's
        // own SpawnResolutionCheck), never silently drops it.
        const string payload = """{"id":"e1","data":{"toolCallId":"orphan-agent"}}""";
        using (var context = temporary.Store.Open())
        {
            context.RawEvents.Add(new RawEvent(
                "s4", 0, "subagent.started", "2026-08-16T10:00:05Z", "0.0.339",
                @"~/.copilot/session-state/s4/events.jsonl", 0,
                RawPayload.ContentHashOfText(payload), payload));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s4"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var failed = Assert.IsType<SessionRecordingStatusEnvelope.ReconstructionFailed>(envelope!.Status);
            Assert.Single(failed.Skipped);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>FR-22 (S-09, issue #18), Scenarios 1 and 2: a subagent's lane serves the last
    /// <c>assistant.message</c> under its own <c>agentId</c> as its output — never the parent's
    /// truncated <c>read_agent</c> completion, even though both exist in this session's own RAW
    /// events.</summary>
    [Fact]
    public async Task A_subagents_lane_serves_its_own_report_not_the_parents_truncated_stub()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s5",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s5/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.Agents.Add(new Agent
            {
                SessionId = "s5",
                AgentId = "a1",
                SpawningToolCallId = "a1",
                Name = "general-purpose",
                DisplayName = "General Purpose Agent",
                StartedAt = "2026-08-16T10:00:01Z",
                Outcome = AgentOutcome.Completed,
            });
            context.SaveChanges();
        }

        const string report = """{"id":"e1","data":{"content":"The real, much longer report from the subagent itself."},"agentId":"a1"}""";
        const string stub = """{"id":"e2","data":{"toolName":"read_agent","result":{"content":"Perfect! Task 1 is complete.\n\n(Full response provided to agent)"}}}""";
        using (var context = temporary.Store.Open())
        {
            context.RawEvents.Add(new RawEvent(
                "s5", 0, "assistant.message", "2026-08-16T10:00:02Z", "0.0.339",
                @"~/.copilot/session-state/s5/events.jsonl", 0, RawPayload.ContentHashOfText(report), report));
            context.RawEvents.Add(new RawEvent(
                "s5", 1, "tool.execution_complete", "2026-08-16T10:00:03Z", "0.0.339",
                @"~/.copilot/session-state/s5/events.jsonl", 100, RawPayload.ContentHashOfText(stub), stub));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s5"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var lane = Assert.Single(envelope!.Lanes);
            Assert.Equal("a1", lane.AgentId);
            var output = Assert.IsType<SubagentOutputEnvelope.Present>(lane.Output);
            Assert.Equal("The real, much longer report from the subagent itself.", output.Text);
            Assert.DoesNotContain("Full response provided to agent", output.Text);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Scenario 3: a subagent with no messages of its own states that plainly rather than
    /// falling back to anything else.</summary>
    [Fact]
    public async Task A_subagent_with_no_messages_of_its_own_serves_a_not_recorded_lane()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s6",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s6/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.Agents.Add(new Agent
            {
                SessionId = "s6",
                AgentId = "a1",
                SpawningToolCallId = "a1",
                Name = "general-purpose",
                DisplayName = "General Purpose Agent",
                StartedAt = "2026-08-16T10:00:01Z",
                Outcome = AgentOutcome.Completed,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s6"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var lane = Assert.Single(envelope!.Lanes);
            Assert.IsType<SubagentOutputEnvelope.NotRecorded>(lane.Output);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Scenario 4: a failed subagent's lane serves the failure and its recorded error.</summary>
    [Fact]
    public async Task A_failed_subagents_lane_serves_its_failure_and_recorded_error()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s7",
                StartedAt = "2026-08-16T10:00:00Z",
                EndedAt = "2026-08-16T10:10:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s7/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.Agents.Add(new Agent
            {
                SessionId = "s7",
                AgentId = "a1",
                SpawningToolCallId = "a1",
                Name = "general-purpose",
                DisplayName = "General Purpose Agent",
                StartedAt = "2026-08-16T10:00:01Z",
                Outcome = AgentOutcome.Failed,
                Error = "MCP tool timed out",
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<SessionEnvelope>(
                ApiHost.SessionRoute("s7"), ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var lane = Assert.Single(envelope!.Lanes);
            Assert.Equal(AgentOutcome.Failed, lane.Outcome);
            var failed = Assert.IsType<SubagentOutputEnvelope.Failed>(lane.Output);
            Assert.Equal("MCP tool timed out", failed.Error);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task An_unknown_session_id_is_reported_as_not_found()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(ApiHost.SessionRoute("no-such-session"), Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_tool_calls_raw_event_is_served_verbatim()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s1",
                StartedAt = "2026-08-16T10:00:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s1/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.RawEvents.Add(new RawEvent(
                "s1", 1, "tool.execution_start", "2026-08-16T10:00:01Z", "0.0.339",
                "events.jsonl", 0, "hash-1",
                """{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}"""));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var evidence = await client.GetFromJsonAsync<StepEvidenceEnvelope>(
                ApiHost.StepEvidenceRoute("s1", "tc1", SessionTapeStepKind.ToolCall), ClientOptions, Cancellation);

            Assert.NotNull(evidence);
            var raw = Assert.IsType<RawStepEventEnvelope.Present>(evidence!.Raw);
            Assert.Equal("tool.execution_start", raw.EventType);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_step_with_no_matching_raw_event_reports_skipped_not_a_404()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "s1",
                StartedAt = "2026-08-16T10:00:00Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/s1/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var evidence = await client.GetFromJsonAsync<StepEvidenceEnvelope>(
                ApiHost.StepEvidenceRoute("s1", "tc-missing", SessionTapeStepKind.ToolCall), ClientOptions, Cancellation);

            Assert.NotNull(evidence);
            Assert.IsType<RawStepEventEnvelope.Skipped>(evidence!.Raw);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task Step_evidence_for_an_unknown_session_is_reported_as_not_found()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(
                ApiHost.StepEvidenceRoute("no-such-session", "tc1", SessionTapeStepKind.ToolCall), Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    static string MissingCopilotRoot(TemporaryStore temporary) =>
        System.IO.Path.Combine(temporary.Folder, "no-such-copilot-root");

    static HttpClient HttpClientFor(WebApplication app) =>
        new() { BaseAddress = new Uri(ListeningAddress(app), UriKind.Absolute) };

    static string ListeningAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
}
