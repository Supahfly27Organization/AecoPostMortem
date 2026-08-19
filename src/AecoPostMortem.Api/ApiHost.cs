using System.Text.Json;
using System.Text.Json.Serialization;
using AecoPostMortem.Data;
using AecoPostMortem.Ingestion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api;

/// <summary>
/// Builds the local API and web shell host (S-48): the JSON app-state endpoint every surface reads
/// before it renders anything and — when a built web app is available — the static files that make
/// this the one process the operator runs (PRD §3.1: "served by <c>serve</c> from the same process;
/// there is no separate dev server in the shipped product").
/// </summary>
public static class ApiHost
{
    public const string AppStateRoute = "/api/app-state";

    /// <summary>
    /// Builds the host without starting it — the caller decides when and how to run it, which is
    /// what keeps this testable without a Kestrel listener staying up for the life of a test run.
    /// </summary>
    /// <param name="webRootPath">The built web app's static output (e.g. <c>web/dist</c>), if one
    /// is available. Left <see langword="null"/>, or pointed at a directory that does not exist,
    /// the host still serves <see cref="AppStateRoute"/> — it just has no web shell to hand back,
    /// the same as any machine that has not run <c>scripts/build-web.ps1</c>. <c>dotnet build</c>
    /// and <c>dotnet test</c> never run that script (`web/CLAUDE.md`), so this stays optional
    /// rather than a hard dependency on Node being installed.</param>
    public static WebApplication Build(
        LocalStore store,
        string copilotSessionStateRoot,
        int port,
        string? webRootPath = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(copilotSessionStateRoot);

        var resolvedWebRoot = webRootPath is not null && Directory.Exists(webRootPath)
            ? Path.GetFullPath(webRootPath)
            : null;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = resolvedWebRoot,
        });

        // 127.0.0.1 rather than "localhost": Kestrel refuses a dynamic port (0) bound to the
        // "localhost" host name, which port-0 tests need, and 127.0.0.1 is what "localhost"
        // resolves to for the operator's browser anyway.
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        // A camelCase string discriminator ("noSourceFound") is what a client actually reads off
        // the wire, matching the camelCase property names ASP.NET Core's minimal APIs already use
        // by default — JsonStringEnumConverter does not inherit that naming policy on its own, so
        // it is named again here, explicitly, or "EmptyStore" would reach the client instead of
        // the "emptyStore" AppStateKind.ts (`web/src/api/appState.ts`) is written against.
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        var app = builder.Build();

        app.MapGet(AppStateRoute, () => Results.Ok(DiagnoseAppState(store, copilotSessionStateRoot)));

        if (resolvedWebRoot is not null)
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");
        }

        return app;
    }

    /// <summary>
    /// The same diagnosis <see cref="AppStateRoute"/> serves, exposed directly so the CLI's
    /// <c>serve</c> command can print the same fix-command wording at startup without making an
    /// HTTP request to itself.
    /// </summary>
    public static AppStateReport DiagnoseAppState(LocalStore store, string copilotSessionStateRoot)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(copilotSessionStateRoot);

        var copilotSourceFound = SessionDiscovery.Discover(copilotSessionStateRoot).RootFound;
        var storeHasBeenIngested = StoreHasBeenIngested(store);

        return AppStateReport.Diagnose(copilotSourceFound, storeHasBeenIngested);
    }

    /// <summary>
    /// Opens a fresh context per call rather than caching one for the host's lifetime. This runs on
    /// every <see cref="AppStateRoute"/> request, so it re-applies <c>Database.Migrate()</c> and
    /// <c>DerivedSchema.EnsureCurrent</c> each time — cheap against one local SQLite file fetched
    /// once per page load (today's only caller, `web/src/api/useAppState.ts`), but worth revisiting
    /// if this endpoint is ever polled repeatedly rather than fetched once.
    /// </summary>
    static bool StoreHasBeenIngested(LocalStore store)
    {
        if (!store.Exists)
        {
            return false;
        }

        using var context = store.Open();
        return context.RawEvents.Any();
    }
}
