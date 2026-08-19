using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// S-48: the app-state endpoint the web shell reads before it renders anything, and the static
/// files that make the built web app reachable from the same process (issue #11).
/// </summary>
public sealed class ApiHostTests
{
    static readonly JsonSerializerOptions ClientOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task App_state_reports_no_source_found_when_the_Copilot_directory_does_not_exist()
    {
        using var temporary = new TemporaryStore();
        var missingRoot = Path.Combine(temporary.Folder, "no-such-copilot-root");

        await using var app = ApiHost.Build(temporary.Store, missingRoot, port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            var report = await FetchAppState(app);

            Assert.Equal(AppStateKind.NoSourceFound, report.Kind);
            Assert.Null(report.FixCommand);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task App_state_reports_empty_store_and_names_the_ingest_command_when_the_source_exists_but_nothing_was_ingested()
    {
        using var temporary = new TemporaryStore();
        var copilotRoot = Path.Combine(temporary.Folder, "copilot-session-state");
        Directory.CreateDirectory(copilotRoot);

        await using var app = ApiHost.Build(temporary.Store, copilotRoot, port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            var report = await FetchAppState(app);

            Assert.Equal(AppStateKind.EmptyStore, report.Kind);
            Assert.Equal(AppStateReport.IngestCommand, report.FixCommand);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task App_state_reports_ready_once_the_store_carries_a_RAW_event()
    {
        using var temporary = new TemporaryStore();
        var copilotRoot = Path.Combine(temporary.Folder, "copilot-session-state");
        Directory.CreateDirectory(copilotRoot);

        using (var context = temporary.Store.Open())
        {
            context.RawEvents.Add(new RawEvent(
                "session-1", 0, "session.start", "2026-08-09T20:14:36.758Z", "0.0.339",
                @"~/.copilot/session-state/session-1/events.jsonl", 0,
                RawPayload.ContentHashOfText("{}"), "{}"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, copilotRoot, port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            var report = await FetchAppState(app);

            Assert.Equal(AppStateKind.Ready, report.Kind);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>
    /// Regression test: the wire format is camelCase (<c>"emptyStore"</c>), matching the property
    /// naming ASP.NET Core's minimal APIs already use by default and what
    /// <c>web/src/api/appState.ts</c>'s <c>AppStateKind</c> union is written against.
    /// <c>JsonStringEnumConverter</c> does not inherit the naming policy on its own — a version of
    /// this host once shipped <c>"EmptyStore"</c> because of exactly that, silently mismatching the
    /// frontend's contract without either side's own tests catching it.
    /// </summary>
    [Fact]
    public async Task The_kind_field_is_serialised_as_camelCase_on_the_wire()
    {
        using var temporary = new TemporaryStore();
        var copilotRoot = Path.Combine(temporary.Folder, "copilot-session-state");
        Directory.CreateDirectory(copilotRoot);

        await using var app = ApiHost.Build(temporary.Store, copilotRoot, port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var json = await client.GetStringAsync(ApiHost.AppStateRoute, Cancellation);

            Assert.Contains("\"kind\":\"emptyStore\"", json, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task With_no_built_web_app_the_host_still_serves_the_API()
    {
        using var temporary = new TemporaryStore();
        var missingRoot = Path.Combine(temporary.Folder, "no-such-copilot-root");

        await using var app = ApiHost.Build(temporary.Store, missingRoot, port: 0, webRootPath: null);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(ApiHost.AppStateRoute, Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_built_web_apps_index_page_is_served_as_a_static_file()
    {
        using var temporary = new TemporaryStore();
        var missingRoot = Path.Combine(temporary.Folder, "no-such-copilot-root");

        var webRoot = Path.Combine(temporary.Folder, "web-dist");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(
            Path.Combine(webRoot, "index.html"),
            "<!doctype html><title>AecoPostMortem</title>",
            Cancellation);

        await using var app = ApiHost.Build(temporary.Store, missingRoot, port: 0, webRootPath: webRoot);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync("/", Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(
                "AecoPostMortem",
                await response.Content.ReadAsStringAsync(Cancellation),
                StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>DiagnoseAppState is the same diagnosis without a listener, for the CLI's own
    /// startup message (issue #11).</summary>
    [Fact]
    public void DiagnoseAppState_matches_what_the_endpoint_serves_without_starting_a_host()
    {
        using var temporary = new TemporaryStore();
        var missingRoot = Path.Combine(temporary.Folder, "no-such-copilot-root");

        var report = ApiHost.DiagnoseAppState(temporary.Store, missingRoot);

        Assert.Equal(AppStateKind.NoSourceFound, report.Kind);
    }

    static async Task<AppStateReport> FetchAppState(WebApplication app)
    {
        using var client = HttpClientFor(app);
        var report = await client.GetFromJsonAsync<AppStateReport>(
            ApiHost.AppStateRoute, ClientOptions, Cancellation);
        Assert.NotNull(report);
        return report!;
    }

    static HttpClient HttpClientFor(WebApplication app) =>
        new() { BaseAddress = new Uri(ListeningAddress(app), UriKind.Absolute) };

    static string ListeningAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
}
