using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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

    static string MissingCopilotRoot(TemporaryStore temporary) =>
        System.IO.Path.Combine(temporary.Folder, "no-such-copilot-root");

    static HttpClient HttpClientFor(WebApplication app) =>
        new() { BaseAddress = new Uri(ListeningAddress(app), UriKind.Absolute) };

    static string ListeningAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
}
