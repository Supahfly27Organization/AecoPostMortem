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

    /// <summary>Minor (code review): proves the 500 path end to end over the real wire, not only
    /// via <c>RunGatedTests</c>' own direct unit-level exercise of <c>RunGated</c>'s catch branch —
    /// a real store-open failure (<see cref="LocalStore.GuardAgainstAForeignFile"/>) is a genuine way
    /// <see cref="ApiHost.RunIngest"/> can throw, and this confirms the client-observable contract
    /// (<c>readErrorMessage</c> in <c>web/src/api/settings.ts</c> is written against) is real: a
    /// `Results.Problem` body carrying the real exception message, not a bare unexplained 500.</summary>
    [Fact]
    public async Task Posting_ingest_against_a_store_path_that_is_not_a_sqlite_database_reports_a_real_problem_detail()
    {
        using var temporary = new TemporaryStore();
        Directory.CreateDirectory(temporary.Folder);
        await File.WriteAllTextAsync(temporary.Store.FilePath, "not a sqlite database", Cancellation);

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.PostAsync(ApiHost.IngestRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Cancellation);
            Assert.Contains("not a SQLite database", body, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    static string MissingCopilotRoot(TemporaryStore temporary) =>
        Path.Combine(temporary.Folder, "no-such-copilot-root");

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
