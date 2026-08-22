using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AecoPostMortem.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// The Settings surface's first write route (Part B): POST /api/ingest runs the identical call the
/// CLI's own <c>ingest</c> command makes (<see cref="ApiHost.RunIngest"/>'s own remarks) and serves
/// back FR-14's coverage report — this codebase's first POST endpoint anywhere.
/// </summary>
public sealed class IngestRouteTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Posting_ingest_persists_a_real_session_and_serves_its_coverage_report()
    {
        using var temporary = new TemporaryStore();
        using var sessionState = new TemporarySessionState();
        sessionState.WriteEventsFile(
            "session-1",
            """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1}}""",
            """{"type":"assistant.turn_start","timestamp":"2026-05-07T14:16:49.000Z"}""");

        await using var app = ApiHost.Build(temporary.Store, sessionState.Root, port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.PostAsync(ApiHost.IngestRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<IngestResultEnvelope>(Cancellation);

            Assert.NotNull(result);
            Assert.Equal(1, result!.SessionsFound);
            Assert.Equal(1, result.SessionsIngested);
            Assert.Equal(2, result.LinesParsed);
            Assert.True(result.DurationSeconds >= 0);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }

        using var reopened = temporary.Store.Open();
        Assert.Equal(2, reopened.RawEvents.Count());
    }

    [Fact]
    public async Task Posting_ingest_against_a_missing_copilot_root_reports_zero_sessions_and_succeeds()
    {
        using var temporary = new TemporaryStore();
        var missingRoot = Path.Combine(temporary.Folder, "no-such-copilot-root");

        await using var app = ApiHost.Build(temporary.Store, missingRoot, port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.PostAsync(ApiHost.IngestRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<IngestResultEnvelope>(Cancellation);

            Assert.NotNull(result);
            Assert.Equal(0, result!.SessionsFound);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    static HttpClient HttpClientFor(WebApplication app) =>
        new() { BaseAddress = new Uri(ListeningAddress(app), UriKind.Absolute) };

    static string ListeningAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

    /// <summary>A throwaway Copilot session-state root, the same shape
    /// <c>AecoPostMortem.Cli.Tests.CommandRunnerTests</c>' own private helper uses — <c>ingest</c>'s
    /// default is the real machine's own directory, and a test reading it would make the outcome
    /// depend on whatever the machine running it happens to have.</summary>
    sealed class TemporarySessionState : IDisposable
    {
        public TemporarySessionState()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "AecoPostMortem.Tests",
                Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WriteEventsFile(string sessionId, params string[] lines)
        {
            var directory = Path.Combine(Root, sessionId);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "events.jsonl"), string.Join('\n', lines) + '\n');
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Never created, or already gone.
            }
        }
    }
}
