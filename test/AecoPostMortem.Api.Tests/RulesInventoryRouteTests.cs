using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-40's own not-yet-wired gap, closed here (`AecoPostMortem.Api/CLAUDE.md`'s own status note):
/// <see cref="ApiHost.GetRulesInventory"/> resolves a whole store's <see cref="RawEvent"/>s into
/// <see cref="Rules.SessionRuleSet"/>s (<see cref="SessionRuleSetLookup"/>), classifies each statement
/// with <see cref="RulesInventoryClassifier"/>, and serves <see cref="RulesInventory.Build"/>'s result.
/// </summary>
public sealed class RulesInventoryRouteTests
{
    const string ProviderVersion = "0.0.339";

    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static readonly JsonSerializerOptions ClientOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    static Session ASession(string sessionId, string? repository, string startedAt) => new()
    {
        SessionId = sessionId,
        StartedAt = startedAt,
        EndedAt = null,
        CopilotVersion = ProviderVersion,
        EventSchemaVersion = "1",
        SourceFile = $@"~/.copilot/session-state/{sessionId}/events.jsonl",
        Cwd = @"C:\repo",
        Repository = repository,
    };

    static RawEvent SystemMessage(string sessionId, string content, long sequence = 0) => new(
        sessionId,
        sequence,
        "system.message",
        "2026-08-16T10:00:00Z",
        ProviderVersion,
        $"events-{sessionId}.jsonl",
        sequence,
        RawPayload.ContentHashOfText(content),
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "system.message",
            ["data"] = new Dictionary<string, string> { ["content"] = content },
        }));

    const string Prompt = """
        <custom_instruction>
        CLAUDE.md
        - Never commit a file larger than five megabytes.
        - Task → Read These First
        </custom_instruction>
        """;

    [Fact]
    public async Task An_empty_store_answers_404_there_is_no_version_to_select()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(ApiHost.RulesInventoryRoute, Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_sessions_own_statements_serve_with_the_narrowed_real_classification()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.RawEvents.Add(SystemMessage("s1", Prompt));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<RulesInventoryEnvelope>(
                ApiHost.RulesInventoryRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("org/repo", envelope!.SelectedVersion.Repository);
            Assert.Equal(RulesInventoryState.Listed, envelope.State);
            Assert.Equal(2, envelope.Rows.Count);

            var checkable = envelope.Rows.Single(row => row.Text == "Never commit a file larger than five megabytes.");
            Assert.IsType<RuleStatementStatusEnvelope.CheckableNotYetBuiltStatus>(checkable.Status);

            var notARule = envelope.Rows.Single(row => row.Text == "Task → Read These First");
            Assert.IsType<RuleStatementStatusEnvelope.NotARuleStatus>(notARule.Status);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task The_inventory_scopes_to_the_repository_with_the_most_sessions()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("majority-1", "org/majority", "2026-08-16T10:00:00Z"));
            context.Sessions.Add(ASession("majority-2", "org/majority", "2026-08-16T11:00:00Z"));
            context.Sessions.Add(ASession("minority-1", "org/minority", "2026-08-16T12:00:00Z"));
            context.RawEvents.Add(SystemMessage("minority-1", Prompt));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<RulesInventoryEnvelope>(
                ApiHost.RulesInventoryRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("org/majority", envelope!.SelectedVersion.Repository);
            // org/majority's two sessions carried no custom_instruction block at all.
            Assert.Equal(RulesInventoryState.NoInstructionBlocks, envelope.State);
            Assert.Empty(envelope.Rows);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task An_unknown_version_hash_answers_404()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(
                $"{ApiHost.RulesInventoryRoute}?{ApiHost.VersionParameter}=does-not-exist", Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    const string BannedToolPrompt = """
        <custom_instruction>
        CLAUDE.md
        - Never use curl.
        </custom_instruction>
        """;

    /// <summary>Mockup parity item #7: a Watched <see cref="RuleShapeKind.ToolIsBanned"/> row serves
    /// the real count <see cref="BannedToolFinding"/> produces over this method's own corpus-wide
    /// scope — the exact tool calls <see cref="RulesInventoryClassifier"/> already resolved this
    /// row's Watched status against.</summary>
    [Fact]
    public async Task A_watched_tool_is_banned_row_serves_its_real_violation_count()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.RawEvents.Add(SystemMessage("s1", BannedToolPrompt));
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc1",
                ToolName = "curl",
                StartedAt = "2026-08-16T10:00:01Z",
                OwnerKind = OwnerKind.Main,
            });
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc2",
                ToolName = "curl",
                StartedAt = "2026-08-16T10:00:02Z",
                OwnerKind = OwnerKind.Main,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<RulesInventoryEnvelope>(
                ApiHost.RulesInventoryRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var row = envelope!.Rows.Single(r => r.Text == "Never use curl.");
            Assert.IsType<RuleStatementStatusEnvelope.WatchedStatus>(row.Status);
            var counted = Assert.IsType<RuleViolationCountEnvelope.CountedViolations>(row.ViolationCount);
            Assert.Equal(2, counted.Count);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    const string PreferAOverBPrompt = """
        <custom_instruction>
        CLAUDE.md
        - Prefer rg over grep.
        </custom_instruction>
        """;

    /// <summary>Mockup parity item #7's honest-absence path: a Watched
    /// <see cref="RuleShapeKind.PreferAOverB"/> row — today's one Watchable shape with no
    /// Finding-producing orchestrator — never renders a fabricated or zero-by-default count.</summary>
    [Fact]
    public async Task A_watched_row_whose_shape_has_no_built_check_renders_an_honest_absence()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.RawEvents.Add(SystemMessage("s1", PreferAOverBPrompt));
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc1",
                ToolName = "rg",
                StartedAt = "2026-08-16T10:00:01Z",
                OwnerKind = OwnerKind.Main,
            });
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc2",
                ToolName = "grep",
                StartedAt = "2026-08-16T10:00:02Z",
                OwnerKind = OwnerKind.Main,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<RulesInventoryEnvelope>(
                ApiHost.RulesInventoryRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var row = envelope!.Rows.Single(r => r.Text == "Prefer rg over grep.");
            Assert.IsType<RuleStatementStatusEnvelope.WatchedStatus>(row.Status);
            Assert.IsType<RuleViolationCountEnvelope.NoBuiltCheck>(row.ViolationCount);
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
