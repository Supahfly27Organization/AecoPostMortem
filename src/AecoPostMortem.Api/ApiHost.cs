using System.Text.Json;
using System.Text.Json.Serialization;
using AecoPostMortem.Data;
using AecoPostMortem.Findings;
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

    /// <summary>The route template <see cref="Build"/> registers for FR-21's session endpoint
    /// (S-08). Use <see cref="SessionRoute"/> to build the concrete request path for one session.</summary>
    public const string SessionRouteTemplate = "/api/sessions/{sessionId}";

    /// <summary>The route template <see cref="Build"/> registers for FR-21 part 2 of 3's step
    /// evidence endpoint (S-52, issue #16) — the inspector's Thinking and Raw tabs. Use
    /// <see cref="StepEvidenceRoute"/> to build the concrete request path for one step.</summary>
    public const string StepEvidenceRouteTemplate = "/api/sessions/{sessionId}/steps/{stepId}";

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

        app.MapGet(SessionRouteTemplate, (string sessionId) =>
        {
            var envelope = GetSession(store, sessionId);
            return envelope is null ? Results.NotFound() : Results.Ok(envelope);
        });

        app.MapGet(StepEvidenceRouteTemplate, (string sessionId, string stepId, string? kind) =>
        {
            if (kind is null || !Enum.TryParse<SessionTapeStepKind>(kind, ignoreCase: true, out var parsedKind))
            {
                return Results.BadRequest("A valid 'kind' query parameter is required.");
            }

            var evidence = GetStepEvidence(store, sessionId, stepId, parsedKind);
            return evidence is null ? Results.NotFound() : Results.Ok(evidence);
        });

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

    /// <summary>Builds the concrete request path for one session's <see cref="SessionRouteTemplate"/>
    /// endpoint, escaping the id the same way any other path segment would need to be.</summary>
    public static string SessionRoute(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return $"/api/sessions/{Uri.EscapeDataString(sessionId)}";
    }

    /// <summary>Builds the concrete request path for one step's <see cref="StepEvidenceRouteTemplate"/>
    /// endpoint. <paramref name="kind"/> is carried as a query parameter, not a route segment: a
    /// step's identity alone (<see cref="SessionTapeStep.StepId"/>) does not say which envelope
    /// field to match it against — a client already has <see cref="SessionTapeStepKind"/> on the
    /// selected step from the tape it just fetched.</summary>
    public static string StepEvidenceRoute(string sessionId, string stepId, SessionTapeStepKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        var kindText = kind.ToString();
        var kindQuery = Uri.EscapeDataString(char.ToLowerInvariant(kindText[0]).ToString() + kindText[1..]);
        return $"/api/sessions/{Uri.EscapeDataString(sessionId)}/steps/{Uri.EscapeDataString(stepId)}?kind={kindQuery}";
    }

    /// <summary>
    /// FR-21's masthead and tape (S-08, issue #15), read through <c>Data.Execution</c> — the
    /// minimal read path this story needs. Nothing in this repository yet writes
    /// <c>Turn</c>/<c>ToolCall</c>/<c>Agent</c>/<c>Skill</c>/<c>Hook</c> rows at ingest time
    /// (`AecoPostMortem.Ingestion/CLAUDE.md`, "not yet wired into the store"), so today this reads
    /// whatever those tables carry — nothing, on a store no writer has populated yet — rather than
    /// re-deriving them from RAW in this project, which would duplicate the ETL wiring a later
    /// story owns. <see langword="null"/> when <paramref name="sessionId"/> names no session at
    /// all, distinct from a session that exists but recorded no steps (Scenario 3).
    /// </summary>
    public static SessionEnvelope? GetSession(LocalStore store, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        using var context = store.Open();

        var session = context.Sessions.SingleOrDefault(s => s.SessionId == sessionId);
        if (session is null)
        {
            return null;
        }

        var turns = context.Turns.Where(t => t.SessionId == sessionId).ToList();
        var toolCalls = context.ToolCalls.Where(t => t.SessionId == sessionId).ToList();
        var agents = context.Agents.Where(a => a.SessionId == sessionId).ToList();
        var skills = context.Skills.Where(s => s.SessionId == sessionId).ToList();
        var hooks = context.Hooks.Where(h => h.SessionId == sessionId).ToList();

        var recording = SessionRecording.Build(session, turns, toolCalls, agents, skills, hooks);

        // No check orchestrator runs against the live store yet (FR-21 part 2 of 3, S-52, issue
        // #16) — the same "not yet wired to a live corpus" gap `ProcessDigest`/`DigestEnvelope`
        // document for their own `findings` input (`Findings/CLAUDE.md`, `Api/CLAUDE.md`). An empty
        // list is the honest answer today: `SessionFindings.For` still runs, so the chip row's own
        // designed "no findings" state is real, not a placeholder skipped by short-circuiting here.
        var findings = SessionFindings.For(sessionId, []);

        return SessionEnvelope.From(recording, findings, FindingEnvelope.From);
    }

    /// <summary>
    /// FR-21 part 2 of 3 (S-52, issue #16): the inspector's Thinking and Raw tabs for one step,
    /// resolved straight from the session's own <see cref="RawEvent"/>s (<see cref="StepEvidenceLookup"/>)
    /// — the Detail tab needs no query of its own, since every field it renders already travels on
    /// the tape's own <see cref="SessionTapeStepEnvelope"/>. <see langword="null"/> only when
    /// <paramref name="sessionId"/> names no session at all, the same "session not found" distinction
    /// <see cref="GetSession"/> draws — a session that exists but carries no raw event for this step
    /// still answers with <see cref="StepEvidenceEnvelope"/>'s own skipped/unavailable states, never
    /// a 404.
    /// </summary>
    public static StepEvidenceEnvelope? GetStepEvidence(
        LocalStore store, string sessionId, string stepId, SessionTapeStepKind kind)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        using var context = store.Open();

        var sessionExists = context.Sessions.Any(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            return null;
        }

        var events = context.RawEvents.Where(e => e.SessionId == sessionId).ToList();
        return StepEvidenceLookup.Find(events, kind, stepId);
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
