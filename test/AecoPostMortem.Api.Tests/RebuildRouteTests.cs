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
/// The Settings surface's second write route (Part B): POST /api/rebuild runs the identical
/// sequence the CLI's own <c>rebuild</c> command runs (<see cref="ApiHost.RunRebuild"/>'s own
/// remarks, via the shared <c>NormalizedLayerWriter.RebuildAll</c>) — drop-and-recreate the derived
/// schema, then re-derive every session RAW still holds. RAW itself is never read from disk again;
/// the source directory is not consulted.
/// </summary>
public sealed class RebuildRouteTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Posting_rebuild_repopulates_the_derived_layer_from_raw_alone()
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

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.PostAsync(ApiHost.RebuildRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<RebuildResultEnvelope>(Cancellation);

            Assert.NotNull(result);
            Assert.Equal(1, result!.RawEventCount);
            Assert.Equal(1, result.SessionCount);
            Assert.True(result.DurationSeconds >= 0);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }

        using var reopened = temporary.Store.Open();
        var session = Assert.Single(reopened.Sessions);
        Assert.Equal("s1", session.SessionId);
    }

    [Fact]
    public async Task Posting_rebuild_against_a_store_with_no_raw_events_succeeds_and_repopulates_nothing()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.PostAsync(ApiHost.RebuildRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<RebuildResultEnvelope>(Cancellation);

            Assert.NotNull(result);
            Assert.Equal(0, result!.RawEventCount);
            Assert.Equal(0, result.SessionCount);
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
}
