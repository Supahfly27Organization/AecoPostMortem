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
/// Piece 4 (FR-39, S-35, issue #43): <see cref="ApiHost.GetMonitorComparison"/> wires
/// <see cref="ApiHost.MonitorComparisonRoute"/> to a live store — the not-yet-wired gap
/// `AecoPostMortem.Api/CLAUDE.md`'s own status note names for <see cref="Findings.MonitorComparison"/>.
/// </summary>
public sealed class MonitorComparisonRouteTests
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

    const string PreferRgOverGrepPrompt = """
        <custom_instruction>
        CLAUDE.md
        - Prefer rg over grep.
        </custom_instruction>
        """;

    static ToolCall AToolCall(string sessionId, string toolCallId, string toolName) => new()
    {
        SessionId = sessionId,
        ToolCallId = toolCallId,
        ToolName = toolName,
        StartedAt = "2026-08-16T10:00:01Z",
        OwnerKind = OwnerKind.Main,
    };

    [Fact]
    public async Task Missing_before_or_after_answers_400()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(ApiHost.MonitorComparisonRoute, Cancellation);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task An_empty_store_serves_a_stated_no_repository_reason()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(
                $"{ApiHost.MonitorComparisonRoute}?{ApiHost.BeforeParameter}=h1&{ApiHost.AfterParameter}=h2",
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("noRepository", await KindOf(response));
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Still a 404: a hash no session in this repository ever carried names a resource that
    /// does not exist, unlike the three refusals here — all real, designed states about a pair that
    /// does exist. The same split <c>GetRulesInventory</c> already draws for a missing version.
    /// </summary>
    [Fact]
    public async Task An_unknown_hash_answers_404()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.RawEvents.Add(SystemMessage("s1", PreferRgOverGrepPrompt));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(
                $"{ApiHost.MonitorComparisonRoute}?{ApiHost.BeforeParameter}=does-not-exist"
                + $"&{ApiHost.AfterParameter}=does-not-exist-either",
                Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task Non_adjacent_versions_serve_a_stated_reason_naming_what_lies_between_them()
    {
        using var temporary = new TemporaryStore();
        string hashV1, hashV3;
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.RawEvents.Add(SystemMessage("s1", PreferRgOverGrepPrompt));

            context.Sessions.Add(ASession("s2", "org/repo", "2026-08-16T11:00:00Z"));
            const string middlePrompt = """
                <custom_instruction>
                CLAUDE.md
                - Prefer rg over grep.
                - Never read secrets.env.
                </custom_instruction>
                """;
            context.RawEvents.Add(SystemMessage("s2", middlePrompt, sequence: 1));

            context.Sessions.Add(ASession("s3", "org/repo", "2026-08-16T12:00:00Z"));
            const string laterPrompt = """
                <custom_instruction>
                CLAUDE.md
                - Prefer rg over grep.
                - Never read secrets.env.
                - Always pass an explicit timeout.
                </custom_instruction>
                """;
            context.RawEvents.Add(SystemMessage("s3", laterPrompt, sequence: 2));

            context.SaveChanges();

            hashV1 = RuleSetVersionHasher.ComputeHash(
                RuleStatementExtractor.ExtractBlocks(PreferRgOverGrepPrompt));
            hashV3 = RuleSetVersionHasher.ComputeHash(RuleStatementExtractor.ExtractBlocks(laterPrompt));
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(
                $"{ApiHost.MonitorComparisonRoute}?{ApiHost.BeforeParameter}={hashV1}"
                + $"&{ApiHost.AfterParameter}={hashV3}",
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
            Assert.Equal("notAdjacent", document.RootElement.GetProperty("kind").GetString());

            // The versions between the pair — already computed by the server for its own exception,
            // now served, so a client can say *why* a pair is not adjacent rather than only that it
            // is not.
            Assert.Equal(1, document.RootElement.GetProperty("intervening").GetArrayLength());
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task After_version_with_no_PreferAOverB_statement_serves_a_stated_reason()
    {
        using var temporary = new TemporaryStore();
        string hashV1, hashV2;
        const string neverReadPrompt = """
            <custom_instruction>
            CLAUDE.md
            - Never read secrets.env.
            </custom_instruction>
            """;
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.RawEvents.Add(SystemMessage("s1", PreferRgOverGrepPrompt));

            context.Sessions.Add(ASession("s2", "org/repo", "2026-08-16T11:00:00Z"));
            context.RawEvents.Add(SystemMessage("s2", neverReadPrompt, sequence: 1));

            context.SaveChanges();

            hashV1 = RuleSetVersionHasher.ComputeHash(
                RuleStatementExtractor.ExtractBlocks(PreferRgOverGrepPrompt));
            hashV2 = RuleSetVersionHasher.ComputeHash(RuleStatementExtractor.ExtractBlocks(neverReadPrompt));
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(
                $"{ApiHost.MonitorComparisonRoute}?{ApiHost.BeforeParameter}={hashV1}"
                + $"&{ApiHost.AfterParameter}={hashV2}",
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("noComparableRule", await KindOf(response));
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    static async Task<string?> KindOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("kind").GetString();
    }

    const string PreferRgOverGrepPlusNeverReadPrompt = """
        <custom_instruction>
        CLAUDE.md
        - Prefer rg over grep.
        - Never read secrets.env.
        </custom_instruction>
        """;

    [Fact]
    public async Task An_adjacent_pair_serves_the_shared_PreferAOverB_rules_own_adherence_on_each_side()
    {
        using var temporary = new TemporaryStore();
        string hashV1, hashV2;
        using (var context = temporary.Store.Open())
        {
            // RuleSetVersionAdjacency orders a repository's versions by each version's own
            // FirstSessionStartedAt (Rules/CLAUDE.md) — s1's StartedAt precedes s2's below, so this
            // pair is adjacent by real time, not merely by session id text.
            context.Sessions.Add(ASession("s1", "org/repo", "2026-08-16T10:00:00Z"));
            context.RawEvents.Add(SystemMessage("s1", PreferRgOverGrepPrompt));
            context.ToolCalls.Add(AToolCall("s1", "tc1", "rg"));
            context.ToolCalls.Add(AToolCall("s1", "tc2", "grep"));
            context.ToolCalls.Add(AToolCall("s1", "tc3", "grep"));
            context.ToolCalls.Add(AToolCall("s1", "tc4", "grep"));

            // The rule edit: a second statement is added, changing the version's content hash while
            // "Prefer rg over grep." itself stays present on both sides.
            context.Sessions.Add(ASession("s2", "org/repo", "2026-08-16T11:00:00Z"));
            context.RawEvents.Add(SystemMessage("s2", PreferRgOverGrepPlusNeverReadPrompt, sequence: 1));
            context.ToolCalls.Add(AToolCall("s2", "tc5", "rg"));
            context.ToolCalls.Add(AToolCall("s2", "tc6", "rg"));
            context.ToolCalls.Add(AToolCall("s2", "tc7", "rg"));
            context.ToolCalls.Add(AToolCall("s2", "tc8", "grep"));

            context.SaveChanges();

            hashV1 = RuleSetVersionHasher.ComputeHash(
                RuleStatementExtractor.ExtractBlocks(PreferRgOverGrepPrompt));
            hashV2 = RuleSetVersionHasher.ComputeHash(
                RuleStatementExtractor.ExtractBlocks(PreferRgOverGrepPlusNeverReadPrompt));
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            // The comparison payload is unchanged; it is now nested under the union's own
            // `comparison` arm rather than being the whole body — see
            // `MonitorComparisonResultEnvelope`'s remarks for why the three refusals are served as
            // stated reasons instead of one bodyless 404.
            var result = await client.GetFromJsonAsync<MonitorComparisonResultEnvelope>(
                $"{ApiHost.MonitorComparisonRoute}?{ApiHost.BeforeParameter}={hashV1}"
                + $"&{ApiHost.AfterParameter}={hashV2}",
                ClientOptions,
                Cancellation);

            var envelope = Assert.IsType<MonitorComparisonResultEnvelope.ComparisonResult>(result).Comparison;
            Assert.Equal(hashV1, envelope.BeforeVersion.Hash);
            Assert.Equal(1, envelope.BeforeVersion.SessionCount);
            Assert.Equal(hashV2, envelope.AfterVersion.Hash);
            Assert.Equal(1, envelope.AfterVersion.SessionCount);

            // before1: 1 rg against 3 grep -> 25%.
            Assert.Equal(25d, envelope.Before.Percentage);
            // after1: 3 rg against 1 grep -> 75%.
            Assert.Equal(75d, envelope.After.Percentage);
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
