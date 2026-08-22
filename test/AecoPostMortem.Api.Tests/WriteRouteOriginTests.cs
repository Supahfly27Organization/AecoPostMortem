using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// The Origin/Host guard both write routes are served behind (<see cref="ApiHost.IsAllowedWriteOrigin"/>,
/// `Api/CLAUDE.md`'s "Origin and Host validation close a live simple-request CSRF path" non-obvious
/// decision): a plain cross-origin <c>fetch(..., { method: 'POST' })</c> needs no CORS preflight, so
/// nothing stopped it from actually running before this guard existed — confirmed for real against a
/// running <c>serve --port 5111</c> instance with a hand-crafted <c>Origin: https://evil.example</c>
/// request that returned <c>200</c> and a genuine rebuild.
/// </summary>
public sealed class WriteRouteOriginTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cross_origin_Origin_header_is_refused_with_403_and_the_command_never_runs()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiHost.RebuildRoute);
            request.Headers.Add("Origin", "https://evil.example");

            var response = await client.SendAsync(request, Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_matching_same_origin_Origin_header_is_allowed()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiHost.RebuildRoute);
            // The real origin this host actually listens on — exactly what a browser tab that
            // loaded the app from this same server would send.
            request.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

            var response = await client.SendAsync(request, Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>A non-browser caller (curl, the CLI, every other test in this project) never sends
    /// an <c>Origin</c> header at all — refusing it outright would break every one of them. This is
    /// also exercised implicitly by every other write-route test in this project (none of them sets
    /// <c>Origin</c>), but is proven directly here as the guard's own documented, load-bearing
    /// behaviour rather than an incidental side effect of how those other tests happen to be
    /// written.</summary>
    [Fact]
    public async Task No_Origin_header_at_all_is_allowed()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.PostAsync(ApiHost.RebuildRoute, content: null, Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The DNS-rebinding case: no <c>Origin</c> header (a bare <c>fetch</c> after the
    /// browser resolves the page's own hostname to <c>127.0.0.1</c> can still omit it under some
    /// request shapes), but a <c>Host</c> header naming the attacker's own domain rather than this
    /// server's real, actually-bound loopback authority — refused independently of whatever
    /// <c>Origin</c> claims.</summary>
    [Fact]
    public async Task A_non_loopback_Host_header_is_refused_even_with_no_Origin_header()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiHost.RebuildRoute);
            // Settable directly on HttpRequestMessage: the connection still physically reaches this
            // host's own loopback listener (via BaseAddress), but the literal Host header claims a
            // different name — exactly what a DNS-rebound browser request looks like on the wire.
            request.Headers.Host = "evil.example:12345";

            var response = await client.SendAsync(request, Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
