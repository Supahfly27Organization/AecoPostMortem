using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-41's real orchestration (S-36, issue #44): <see cref="ApiHost.GetDigest"/> assembles a live
/// <see cref="ProcessDigest"/> from six of the seven waste/missing-capability check orchestrators,
/// read straight through <c>Data.Execution</c> the same way <see cref="SessionRouteTests"/> already
/// exercises <c>ApiHost.GetSession</c>.
/// </summary>
public sealed class DigestRouteTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static readonly JsonSerializerOptions ClientOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    static Session ASession(string sessionId, string? repository = null) => new()
    {
        SessionId = sessionId,
        StartedAt = "2026-08-16T10:00:00Z",
        EndedAt = "2026-08-16T10:10:00Z",
        CopilotVersion = "0.0.339",
        EventSchemaVersion = "1",
        SourceFile = $@"~/.copilot/session-state/{sessionId}/events.jsonl",
        Cwd = @"C:\repo",
        Repository = repository,
    };

    [Fact]
    public async Task An_empty_store_serves_an_analyzed_digest_with_no_findings()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(DigestState.Analyzed, envelope!.State);
            Assert.Empty(envelope.RankedFindings);
            Assert.Equal(0, envelope.Masthead.SessionCount);
            Assert.Null(envelope.Masthead.SpanStart);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task Four_repeated_reads_of_one_path_serve_as_a_ranked_finding()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            for (var i = 0; i < 4; i++)
            {
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "s1",
                    ToolCallId = $"tc{i}",
                    ToolName = "view",
                    Path = "/repeated.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
            }
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(envelope!.RankedFindings);
            Assert.Equal(FindingClass.Waste, finding.Class);
            Assert.Contains(finding.Evidence, item => item.Field == "data.path" && item.Value == "/repeated.cs");
            Assert.Equal(1, envelope.Masthead.SessionCount);
            Assert.Equal(4, envelope.Masthead.ToolCallCount);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_failed_hook_pair_serves_its_error_text_read_from_raw()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            const string startPayload = """{"id":"h1","data":{"hookInvocationId":"inv-1","hookType":"sessionStart"}}""";
            const string endPayload = """{"id":"h2","data":{"hookInvocationId":"inv-1","hookType":"sessionStart","success":false,"error":{"message":"ParserError: bad token"}}}""";
            context.RawEvents.Add(new RawEvent(
                "s1", 0, "hook.start", "2026-08-16T10:00:01Z", "0.0.339",
                "events.jsonl", 0, "hash-0", startPayload));
            context.RawEvents.Add(new RawEvent(
                "s1", 1, "hook.end", "2026-08-16T10:00:02Z", "0.0.339",
                "events.jsonl", 100, "hash-1", endPayload));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(envelope!.RankedFindings);
            Assert.Contains(finding.Evidence, item => item.Field == "data.error" && item.Value == "ParserError: bad token");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_report_intent_call_that_returns_to_an_earlier_phase_serves_a_phase_churn_finding()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            context.SaveChanges();
        }

        using (var context = temporary.Store.Open())
        {
            var events = new[]
            {
                Intent("s1", 0, "2026-08-16T10:00:01Z", "explore"),
                Intent("s1", 1, "2026-08-16T10:00:02Z", "implement"),
                Intent("s1", 2, "2026-08-16T10:00:03Z", "explore"),
            };
            context.RawEvents.AddRange(events);
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Contains(envelope!.RankedFindings, f => f.Evidence.Any(e => e.Field == "returns" && e.Value == "1"));
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task The_digest_scopes_findings_to_the_repository_with_the_most_sessions()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("majority-1", "org/majority"));
            context.Sessions.Add(ASession("majority-2", "org/majority"));
            context.Sessions.Add(ASession("minority-1", "org/minority"));

            for (var i = 0; i < 4; i++)
            {
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "minority-1",
                    ToolCallId = $"tc{i}",
                    ToolName = "view",
                    Path = "/minority-only.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
            }
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("org/majority", envelope!.Masthead.RepositoryScope.SelectedRepository);
            Assert.Equal(
                new[] { "org/majority", "org/minority" },
                envelope.Masthead.RepositoryScope.AvailableRepositories);
            Assert.Empty(envelope.RankedFindings);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Proves the scoping is a real filter, not just a correct default pick: a qualifying
    /// finding in the minority repository must not leak into the majority repository's ranked list
    /// even when both repositories genuinely have one.</summary>
    [Fact]
    public async Task A_finding_in_the_non_selected_repository_never_appears_in_the_ranked_list()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("majority-1", "org/majority"));
            context.Sessions.Add(ASession("majority-2", "org/majority"));
            context.Sessions.Add(ASession("minority-1", "org/minority"));

            for (var i = 0; i < 4; i++)
            {
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "majority-1",
                    ToolCallId = $"maj-tc{i}",
                    ToolName = "view",
                    Path = "/majority-only.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "minority-1",
                    ToolCallId = $"min-tc{i}",
                    ToolName = "view",
                    Path = "/minority-only.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
            }
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("org/majority", envelope!.Masthead.RepositoryScope.SelectedRepository);
            var finding = Assert.Single(envelope.RankedFindings);
            Assert.Contains(finding.Evidence, item => item.Field == "data.path" && item.Value == "/majority-only.cs");
            Assert.DoesNotContain(finding.Evidence, item => item.Value == "/minority-only.cs");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    static RawEvent SystemMessage(string sessionId, string content, long sequence = 0) => new(
        sessionId,
        sequence,
        "system.message",
        "2026-08-16T10:00:00Z",
        "0.0.339",
        $"events-{sessionId}.jsonl",
        sequence,
        RawPayload.ContentHashOfText(content),
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "system.message",
            ["data"] = new Dictionary<string, string> { ["content"] = content },
        }));

    const string BannedToolPrompt = """
        <custom_instruction>
        CLAUDE.md
        - Never use grep.
        </custom_instruction>
        """;

    /// <summary>Piece 3's second slice: a banned tool actually called serves a
    /// <see cref="FindingClass.RuleAdherenceToolChoice"/> finding, wired the same way the other six
    /// checks already are.</summary>
    [Fact]
    public async Task A_banned_tool_actually_called_serves_a_rule_adherence_finding()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            context.RawEvents.Add(SystemMessage("s1", BannedToolPrompt));
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc1",
                ToolName = "grep",
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
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(
                envelope!.RankedFindings, f => f.Class == FindingClass.RuleAdherenceToolChoice);
            Assert.Contains(finding.Evidence, item => item.Field == "named_tool" && item.Value == "grep");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    static RawEvent Intent(string sessionId, long sequence, string timestamp, string intent)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = $"e{sequence}",
            data = new
            {
                toolCallId = $"tc{sequence}",
                toolName = "report_intent",
                arguments = new { intent },
            },
        });
        return new RawEvent(
            sessionId, sequence, "tool.execution_start", timestamp, "0.0.339",
            $"~/.copilot/session-state/{sessionId}/events.jsonl", sequence, $"hash-{sequence}", payload);
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
