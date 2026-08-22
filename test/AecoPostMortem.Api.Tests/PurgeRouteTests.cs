using System.Net;
using System.Net.Http.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// The Settings surface's third — and only destructive — write route: POST /api/purge deletes the
/// operator's whole local store, the same thing the CLI's own <c>purge</c> command does
/// (<c>Data.LocalStore.Purge</c>). Unlike ingest and rebuild (both safe to replay uninvited —
/// <c>Api/CLAUDE.md</c>'s "Why this reasoning still does not extend to a hypothetical purge
/// endpoint"), this one destroys data, so it is served behind a third gate the other two do not
/// have: a required confirmation header, on top of the shared Origin/Host guard and the shared
/// write gate.
/// </summary>
public sealed class PurgeRouteTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Posting_purge_deletes_the_store_and_reports_what_it_deleted()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.RawEvents.Add(new RawEvent(
                "s1", 0, "session.start", "2026-08-16T10:00:00Z", "0.0.339",
                "events.jsonl", 0, "hash-0",
                """{"id":"e0","data":{"version":1,"copilotVersion":"0.0.339","context":{"cwd":"C:\\repo"}}}"""));
            context.SaveChanges();
        }

        Assert.True(File.Exists(temporary.Store.FilePath));

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.SendAsync(ConfirmedPurge(), Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<PurgeResultEnvelope>(Cancellation);

            Assert.NotNull(result);
            Assert.True(result!.DeletedAnything);
            Assert.Contains(temporary.Store.FilePath, result.DeletedFiles);
            Assert.True(result.BytesReclaimed > 0);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }

        Assert.False(File.Exists(temporary.Store.FilePath));
    }

    /// <summary>The CLI's own "Nothing to purge; there is no store at …" state, kept as a real
    /// served value rather than collapsed into a success that claims a deletion that never
    /// happened.</summary>
    [Fact]
    public async Task Posting_purge_when_there_is_no_store_reports_that_nothing_was_deleted()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.SendAsync(ConfirmedPurge(), Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<PurgeResultEnvelope>(Cancellation);

            Assert.NotNull(result);
            Assert.False(result!.DeletedAnything);
            Assert.Empty(result.DeletedFiles);
            Assert.Equal(0, result.BytesReclaimed);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The gate ingest and rebuild deliberately do not have. A request carrying no
    /// confirmation header is refused before <c>LocalStore.Purge</c> is ever reached — proven by the
    /// store still being on disk afterwards, not only by the status code.</summary>
    [Fact]
    public async Task Posting_purge_without_the_confirmation_header_is_refused_and_the_store_survives()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.PostAsync(ApiHost.PurgeRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }

        Assert.True(File.Exists(temporary.Store.FilePath));
    }

    [Fact]
    public async Task Posting_purge_with_the_wrong_confirmation_value_is_refused_and_the_store_survives()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiHost.PurgeRoute);
            request.Headers.Add(ApiHost.ConfirmationHeader, "rebuild");

            var response = await client.SendAsync(request, Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }

        Assert.True(File.Exists(temporary.Store.FilePath));
    }

    /// <summary>The confirmation header is an addition to the Origin/Host guard, never a replacement
    /// for it: a cross-origin request that knows the header's own name and value is still refused.
    /// </summary>
    [Fact]
    public async Task A_cross_origin_purge_carrying_a_valid_confirmation_header_is_still_refused()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            using var request = ConfirmedPurge();
            request.Headers.Add("Origin", "https://evil.example");

            var response = await client.SendAsync(request, Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }

        Assert.True(File.Exists(temporary.Store.FilePath));
    }

    /// <summary>
    /// Purge is served behind the *same* write gate as ingest and rebuild — a purge landing
    /// mid-ingest is exactly as unsafe as two ingests overlapping. <c>RunGatedTests</c> proves the
    /// gate's own refusal logic, but nothing proved which routes actually consult it; this holds the
    /// very gate the host was built with and asserts all three refuse, which is the wiring claim
    /// itself rather than a re-test of <c>RunGated</c>.
    ///
    /// Deterministic by construction: the gate is held before any request is made, and
    /// <c>RunGated</c> uses <c>Wait(0)</c>, so nothing blocks and no race has to be reproduced over a
    /// real socket — the same reasoning <c>RunGatedTests</c> records for testing the gate directly.
    /// </summary>
    [Fact]
    public async Task Every_write_route_including_purge_is_served_behind_the_one_shared_gate()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        using var writeGate = new SemaphoreSlim(1, 1);
        await writeGate.WaitAsync(Cancellation);

        await using var app = ApiHost.Build(
            temporary.Store, MissingCopilotRoot(temporary), port: 0, webRootPath: null, writeGate);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);

            var purge = await client.SendAsync(ConfirmedPurge(), Cancellation);
            var ingest = await client.PostAsync(ApiHost.IngestRoute, content: null, Cancellation);
            var rebuild = await client.PostAsync(ApiHost.RebuildRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.Conflict, purge.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, ingest.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, rebuild.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }

        // The refusal happened before LocalStore.Purge, not after it.
        Assert.True(File.Exists(temporary.Store.FilePath));
    }

    static HttpRequestMessage ConfirmedPurge()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiHost.PurgeRoute);
        request.Headers.Add(ApiHost.ConfirmationHeader, ApiHost.PurgeConfirmation);
        return request;
    }

    static string MissingCopilotRoot(TemporaryStore temporary) =>
        Path.Combine(temporary.Folder, "no-such-copilot-root");

    static HttpClient HttpClientFor(WebApplication app) =>
        new() { BaseAddress = new Uri(ListeningAddress(app), UriKind.Absolute) };

    static string ListeningAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
}
