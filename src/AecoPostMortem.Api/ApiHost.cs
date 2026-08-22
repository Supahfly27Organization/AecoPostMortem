using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;
using AecoPostMortem.Ingestion;
using AecoPostMortem.Rules;
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

    /// <summary>FR-41's digest route (S-36, issue #44), first served for real here.</summary>
    public const string DigestRoute = "/api/digest";

    /// <summary>The optional date-range filter's two query parameters — both plain calendar dates
    /// (<c>yyyy-MM-dd</c>), inclusive of the whole named day (<see cref="StartOfDayUtc"/>/
    /// <see cref="EndOfDayUtc"/>). Omitted, <see cref="GetDigest"/> behaves exactly as it did before
    /// this filter existed. See the "A date-range filter re-scopes the whole analysis" non-obvious
    /// decision in <c>Api/CLAUDE.md</c> for why this narrows <em>which sessions every check runs
    /// over</em> rather than merely which already-computed findings are displayed.</summary>
    public const string FromParameter = "from";

    public const string ToParameter = "to";

    /// <summary>FR-40's Rules Inventory route (S-22, issue #35), first served for real here.</summary>
    public const string RulesInventoryRoute = "/api/rules-inventory";

    /// <summary>The query parameter naming which rule-set version to render — matches
    /// <c>web/src/api/rulesInventory.ts</c>'s own <c>VersionParameter</c>. Omitted, the most recent
    /// version in the selected repository is served.</summary>
    public const string VersionParameter = "version";

    /// <summary>FR-39's Monitor comparison route (S-35, issue #43), first served for real here —
    /// piece 4. Matches <c>web/src/api/monitor.ts</c>'s own <c>MonitorComparisonRoute</c>.</summary>
    public const string MonitorComparisonRoute = "/api/monitor-comparison";

    /// <summary>The two query parameters naming which adjacent rule-set-version hashes to compare —
    /// bare hashes, disambiguated within the selected repository only, the same convention
    /// <see cref="VersionParameter"/> already established for <see cref="RulesInventoryRoute"/>.
    /// Matches <c>web/src/api/monitor.ts</c>'s own <c>fetchMonitorComparison</c> query string.</summary>
    public const string BeforeParameter = "before";

    public const string AfterParameter = "after";

    /// <summary>The route template <see cref="Build"/> registers for FR-21's session endpoint
    /// (S-08). Use <see cref="SessionRoute"/> to build the concrete request path for one session.</summary>
    public const string SessionRouteTemplate = "/api/sessions/{sessionId}";

    /// <summary>The route template <see cref="Build"/> registers for FR-21 part 2 of 3's step
    /// evidence endpoint (S-52, issue #16) — the inspector's Thinking and Raw tabs. Use
    /// <see cref="StepEvidenceRoute"/> to build the concrete request path for one step.</summary>
    public const string StepEvidenceRouteTemplate = "/api/sessions/{sessionId}/steps/{stepId}";

    /// <summary>The Settings surface's read-only route (Part A): the operator's currently-resolved
    /// configuration, real facts only — see <see cref="SettingsEnvelope"/>.</summary>
    public const string SettingsRoute = "/api/settings";

    /// <summary>
    /// The Settings surface's first write route (Part B), and this codebase's first POST endpoint
    /// anywhere — see the "The first write endpoint: a shared write gate, and the threat model
    /// this host assumes" non-obvious decision in <c>Api/CLAUDE.md</c> for the concurrency guard and
    /// the security posture this and <see cref="RebuildRoute"/> share.
    /// </summary>
    public const string IngestRoute = "/api/ingest";

    /// <summary>The Settings surface's second write route (Part B) — see <see cref="IngestRoute"/>'s
    /// own remarks for the shared write gate both routes are served behind.</summary>
    public const string RebuildRoute = "/api/rebuild";

    /// <summary>
    /// The Settings surface's third write route (Part B), and the only destructive endpoint this
    /// codebase serves: it deletes the operator's whole store. Served behind the same write gate and
    /// the same Origin/Host guard the other two are, <em>plus</em> a required confirmation header
    /// (<see cref="ConfirmationHeader"/>) — see the "A destructive route needs a gate that proves
    /// intent, not only provenance" non-obvious decision in <c>Api/CLAUDE.md</c>.
    /// </summary>
    public const string PurgeRoute = "/api/purge";

    /// <summary>The header a destructive request must carry, naming the action it intends. A custom
    /// header cannot ride on a CORS <em>simple</em> request — the exact request shape that made
    /// <see cref="RebuildRoute"/> reachable cross-origin before <see cref="IsAllowedWriteOrigin"/>
    /// existed — so requiring one is a browser-enforced guard, not only a convention.</summary>
    public const string ConfirmationHeader = "X-AecoPostMortem-Confirm";

    /// <summary>The one value <see cref="ConfirmationHeader"/> may carry on <see cref="PurgeRoute"/>.
    /// Named for the action rather than a generic "yes", so a header copied onto a different
    /// destructive route later cannot authorise it by accident.</summary>
    public const string PurgeConfirmation = "purge";

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
        string? webRootPath = null) =>
        Build(store, copilotSessionStateRoot, port, webRootPath, new SemaphoreSlim(1, 1));

    /// <summary>
    /// The same host, with the write gate supplied rather than created here — the seam that makes
    /// "every write route is served behind the one shared gate" a testable claim instead of a
    /// structural one nothing checks. A test holds the gate it passes in and asserts each write route
    /// answers 409 without its command running (<c>PurgeRouteTests.
    /// Every_write_route_including_purge_is_served_behind_the_one_shared_gate</c>); the public
    /// overload above is unchanged for every real caller, which still gets one fresh gate per host.
    /// </summary>
    internal static WebApplication Build(
        LocalStore store,
        string copilotSessionStateRoot,
        int port,
        string? webRootPath,
        SemaphoreSlim writeGate)
    {
        ArgumentNullException.ThrowIfNull(writeGate);

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

        // `writeGate` is the single gate all three write routes are served behind — see the "The
        // first write endpoint: a shared write gate, and the threat model this host assumes"
        // non-obvious decision in Api/CLAUDE.md. One SemaphoreSlim(1, 1) per host instance (not a
        // process-wide static): a test that builds two hosts against two different stores must never
        // have one host's run block the other's. It is a parameter rather than a local only so a test
        // can hold it and prove every write route consults it — see this overload's own remarks.

        app.MapGet(AppStateRoute, () => Results.Ok(DiagnoseAppState(store, copilotSessionStateRoot)));

        app.MapGet(SettingsRoute, () => Results.Ok(GetSettings(store, copilotSessionStateRoot)));

        app.MapPost(IngestRoute, (HttpContext context) =>
            IsAllowedWriteOrigin(context)
                ? RunGated(writeGate, () => RunIngest(store, copilotSessionStateRoot))
                : RefusedOrigin());

        app.MapPost(RebuildRoute, (HttpContext context) =>
            IsAllowedWriteOrigin(context)
                ? RunGated(writeGate, () => RunRebuild(store))
                : RefusedOrigin());

        app.MapPost(PurgeRoute, (HttpContext context) =>
            IsAllowedWriteOrigin(context)
                ? IsConfirmed(context, PurgeConfirmation)
                    ? RunGated(writeGate, () => RunPurge(store))
                    : RefusedUnconfirmed()
                : RefusedOrigin());

        app.MapGet(DigestRoute, (DateOnly? from, DateOnly? to) =>
        {
            // GetDigest is the one place this validates — no separate pre-check here that could
            // silently drift from what the method itself enforces (or stop firing if GetDigest's own
            // rule ever changes without this route being touched).
            try
            {
                return Results.Ok(GetDigest(store, from, to));
            }
            catch (ArgumentException ex) when (ex is not ArgumentNullException)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapGet(RulesInventoryRoute, (string? version) =>
        {
            var envelope = GetRulesInventory(store, version);
            return envelope is null ? Results.NotFound() : Results.Ok(envelope);
        });

        app.MapGet(MonitorComparisonRoute, (string? before, string? after) =>
        {
            if (before is null || after is null)
            {
                return Results.BadRequest(
                    $"Both '{BeforeParameter}' and '{AfterParameter}' query parameters are required.");
            }

            var envelope = GetMonitorComparison(store, before, after);
            return envelope is null ? Results.NotFound() : Results.Ok(envelope);
        });

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

    /// <summary>
    /// The Settings surface's read-only half (Part A): every field here is a real, already-resolved
    /// fact — the same <see cref="LocalStore"/>/<see cref="ExclusionListSource"/> calls
    /// <see cref="RunIngest"/> and the CLI's own <c>ingest</c> command already make, never a second,
    /// looser guess at the same configuration. The exclusion list is loaded from beside the store
    /// actually being served (<see cref="LocalStore.Folder"/>), not
    /// <see cref="ExclusionListSource.DefaultPath"/>, the same isolation
    /// <c>AecoPostMortem.Cli.CommandRunner.Ingest</c>'s own remarks document for a test store.
    /// </summary>
    public static SettingsEnvelope GetSettings(LocalStore store, string copilotSessionStateRoot)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(copilotSessionStateRoot);

        var copilotSourceFound = SessionDiscovery.Discover(copilotSessionStateRoot).RootFound;
        var excludedRoots = ExclusionListSource.Load(
            Path.Combine(store.Folder, ExclusionListSource.FileName));

        return new SettingsEnvelope(
            store.FilePath,
            store.Exists,
            store.SizeInBytes,
            copilotSessionStateRoot,
            copilotSourceFound,
            excludedRoots);
    }

    /// <summary>
    /// The Settings surface's first write action (Part B): the identical call the CLI's own
    /// <c>ingest</c> command makes (<c>AecoPostMortem.Cli.CommandRunner.Ingest</c>) — the same
    /// exclusion-list resolution, the same <see cref="IngestionRun.Run"/> entry point, populating RAW
    /// and the derived layer together exactly as a terminal-driven ingest would. There is no request
    /// body: the source directory served here is always <paramref name="copilotSessionStateRoot"/>,
    /// the one <see cref="Build"/> was given (and <see cref="GetSettings"/> already reports) — this
    /// surface has no path override the way the CLI's optional positional argument does, since an
    /// operator driving the browser has no terminal to type one into.
    /// </summary>
    public static IngestResultEnvelope RunIngest(LocalStore store, string copilotSessionStateRoot)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(copilotSessionStateRoot);

        var excludedRoots = ExclusionListSource.Load(
            Path.Combine(store.Folder, ExclusionListSource.FileName));

        using var context = store.Open();

        var stopwatch = Stopwatch.StartNew();
        var report = IngestionRun.Run(context, copilotSessionStateRoot, excludedRoots);
        stopwatch.Stop();

        return IngestResultEnvelope.From(report, stopwatch.Elapsed);
    }

    /// <summary>
    /// The Settings surface's second write action (Part B): the identical sequence the CLI's own
    /// <c>rebuild</c> command runs, through the one shared definition
    /// <see cref="NormalizedLayerWriter.RebuildAll"/> gives both callers — see the "The API calls the
    /// CLI's own underlying calls, not the CLI itself" non-obvious decision in <c>Api/CLAUDE.md</c>.
    /// </summary>
    public static RebuildResultEnvelope RunRebuild(LocalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        using var context = store.Open();

        var rawEventCount = context.RawEvents.Count();

        var stopwatch = Stopwatch.StartNew();
        var sessionIds = NormalizedLayerWriter.RebuildAll(context);
        stopwatch.Stop();

        return new RebuildResultEnvelope(rawEventCount, sessionIds.Count, stopwatch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// The Settings surface's third write action (Part B), and the only destructive one: the
    /// identical call the CLI's own <c>purge</c> command makes
    /// (<c>AecoPostMortem.Cli.CommandRunner.Purge</c> — a <see cref="LocalStore.Purge"/> call plus
    /// stdout formatting, so there is no shared sequence to factor out the way
    /// <see cref="NormalizedLayerWriter.RebuildAll"/> was). The store is deleted, not emptied: the
    /// next request that opens it recreates it from migrations, empty — see the "What an operator
    /// sees immediately after a purge" non-obvious decision in <c>Api/CLAUDE.md</c>.
    ///
    /// This method itself carries no confirmation check: the gate belongs at the route
    /// (<see cref="IsConfirmed"/>), where the request's own headers are, and a direct caller of this
    /// method (the CLI, a test) has already stated its intent by calling it.
    /// </summary>
    public static PurgeResultEnvelope RunPurge(LocalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var outcome = store.Purge();

        return new PurgeResultEnvelope(
            outcome.DeletedAnything,
            outcome.Deleted,
            outcome.BytesReclaimed);
    }

    /// <summary>
    /// Origin/Host validation for every write route — the two safe ones (<see cref="IngestRoute"/>,
    /// <see cref="RebuildRoute"/>) and, since Part C, the destructive one
    /// (<see cref="PurgeRoute"/>, which adds <see cref="IsConfirmed"/> on top of this rather than
    /// instead of it) (code review, follow-up round — see the
    /// "Origin and Host validation close a live simple-request CSRF path" non-obvious decision in
    /// <c>Api/CLAUDE.md</c> for the full reasoning and how this was verified against a running host).
    /// A plain cross-origin <c>fetch(..., { method: 'POST' })</c> with a <c>text/plain</c> (or no)
    /// body is a CORS <em>simple request</em> — no preflight, so nothing before this check ever
    /// stopped the write from actually running. Two checks, both permissive toward a non-browser
    /// caller (curl, the CLI, this suite's own <c>HttpClient</c>), since neither header is one a
    /// non-browser client is required to send:
    ///
    /// 1. <c>Origin</c>, when present, must equal this host's own origin exactly. A browser adds
    ///    <c>Origin</c> to every cross-origin request and to most same-origin state-changing
    ///    requests too (the Fetch spec adds it for any non-GET/HEAD request) — this is the
    ///    load-bearing check, since a real attacker page's own <c>Origin</c> can never be spoofed to
    ///    read as this host's origin. Absent entirely (no browser involved), the request proceeds to
    ///    the <c>Host</c> check below rather than being refused for a header non-browser callers never
    ///    send.
    /// 2. <c>Host</c> must resolve to this same connection's own real, actually-bound loopback
    ///    address and port (<see cref="Microsoft.AspNetCore.Http.ConnectionInfo.LocalPort"/> — read
    ///    per request, not the <c>port</c> parameter <see cref="Build"/> was originally given, which
    ///    is <c>0</c> for every test using an OS-assigned ephemeral port). This is what actually
    ///    closes DNS rebinding: a rebound page's own browser-set <c>Origin</c> still names its real
    ///    origin (its hostname, not the IP the DNS answer was switched to), which check 1 already
    ///    refuses — but the same attack could otherwise send <c>Host: evil.example:&lt;port&gt;</c>
    ///    while physically connecting to <c>127.0.0.1</c>, and validating <c>Host</c> too closes that
    ///    path independently of whatever <c>Origin</c> claims.
    ///
    /// Refusing either check answers <see cref="StatusCodes.Status403Forbidden"/> via
    /// <see cref="RefusedOrigin"/>, never reaching <see cref="RunGated{T}"/> — the command underneath
    /// it never runs.
    /// </summary>
    internal static bool IsAllowedWriteOrigin(HttpContext context)
    {
        var expectedAuthority = $"127.0.0.1:{context.Connection.LocalPort.ToString(CultureInfo.InvariantCulture)}";

        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) &&
            !string.Equals(origin, $"http://{expectedAuthority}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = context.Request.Headers.Host.ToString();
        return string.Equals(host, expectedAuthority, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The refusal <see cref="IsAllowedWriteOrigin"/>'s own callers answer with — a stated
    /// reason (<c>Results.Problem</c>, the same shape a caught exception in
    /// <see cref="RunGated{T}"/> already uses), not a bare, unexplained 403.</summary>
    static IResult RefusedOrigin() =>
        Results.Problem(
            detail: "This request's Origin or Host did not match this local server; refused.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// The second, stricter gate <see cref="PurgeRoute"/> is served behind, and the only route that
    /// has one — see the "A destructive route needs a gate that proves intent, not only provenance"
    /// non-obvious decision in <c>Api/CLAUDE.md</c>. <see cref="IsAllowedWriteOrigin"/> proves a
    /// request came from this host's own served page; it cannot prove the operator meant to destroy
    /// their store. A custom header can only be set by a caller that deliberately set it — a
    /// cross-origin <c>fetch</c> carrying one is no longer a CORS <em>simple</em> request, so the
    /// browser preflights it and this host, which answers no CORS policy at all, fails that
    /// preflight before the real request is ever sent.
    ///
    /// The value must name the action (<see cref="PurgeConfirmation"/>), so a header a future
    /// destructive route copies cannot authorise the wrong one. Compared ordinally and
    /// case-sensitively: this is a machine-to-machine token from this app's own client, not operator
    /// input to be forgiving about (the operator's own typed confirmation is a separate,
    /// client-side gate — <c>web/CLAUDE.md</c>).
    /// </summary>
    internal static bool IsConfirmed(HttpContext context, string expectedAction) =>
        string.Equals(
            context.Request.Headers[ConfirmationHeader].ToString(),
            expectedAction,
            StringComparison.Ordinal);

    /// <summary>The refusal <see cref="IsConfirmed"/>'s own caller answers with — a stated reason
    /// naming the missing header, the same <c>Results.Problem</c> shape <see cref="RefusedOrigin"/>
    /// already uses.</summary>
    static IResult RefusedUnconfirmed() =>
        Results.Problem(
            detail:
                $"This request destroys data and must carry the header '{ConfirmationHeader}: " +
                $"{PurgeConfirmation}'; refused.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// The shared guard both <see cref="IngestRoute"/> and <see cref="RebuildRoute"/> are served
    /// behind: <paramref name="gate"/>.Wait(0) never blocks the request thread — it either acquires
    /// the gate immediately or reports the conflict immediately, so a second click (or a second tab)
    /// gets an honest, instant "already running" rather than a request silently queued behind the
    /// first. A caught exception is reported as a real failure (<c>Results.Problem</c>, its own
    /// message on the wire) rather than surfacing as a bare, unexplained 500 — "a failed ingest must
    /// show what failed" (the brief's own Scenario 2).
    /// </summary>
    internal static IResult RunGated<T>(SemaphoreSlim gate, Func<T> run)
    {
        if (!gate.Wait(0))
        {
            return Results.Conflict(new { message = "An ingest or rebuild is already running." });
        }

        try
        {
            return Results.Ok(run());
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// FR-41's real orchestration (S-36, issue #44): assembles a live <see cref="ProcessDigest"/> from
    /// the store — six of the seven waste/missing-capability check orchestrators
    /// (<see cref="RepeatedFileReadFindingCheck"/>, <see cref="FailedToolCallsFinding"/>,
    /// <see cref="AbortedTurnFinding"/>, <see cref="HookFailureFinding"/>,
    /// <see cref="InterruptionLoadFinding"/>, <see cref="PhaseChurnFinding"/>) plus
    /// <see cref="Findings.MastheadCounters"/> and a resolved <see cref="Findings.RepositoryScope"/>.
    /// <see cref="ToolFailureClusterFinding"/> is not run here — it needs a mandating rule
    /// (<c>Findings/CLAUDE.md</c>), which real rule extraction at scale (S-20) does not populate yet.
    ///
    /// Piece 3's second slice adds a seventh: <see cref="Findings.BannedToolFinding"/>, the first
    /// real adherence-class (<see cref="Findings.FindingClass.RuleAdherenceToolChoice"/>) check this
    /// surface runs. It needs the same two corpora <see cref="GetRulesInventory"/> already builds —
    /// <see cref="RuleShapeCatalogue.MatchAll"/> over this repository's own rule statements
    /// (<see cref="SessionRuleSetLookup"/>) and a real <see cref="ToolInvocationShape"/> corpus
    /// (<see cref="ToolInvocationShapeLookup"/>) — scoped to the selected repository's own sessions
    /// here, unlike <see cref="GetRulesInventory"/>'s corpus-wide scope, matching every other check
    /// this method already runs.
    ///
    /// Every table this reads is queried once, corpus-wide, and held in memory rather than filtered by
    /// repository in SQL — a measured 126 ms per million rows (S-36's own edge case,
    /// `docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md`) is well
    /// inside budget for this corpus' actual scale (a measured 56,138 RAW rows, PRD §3.1).
    /// <see cref="Findings.MastheadCounters"/> is built from every session regardless of repository —
    /// it is a fact about the whole corpus, not the repository currently selected for ranking — while
    /// every check runs only over the selected repository's own sessions, per
    /// <see cref="Findings.RepositoryScope"/>'s own contract ("the caller has already filtered
    /// findings to one repository before calling it").
    ///
    /// <see cref="Findings.MastheadCounters.IngestInProgress"/> is always <see langword="false"/>:
    /// <c>ingest</c> is a synchronous CLI command that runs to completion before this process ever
    /// opens the store, so there is no live "still ingesting" signal for a separate <c>serve</c>
    /// process to read.
    /// </summary>
    public static DigestEnvelope GetDigest(LocalStore store, DateOnly? from = null, DateOnly? to = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (from is not null && to is not null && from > to)
        {
            throw new ArgumentException(
                $"'{nameof(from)}' ({from}) must not be after '{nameof(to)}' ({to}).", nameof(from));
        }

        using var context = store.Open();

        var sessions = context.Sessions.ToList();
        var rawEvents = context.RawEvents.ToList();
        var toolCalls = context.ToolCalls.ToList();
        var turns = context.Turns.ToList();
        var permissions = context.Permissions.ToList();
        var agents = context.Agents.ToList();

        var repositoryScope = BuildRepositoryScope(sessions);
        // repositoryScope.SessionIds is already exactly this scope's session ids (BuildRepositoryScope
        // computes both from the same selected-repository filter) — reusing it here, rather than
        // re-filtering sessions a second time, is what guarantees the served session-strip positions
        // and the sessions every check below runs over can never disagree.
        var repositorySessionIds = repositoryScope.SessionIds.ToHashSet(StringComparer.Ordinal);

        // The date-range filter narrows the repository scope further — see the "A date-range filter
        // re-scopes the whole analysis" non-obvious decision in Api/CLAUDE.md. When neither bound is
        // supplied this is exactly repositorySessionIds, unchanged from before this filter existed.
        var scopedSessionIds = from is null && to is null
            ? repositorySessionIds
            : sessions
                .Where(session => repositorySessionIds.Contains(session.SessionId))
                .Where(session => IsWithinDateRange(ParseTimestampAsUtc(session.StartedAt), from, to))
                .Select(session => session.SessionId)
                .ToHashSet(StringComparer.Ordinal);
        var scopedSessions = sessions.Where(session => scopedSessionIds.Contains(session.SessionId)).ToList();

        var scopedToolCalls = toolCalls.Where(call => scopedSessionIds.Contains(call.SessionId)).ToList();
        var scopedTurns = turns.Where(turn => scopedSessionIds.Contains(turn.SessionId)).ToList();
        var scopedPermissions = permissions.Where(p => scopedSessionIds.Contains(p.SessionId)).ToList();
        var scopedRawEvents = rawEvents.Where(e => scopedSessionIds.Contains(e.SessionId)).ToList();
        var scopedAgents = agents.Where(a => scopedSessionIds.Contains(a.SessionId)).ToList();

        var (findings, checkRegistry) = BuildFindingsForScope(
            scopedSessions, scopedRawEvents, scopedToolCalls, scopedTurns, scopedPermissions, scopedAgents,
            scopedSessionIds);

        // Rule coverage stays scoped to the repository selection alone, the same "corpus-wide fact,
        // not a ranking-scope lens" treatment MastheadCounters below already gets — a date filter
        // narrows which sessions the findings ranking runs over, not which rule-set version's
        // coverage figure is shown.
        var ruleCoverage = BuildRuleCoverageStatus(sessions, rawEvents, toolCalls, agents, repositoryScope);

        // The served RepositoryScope.SessionIds follows the date filter: its own contract
        // (Findings.RepositoryScope.SessionIds) is "the same session set every check ran over", which
        // must stay true whether or not a date filter narrowed that set further.
        var servedRepositoryScope = scopedSessionIds.SetEquals(repositorySessionIds)
            ? repositoryScope
            : repositoryScope with { SessionIds = OrderedSessionIds(scopedSessions) };

        var digest = ProcessDigest.Build(
            BuildMastheadCounters(sessions, rawEvents, toolCalls, agents), checkRegistry, findings,
            servedRepositoryScope, ruleCoverage);

        // Digest session-naming, Slice 2: a session's own display label, resolved over the identical
        // scopedRawEvents already grouped by session for HookFailureEventLookup/DeclaredIntentLookup
        // above — no new store read.
        var sessionLabels = scopedRawEvents
            .GroupBy(e => e.SessionId, StringComparer.Ordinal)
            .Select(group => (SessionId: group.Key, Label: SessionLabelLookup.Find(group.Key, group.ToList())))
            .Where(pair => pair.Label is not null)
            .ToDictionary(pair => pair.SessionId, pair => pair.Label!, StringComparer.Ordinal);

        return DigestEnvelope.From(digest, FindingEnvelope.From, sessionLabels);
    }

    /// <summary>
    /// Mockup parity item #15: the Digest masthead's own rule-coverage figure. Two candidate scopes
    /// were considered — (a) the selected repository's own most recent rule-set version
    /// (<see cref="RulesInventory.MostRecentVersion"/>, the exact default <see cref="GetRulesInventory"/>
    /// already opens on), or (b) something scoped differently. (a) was chosen: the Digest is already
    /// repository-scoped for every ranked finding (<see cref="Findings.RepositoryScope"/>'s own
    /// remarks), and mirroring the Rules Inventory's own default keeps this a corpus-wide,
    /// deterministic figure with no new selection UI needed — the same "one served figure, never
    /// recounted differently on a second surface" discipline <c>RulesInventoryEnvelope.cs</c>'s own
    /// remarks state, now extended across two endpoints instead of one. Reuses
    /// <see cref="BuildRulesInventoryInputs"/>, the identical <see cref="SessionRuleSetLookup"/>/
    /// <see cref="ToolInvocationShapeLookup"/>/<see cref="RuleShapeCatalogue.MatchAll"/>/
    /// <see cref="RulesInventoryClassifier"/> pipeline <see cref="GetRulesInventory"/> runs, corpus-wide
    /// — not the repository-scoped corpus <see cref="BuildFindingsForScope"/>'s own piece-3 checks use
    /// — so this figure and <c>/api/rules-inventory</c>'s own served counts, for the same version, can
    /// never disagree. <see cref="RuleCoverageStatus.NotYetAnalyzed"/> when there is no version to
    /// select at all — an empty store, or no session in the selected repository ever carrying a rule
    /// set — the same "there is no version to select" case <see cref="GetRulesInventory"/> answers 404
    /// for.
    /// </summary>
    static RuleCoverageStatus BuildRuleCoverageStatus(
        IReadOnlyList<Session> sessions,
        IReadOnlyList<RawEvent> rawEvents,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents,
        RepositoryScope repositoryScope)
    {
        var inputs = BuildRulesInventoryInputs(sessions, rawEvents, toolCalls, agents);
        var selectedVersion = RulesInventory.MostRecentVersion(
            inputs.RuleSets, repositoryScope.SelectedRepository);

        if (selectedVersion is null)
        {
            return RuleCoverageStatus.NotYetAnalyzed;
        }

        try
        {
            var inventory = RulesInventory.Build(inputs.RuleSets, selectedVersion, inputs.Classify);
            return RuleCoverageStatus.Analyzed(inventory.StatusCounts);
        }
        catch (UnknownRuleSetVersionException)
        {
            return RuleCoverageStatus.NotYetAnalyzed;
        }
    }

    /// <summary>
    /// Mockup parity item #4's shared orchestration: the identical ten check orchestrators
    /// <see cref="GetDigest"/> already ran inline (<see cref="RepeatedFileReadFindingCheck"/>,
    /// <see cref="FailedToolCallsFinding"/>, <see cref="AbortedTurnFinding"/>,
    /// <see cref="HookFailureFinding"/>, <see cref="InterruptionLoadFinding"/>,
    /// <see cref="PhaseChurnFinding"/>, <see cref="BannedToolFinding"/>,
    /// <see cref="NeverReadPathFinding"/>, <see cref="UseAAfterBFinding"/>,
    /// <see cref="AlwaysPassParamFinding"/>), factored out so <see cref="GetSession"/> can build the
    /// identical set for one session's own repository rather than duplicating the call sequence.
    /// Every parameter is already scoped by the caller — this method filters nothing itself, the same
    /// "already-resolved plain input" discipline every check orchestrator's own <c>Findings/CLAUDE.md</c>
    /// entry documents.
    /// </summary>
    static (List<Finding> Findings, CheckRegistry CheckRegistry) BuildFindingsForScope(
        IReadOnlyList<Session> scopedSessions,
        IReadOnlyList<RawEvent> scopedRawEvents,
        IReadOnlyList<ToolCall> scopedToolCalls,
        IReadOnlyList<Turn> scopedTurns,
        IReadOnlyList<Permission> scopedPermissions,
        IReadOnlyList<Agent> scopedAgents,
        IReadOnlySet<string> scopedSessionIds)
    {
        var sessionsWithToolCall = scopedToolCalls.Select(call => call.SessionId).ToHashSet(StringComparer.Ordinal);

        var hookFailures = scopedRawEvents
            .GroupBy(e => e.SessionId, StringComparer.Ordinal)
            .SelectMany(group => HookFailureEventLookup.Find(group.Key, group.ToList()))
            .ToList();

        var declaredIntents = scopedRawEvents
            .GroupBy(e => e.SessionId, StringComparer.Ordinal)
            .SelectMany(group => DeclaredIntentLookup.Find(group.Key, group.ToList()))
            .ToList();

        var repeatedReads = RepeatedFileReadFindingCheck.Run(scopedToolCalls);
        var failedCalls = FailedToolCallsFinding.Run(ToToolCallOutcomes(scopedToolCalls));
        var aborted = AbortedTurnFinding.Build(scopedTurns);
        var hookFailureResult = HookFailureFinding.Build(scopedSessionIds.ToList(), sessionsWithToolCall, hookFailures);
        var interruption = InterruptionLoadFinding.Run(scopedPermissions, scopedToolCalls);
        var phaseChurn = PhaseChurnFinding.Run(declaredIntents);

        var ruleShapeMatches = RuleShapeCatalogue.MatchAll(
            SessionRuleSetLookup.BuildAll(scopedSessions, scopedRawEvents)
                .SelectMany(set => set.Blocks)
                .SelectMany(block => block.Statements)
                .Distinct()
                .ToList()).Matches;
        // Computed once and shared: ToolInvocationShapeLookup and ParamCarryingCallLookup both need
        // every scoped call's own RAW arguments, and would otherwise each parse the identical
        // tool.execution_start payloads a second time.
        var scopedArgumentsByCall = RawToolArguments.ByCall(scopedRawEvents);
        var invocations = ToolInvocationShapeLookup.BuildAll(scopedToolCalls, scopedAgents, scopedArgumentsByCall);
        var bannedTool = BannedToolFinding.Run(ruleShapeMatches, invocations, scopedToolCalls);
        var neverReadPath = NeverReadPathFinding.Run(ruleShapeMatches, scopedToolCalls);
        var useAAfterB = UseAAfterBFinding.Run(ruleShapeMatches, invocations, scopedToolCalls);
        var paramCarryingCalls = ParamCarryingCallLookup.BuildAll(scopedToolCalls, scopedAgents, scopedArgumentsByCall);
        var alwaysPassParam = AlwaysPassParamFinding.Run(ruleShapeMatches, paramCarryingCalls);

        var findings = repeatedReads.Findings
            .Concat(failedCalls.Findings)
            .Concat(aborted.Findings)
            .Concat(hookFailureResult.Findings)
            .Concat(interruption.Findings)
            .Concat(phaseChurn.Findings)
            .Concat(bannedTool.Findings)
            .Concat(neverReadPath.Findings)
            .Concat(useAAfterB.Findings)
            .Concat(alwaysPassParam.Findings)
            .ToList();

        var checkRegistry = new CheckRegistry
        {
            Entries =
            [
                repeatedReads.RegistryEntry,
                failedCalls.RegistryEntry,
                aborted.Registry,
                hookFailureResult.Registry,
                interruption.RegistryEntry,
                phaseChurn.RegistryEntry,
                bannedTool.RegistryEntry,
                neverReadPath.RegistryEntry,
                useAAfterB.RegistryEntry,
                alwaysPassParam.RegistryEntry,
            ],
            // The same scope every check above actually ran over — see CheckRegistry.SessionsInScope's
            // own remarks for why SilentCheckEnvelope.From needs this distinct from any one entry's own
            // Population.
            SessionsInScope = scopedSessionIds.Count,
        };

        return (findings, checkRegistry);
    }

    /// <summary>The plain operand <see cref="FailedToolCallsCheck"/> takes: a call with no recorded
    /// completion (<see cref="ToolCall.Success"/> is <see langword="null"/>) is excluded rather than
    /// guessed at, the same "null means not completed, never completed-outcome-unknown" reading the
    /// entity's own remarks give. <see cref="ToolCallOutcome.ToolIdentity"/> is <see cref="ToolCall.ToolName"/>
    /// verbatim — <see cref="FailedToolCallsCheck.Run"/> groups by it with exact, ordinal equality
    /// only, the same convention <see cref="ToolFailureClusterFinding"/>'s own remarks document.</summary>
    static List<ToolCallOutcome> ToToolCallOutcomes(IReadOnlyList<ToolCall> toolCalls) =>
        toolCalls
            .Where(call => call.Success is not null)
            .Select(call => new ToolCallOutcome
            {
                SessionId = call.SessionId,
                ToolIdentity = call.ToolName,
                Succeeded = call.Success!.Value,
            })
            .ToList();

    /// <summary>FR-41's corpus-scope figures (S-36's edge case), built over the whole corpus
    /// regardless of which repository is currently selected — see <see cref="GetDigest"/>'s own
    /// remarks for why. <see cref="MastheadCounters.IngestInProgress"/> is always
    /// <see langword="false"/> for the same reason stated there.</summary>
    static MastheadCounters BuildMastheadCounters(
        IReadOnlyList<Session> sessions,
        IReadOnlyList<RawEvent> rawEvents,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents) =>
        new()
        {
            SessionCount = sessions.Count,
            SpanStart = sessions.Count == 0
                ? null
                : sessions.Select(session => ParseTimestamp(session.StartedAt)).Min(),
            SpanEnd = sessions.Count == 0
                ? null
                : sessions.Select(session => ParseTimestamp(session.EndedAt ?? session.StartedAt)).Max(),
            RepositoryCount = sessions
                .Select(session => session.Repository)
                .Where(repository => repository is not null)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            EventCount = rawEvents.Count,
            ToolCallCount = toolCalls.Count,
            // Mockup parity item #8: every Agent row in the corpus, regardless of repository — the
            // same corpus-wide-then-filter shape GetDigest already established for the other five
            // counters, and this method already receives `agents` corpus-wide (GetDigest reads it
            // once, before narrowing to `scopedAgents` for the check orchestrators below), so no new
            // read was needed to answer this.
            SubagentCount = agents.Count,
            IngestInProgress = false,
        };

    /// <summary>The same culture-invariant, round-trip parse <c>Findings.SessionRecording.ParseTimestamp</c>
    /// already established for a stored RAW/derived timestamp string — the unqualified
    /// <c>DateTimeOffset.Parse</c> overload reads <see cref="CultureInfo.CurrentCulture"/>, a real
    /// portability gap on a codebase whose determinism contract (PRD §3.8) cares about a reproducible
    /// result regardless of the machine it runs on.</summary>
    static DateTimeOffset ParseTimestamp(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Code review Minor (both the internal and external review): <see cref="ParseTimestamp"/>'s
    /// own <see cref="DateTimeStyles.RoundtripKind"/> reads an offset-less timestamp as the parsing
    /// machine's own local time — harmless for the masthead span (a display value, formatted
    /// <c>timeZone: 'UTC'</c> client-side regardless of the offset the server attached) but a real
    /// determinism gap here specifically: <see cref="IsWithinDateRange"/> compares against
    /// <see cref="StartOfDayUtc"/>/<see cref="EndOfDayUtc"/>, both fixed UTC instants, so a
    /// local-time misread would shift which side of the boundary a session falls on depending on the
    /// server's own machine timezone (PRD §3.8's determinism contract). Every real timestamp in the
    /// live reference corpus carries an explicit offset (<c>Z</c>), so this is latent today, not
    /// observed — fixed here rather than left for a machine in a non-UTC timezone to hit first. A
    /// dedicated parse rather than changing <see cref="ParseTimestamp"/> itself: that shared method
    /// mirrors <c>Findings.SessionRecording.ParseTimestamp</c>'s own established convention and has
    /// two other call sites (the masthead span) this filter's own correctness does not need to
    /// revisit.</summary>
    internal static DateTimeOffset ParseTimestampAsUtc(string timestamp) =>
        DateTimeOffset.Parse(
            timestamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>The date-range filter's own inclusive-of-the-whole-day comparison — a session
    /// starting at any time on <paramref name="to"/>'s own calendar day is still in range, not
    /// excluded for carrying a time-of-day later than midnight. Both bounds are optional and
    /// independent: a caller may supply only one.</summary>
    static bool IsWithinDateRange(DateTimeOffset startedAt, DateOnly? from, DateOnly? to)
    {
        if (from is not null && startedAt < StartOfDayUtc(from.Value))
        {
            return false;
        }

        if (to is not null && startedAt > EndOfDayUtc(to.Value))
        {
            return false;
        }

        return true;
    }

    static DateTimeOffset StartOfDayUtc(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    static DateTimeOffset EndOfDayUtc(DateOnly date) => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    /// <summary>
    /// FR-41 part 2 (S-54)'s default: the repository carrying the most sessions, ties broken
    /// ordinally for a deterministic pick (PRD §3.8) — PRD Part 8 Q5 decided the digest shows one
    /// repository at a time, selectable, and the measured corpus has one dominant repository (25 of
    /// 35 sessions) for this default to land on in practice. <see langword="null"/> only when no
    /// session in the store carries a repository at all, matching <see cref="RepositoryScope.SelectedRepository"/>'s
    /// own documented meaning for that value.
    ///
    /// <see cref="RepositoryScope.SessionIds"/> is every session in that scope (the selected
    /// repository, or the whole corpus when none is selected), ordered by the session's own real
    /// start time — never by session id text, which is a random UUID in the reference corpus and has
    /// no relationship to arrival order (the same defect PR #112 fixed for rule-set version
    /// ordering) — tie-broken by session id ordinally for a deterministic total order. This is the
    /// same session set every check <see cref="GetDigest"/> runs is scoped to
    /// (<c>scopedSessionIds</c>), computed once here so both a finding's own ranking and the served
    /// session-strip positions agree on exactly which sessions are "in scope".
    /// </summary>
    static RepositoryScope BuildRepositoryScope(IReadOnlyList<Session> sessions)
    {
        var repositories = sessions
            .Select(session => session.Repository)
            .Where(repository => repository is not null)
            .Select(repository => repository!)
            .ToList();

        var selected = repositories
            .GroupBy(repository => repository, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();

        var scopedSessions = selected is null
            ? sessions
            : sessions.Where(session => session.Repository == selected);

        return new RepositoryScope
        {
            SelectedRepository = selected,
            AvailableRepositories = repositories
                .Distinct(StringComparer.Ordinal)
                .OrderBy(repository => repository, StringComparer.Ordinal)
                .ToList(),
            SessionIds = OrderedSessionIds(scopedSessions),
        };
    }

    /// <summary>The chronological session-id ordering both <see cref="BuildRepositoryScope"/> and
    /// <see cref="GetDigest"/>'s own date-filtered <c>servedRepositoryScope</c> use — extracted so the
    /// two call sites cannot silently drift onto two different orderings. By the session's own real
    /// start time, never by session id text (a random UUID in the reference corpus has no
    /// relationship to arrival order — the same defect PR #112 fixed for rule-set version ordering),
    /// tie-broken by session id ordinally for a deterministic total order.</summary>
    static List<string> OrderedSessionIds(IEnumerable<Session> sessions) =>
        sessions
            .OrderBy(session => session.StartedAt, StringComparer.Ordinal)
            .ThenBy(session => session.SessionId, StringComparer.Ordinal)
            .Select(session => session.SessionId)
            .ToList();

    /// <summary>
    /// FR-40's real orchestration (S-22, issue #35): resolves the whole store's <see cref="RawEvent"/>s
    /// into <see cref="SessionRuleSet"/>s (<see cref="SessionRuleSetLookup"/>) and a real
    /// <see cref="ToolInvocationShape"/> corpus (<see cref="ToolInvocationShapeLookup"/>, corpus-wide —
    /// the same "every statement, not only the selected version's" scope <see cref="RuleShapeCatalogue.MatchAll"/>
    /// already uses below), classifies every distinct statement in the corpus once with
    /// <see cref="RulesInventoryClassifier"/> against that corpus, and serves
    /// <see cref="RulesInventory.Build"/>'s result for one version. The selected repository is the
    /// same default <see cref="BuildRepositoryScope"/> gives <see cref="GetDigest"/> — this surface has
    /// no repository selector of its own (<c>web/CLAUDE.md</c>) — so <paramref name="versionHash"/>
    /// only ever needs to disambiguate a version within that one repository, never name one itself.
    /// <see langword="null"/> when there is no version to select at all (an empty store, or a
    /// <paramref name="versionHash"/> no session in the selected repository ever carried) — the same
    /// "reported as 404, not a designed empty state" distinction <see cref="GetSession"/> draws for a
    /// session id the store carries no row for.
    /// </summary>
    public static RulesInventoryEnvelope? GetRulesInventory(LocalStore store, string? versionHash)
    {
        ArgumentNullException.ThrowIfNull(store);

        using var context = store.Open();

        var sessions = context.Sessions.ToList();
        var rawEvents = context.RawEvents.ToList();
        var toolCalls = context.ToolCalls.ToList();
        var agents = context.Agents.ToList();

        var inputs = BuildRulesInventoryInputs(sessions, rawEvents, toolCalls, agents);
        var repositoryScope = BuildRepositoryScope(sessions);

        var selectedVersion = versionHash is null
            ? RulesInventory.MostRecentVersion(inputs.RuleSets, repositoryScope.SelectedRepository)
            : new RuleSetVersionId { Repository = repositoryScope.SelectedRepository, Hash = versionHash };

        if (selectedVersion is null)
        {
            return null;
        }

        // Mockup parity item #7: a Watched row's own violation count. Run the same four piece-3
        // checks GetDigest runs, but over this method's own corpus-wide matches/invocations/toolCalls
        // — the exact inputs RulesInventoryClassifier just resolved Watched status against, never a
        // second, differently (repository-)scoped read (ApiHost/CLAUDE.md's own remarks on why
        // GetRulesInventory stays corpus-wide where GetDigest does not).
        var paramCarryingCalls = ParamCarryingCallLookup.BuildAll(toolCalls, agents, inputs.ArgumentsByCall);
        var violationCounts = BuildViolationCounts(
            inputs.Matching.Matches,
            BannedToolFinding.Run(inputs.Matching.Matches, inputs.Invocations, toolCalls),
            NeverReadPathFinding.Run(inputs.Matching.Matches, toolCalls),
            UseAAfterBFinding.Run(inputs.Matching.Matches, inputs.Invocations, toolCalls),
            AlwaysPassParamFinding.Run(inputs.Matching.Matches, paramCarryingCalls));

        try
        {
            return RulesInventoryEnvelope.From(
                RulesInventory.Build(inputs.RuleSets, selectedVersion, inputs.Classify), violationCounts);
        }
        catch (UnknownRuleSetVersionException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mockup parity item #15's shared pipeline: the identical <see cref="SessionRuleSetLookup"/>/
    /// <see cref="ToolInvocationShapeLookup"/>/<see cref="RuleShapeCatalogue.MatchAll"/>/
    /// <see cref="RulesInventoryClassifier"/> sequence both <see cref="GetRulesInventory"/> and
    /// <see cref="GetDigest"/>'s own rule-coverage figure (<see cref="BuildRuleCoverageStatus"/>) need,
    /// factored out of what used to be <see cref="GetRulesInventory"/>'s own inline sequence — the same
    /// "one served figure, never recounted differently on a second surface" discipline
    /// <c>RulesInventoryEnvelope.cs</c>'s own remarks state for <c>RulesInventoryStatusCountsEnvelope</c>,
    /// now extended so the Digest masthead's bar and the Rules Inventory's own status counts, for the
    /// same rule-set version, can never be computed two different ways. Every parameter is already read
    /// corpus-wide by the caller — this method issues no query of its own.
    /// </summary>
    static RulesInventoryInputs BuildRulesInventoryInputs(
        IReadOnlyList<Session> sessions,
        IReadOnlyList<RawEvent> rawEvents,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents)
    {
        var ruleSets = SessionRuleSetLookup.BuildAll(sessions, rawEvents);
        // Shared once, the same reuse GetDigest already established for ToolInvocationShapeLookup and
        // ParamCarryingCallLookup, so no caller of this method parses the same tool.execution_start
        // payloads twice.
        var argumentsByCall = RawToolArguments.ByCall(rawEvents);
        var invocations = ToolInvocationShapeLookup.BuildAll(toolCalls, agents, argumentsByCall);
        var statements = ruleSets
            .SelectMany(set => set.Blocks)
            .SelectMany(block => block.Statements)
            .Distinct()
            .ToList();
        var matching = RuleShapeCatalogue.MatchAll(statements);
        var classify = RulesInventoryClassifier.BuildClassifier(matching, invocations);

        return new RulesInventoryInputs(ruleSets, argumentsByCall, matching, invocations, classify);
    }

    /// <summary>The plain tuple <see cref="BuildRulesInventoryInputs"/> returns — everything both
    /// <see cref="GetRulesInventory"/> and <see cref="BuildRuleCoverageStatus"/> need from one shared
    /// RAW-parsing and classification pass.</summary>
    sealed record RulesInventoryInputs(
        IReadOnlyList<SessionRuleSet> RuleSets,
        Dictionary<(string SessionId, string ToolCallId), ToolArguments> ArgumentsByCall,
        RuleShapeMatching Matching,
        IReadOnlyList<ToolInvocationShape> Invocations,
        Func<RuleStatement, RuleStatementStatus> Classify);

    /// <summary>
    /// Mockup parity item #7's join: one entry per matched statement whose shape has a real
    /// Finding-producing orchestrator (<see cref="RuleShapeKind.ToolIsBanned"/>,
    /// <see cref="RuleShapeKind.NeverReadPath"/>, <see cref="RuleShapeKind.UseAAfterB"/>,
    /// <see cref="RuleShapeKind.AlwaysPassParam"/>) — a matched <see cref="RuleShapeKind.PreferAOverB"/>
    /// statement (today's one Watchable shape with no orchestrator) gets no entry at all, so a lookup
    /// against this dictionary falls through to the caller's own <see cref="RuleViolationCountEnvelope.NotAvailable"/>
    /// default rather than a fabricated number. Every one of the four Finding classes keys its own
    /// <c>Recurrence.Key</c> to the matched statement's own text (<c>Findings/CLAUDE.md</c>'s remarks on
    /// each), the same identity <paramref name="matches"/> itself carries per <see cref="RuleShapeMatch.Statement"/>
    /// — a match with no corresponding finding still gets a real <c>Counted(0)</c> entry: the check
    /// ran over every matched statement of its own shape and genuinely found nothing, which is a
    /// different fact from the shape having no check at all.
    /// </summary>
    static Dictionary<RuleStatement, RuleViolationCountEnvelope> BuildViolationCounts(
        IReadOnlyList<RuleShapeMatch> matches,
        BannedToolFinding.Result bannedTool,
        NeverReadPathFinding.Result neverReadPath,
        UseAAfterBFinding.Result useAAfterB,
        AlwaysPassParamFinding.Result alwaysPassParam)
    {
        var bannedToolCounts = CountsByRecurrenceKey(bannedTool.Findings, "call_count");
        var neverReadPathCounts = CountsByRecurrenceKey(neverReadPath.Findings, "access_count");
        var useAAfterBCounts = CountsByRecurrenceKey(useAAfterB.Findings, "violation_count");
        var alwaysPassParamCounts = CountsByRecurrenceKey(alwaysPassParam.Findings, "violation_count");

        var counts = new Dictionary<RuleStatement, RuleViolationCountEnvelope>();

        foreach (var match in matches)
        {
            var lookup = match.Kind switch
            {
                RuleShapeKind.ToolIsBanned => bannedToolCounts,
                RuleShapeKind.NeverReadPath => neverReadPathCounts,
                RuleShapeKind.UseAAfterB => useAAfterBCounts,
                RuleShapeKind.AlwaysPassParam => alwaysPassParamCounts,
                _ => null,
            };

            if (lookup is null)
            {
                // RuleShapeKind.PreferAOverB (or any future shape with no orchestrator here yet): no
                // entry at all — the caller's own lookup falls through to NotAvailable.
                continue;
            }

            counts[match.Statement] = RuleViolationCountEnvelope.Counted(
                lookup.GetValueOrDefault(match.Statement.Text, 0));
        }

        return counts;
    }

    /// <summary>One count per <see cref="Finding.Recurrence"/> key, read from the one evidence field
    /// each of the four piece-3 <c>RuleAdherenceToolChoice</c> checks carries its own count in
    /// (<c>Findings/CLAUDE.md</c>'s "carries its count in Evidence, never in Resolution" remarks) — a
    /// last-write-wins overwrite rather than <c>ToDictionary</c>, defensively: two distinct rule
    /// statements (different source files) that happen to share identical text and the same matched
    /// shape would otherwise throw on a duplicate key here, a pre-existing ambiguity in how these four
    /// Finding classes key their own <c>Recurrence</c> (by text alone, not the statement's full
    /// identity) that this method does not attempt to resolve.</summary>
    static Dictionary<string, int> CountsByRecurrenceKey(
        IReadOnlyList<Finding> findings, string evidenceField)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var finding in findings)
        {
            var evidence = finding.Evidence.First(item => item.Field == evidenceField);
            counts[finding.Recurrence.Key] = int.Parse(evidence.Value, CultureInfo.InvariantCulture);
        }

        return counts;
    }

    /// <summary>
    /// FR-39's real orchestration (S-35, issue #43): the not-yet-wired gap
    /// <see cref="Findings.MonitorComparison"/>'s own remarks name. The wire contract carries only
    /// bare version hashes (<paramref name="beforeHash"/>/<paramref name="afterHash"/>) — no
    /// repository, no rule — so this resolves both the same way <see cref="GetRulesInventory"/>
    /// resolves its own <c>version</c> parameter: within <see cref="BuildRepositoryScope"/>'s default
    /// repository only. It picks the first <see cref="RuleShapeKind.PreferAOverB"/> match among the
    /// statements the <paramref name="afterHash"/> version's own carrying sessions carried — the only
    /// shape <see cref="Findings.MonitorComparison.Compare"/> takes two operands for — and scopes a
    /// real <see cref="ToolInvocationShape"/> corpus to each side's own sessions separately, so the
    /// two figures are never built from calls the other side made. <see langword="null"/> (404) when
    /// there is no repository, no such adjacent pair, or no <see cref="RuleShapeKind.PreferAOverB"/>
    /// statement to compare — the same "answers 404, the same as a missing session" precedent
    /// <see cref="GetRulesInventory"/> already documents for a version hash no session carried.
    /// </summary>
    public static MonitorComparisonEnvelope? GetMonitorComparison(
        LocalStore store, string beforeHash, string afterHash)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterHash);

        using var context = store.Open();

        var sessions = context.Sessions.ToList();
        var rawEvents = context.RawEvents.ToList();
        var toolCalls = context.ToolCalls.ToList();
        var agents = context.Agents.ToList();

        var repositoryScope = BuildRepositoryScope(sessions);
        if (repositoryScope.SelectedRepository is null)
        {
            return null;
        }

        var ruleSets = SessionRuleSetLookup.BuildAll(sessions, rawEvents);
        var versions = RuleSetVersioning.Compute(ruleSets);

        var beforeId = new RuleSetVersionId { Repository = repositoryScope.SelectedRepository, Hash = beforeHash };
        var afterId = new RuleSetVersionId { Repository = repositoryScope.SelectedRepository, Hash = afterHash };

        var afterSessionIds = SessionIdsCarrying(ruleSets, afterId);
        var afterStatements = ruleSets
            .Where(set => afterSessionIds.Contains(set.SessionId))
            .SelectMany(set => set.Blocks)
            .SelectMany(block => block.Statements)
            .Distinct()
            .ToList();

        var preferAOverB = RuleShapeCatalogue.MatchAll(afterStatements).Matches
            .FirstOrDefault(match => match.Kind == RuleShapeKind.PreferAOverB);
        if (preferAOverB is null)
        {
            return null;
        }

        var beforeSessionIds = SessionIdsCarrying(ruleSets, beforeId);
        var beforeInvocations = InvocationsFor(beforeSessionIds, toolCalls, agents, rawEvents);
        var afterInvocations = InvocationsFor(afterSessionIds, toolCalls, agents, rawEvents);

        try
        {
            var comparison = Findings.MonitorComparison.Compare(
                versions,
                beforeId,
                afterId,
                preferAOverB.OperandAText,
                preferAOverB.OperandBText!,
                beforeInvocations,
                afterInvocations);

            return MonitorComparisonEnvelope.From(comparison);
        }
        catch (Exception ex) when (ex is UnknownRuleSetVersionException or NonAdjacentRuleSetVersionsException)
        {
            return null;
        }
    }

    /// <summary>The session ids whose own block set hashes to <paramref name="versionId"/>, within its
    /// own repository — the same content-hash match <see cref="RuleSetVersioning.Compute"/> groups by,
    /// exposed here because neither it nor <see cref="RuleSetVersion"/> carries the full member list,
    /// only the window's first and last session.</summary>
    static HashSet<string> SessionIdsCarrying(IReadOnlyList<SessionRuleSet> ruleSets, RuleSetVersionId versionId) =>
        ruleSets
            .Where(set => string.Equals(set.Repository, versionId.Repository, StringComparison.Ordinal))
            .Where(set => string.Equals(
                RuleSetVersionHasher.ComputeHash(set.Blocks), versionId.Hash, StringComparison.Ordinal))
            .Select(set => set.SessionId)
            .ToHashSet(StringComparer.Ordinal);

    static IReadOnlyList<ToolInvocationShape> InvocationsFor(
        IReadOnlySet<string> sessionIds,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents,
        IReadOnlyList<RawEvent> rawEvents) =>
        ToolInvocationShapeLookup.BuildAll(
            toolCalls.Where(call => sessionIds.Contains(call.SessionId)).ToList(),
            agents.Where(agent => sessionIds.Contains(agent.SessionId)).ToList(),
            rawEvents.Where(e => sessionIds.Contains(e.SessionId)).ToList());

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
    ///
    /// FR-21 part 3 of 3 (S-53, issue #17) widens this read by one narrow addition: it also reads
    /// this session's own RAW events and runs them through <c>Ingestion.ExecutionRecordBuilder</c>
    /// purely for its <c>SpawnResolutionCheck</c> diagnostic — never for the
    /// <c>Turn</c>/<c>ToolCall</c>/<c>Agent</c> rows it also returns, which would be exactly the
    /// duplicate reconstruction path the remark above rules out. A session's own RAW events are
    /// bounded by that session, so this second read stays cheap regardless of corpus size.
    ///
    /// Mockup parity item #4 closes the chip row's own gap: <see cref="SessionEnvelope.Findings"/>
    /// used to be built from an empty <see cref="Finding"/> list unconditionally. This method now
    /// reads every session sharing this session's own <see cref="Session.Repository"/> and runs the
    /// identical ten check orchestrators <see cref="GetDigest"/> runs for a whole repository
    /// (<see cref="BuildFindingsForScope"/>), then filters the result down to this one session via
    /// <see cref="Findings.SessionFindings.For"/> — the same "scope by repository, filter to one
    /// session" split <see cref="GetDigest"/> already establishes for a whole repository's own
    /// ranked list, just narrowed one step further here since a session belongs to exactly one
    /// repository, never an explicitly-selected one.
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

        var rawEvents = context.RawEvents.Where(r => r.SessionId == sessionId).ToList();
        var spawnResolution = ExecutionRecordBuilder.Build(sessionId, rawEvents).SpawnResolutionCheck;

        var recording = SessionRecording.Build(session, turns, toolCalls, agents, skills, hooks, spawnResolution);

        // Mockup parity item #4: the same ten check orchestrators GetDigest runs for a whole
        // repository (BuildFindingsForScope), scoped here to this session's own repository — its
        // own Session.Repository field, not an explicitly-selected one, since GetSession has no
        // repository selector of its own. A session with a null Repository scopes to every other
        // session that also carries no repository, the same equality-based grouping
        // BuildRepositoryScope's own fallback uses when no repository is recorded at all.
        var repositorySessions = context.Sessions.Where(s => s.Repository == session.Repository).ToList();
        var repositorySessionIds = repositorySessions.Select(s => s.SessionId).ToHashSet(StringComparer.Ordinal);

        var repositoryRawEvents = context.RawEvents
            .Where(e => repositorySessionIds.Contains(e.SessionId)).ToList();
        var repositoryToolCalls = context.ToolCalls
            .Where(t => repositorySessionIds.Contains(t.SessionId)).ToList();
        var repositoryTurns = context.Turns
            .Where(t => repositorySessionIds.Contains(t.SessionId)).ToList();
        var repositoryPermissions = context.Permissions
            .Where(p => repositorySessionIds.Contains(p.SessionId)).ToList();
        var repositoryAgents = context.Agents
            .Where(a => repositorySessionIds.Contains(a.SessionId)).ToList();

        var (repositoryFindings, _) = BuildFindingsForScope(
            repositorySessions, repositoryRawEvents, repositoryToolCalls, repositoryTurns,
            repositoryPermissions, repositoryAgents, repositorySessionIds);

        var findings = SessionFindings.For(sessionId, repositoryFindings);

        // Mockup parity item #17: which of this session's own tape steps each session-scoped finding
        // is unambiguously about (`Api/CLAUDE.md`'s own remarks on `SessionTapeStepFindingLookup`) —
        // computed from the identical `toolCalls`/`hooks` rows already read above for the tape itself,
        // never a second query.
        var stepFindings = SessionTapeStepFindingLookup.Build(
            findings.Chips.Select(chip => chip.Finding).ToList(), toolCalls, hooks);

        // FR-22 (S-09, issue #18): one lane per subagent, each carrying the report it actually
        // produced — resolved from the same `rawEvents` read above, ordered by `StartedAt` so the
        // served list is deterministic rather than whatever order the store happened to return rows
        // in.
        var lanes = agents
            .OrderBy(agent => agent.StartedAt, StringComparer.Ordinal)
            .ThenBy(agent => agent.AgentId, StringComparer.Ordinal)
            .Select(agent => SessionAgentLaneEnvelope.From(agent, SubagentOutputLookup.Find(rawEvents, agent)))
            .ToList();

        // Mockup parity item #13 ("Prose in transcript"): every prompt step's own readable
        // reasoning, resolved once here rather than waiting for a per-step click
        // (`GetStepEvidence`'s own `StepEvidenceLookup.Find` path stays exactly as it was, still the
        // only source for a step's Raw tab). Bounded by this session's own turn count
        // (`recording.Masthead.TurnCount`, a measured 84 at this project's largest scale, 195 on a
        // real session in the live reference corpus), not the whole tape's step count — the same
        // `rawEvents` this method already reads for `SessionRecording.Build`/`SpawnResolutionCheck`
        // above, no second RAW read.
        var promptStepIds = recording.Tape.Steps
            .Where(step => step.Kind == SessionTapeStepKind.Prompt)
            .Select(step => step.StepId)
            .ToList();
        var thinkingByPromptStepId = StepEvidenceLookup.FindThinkingForPromptSteps(rawEvents, promptStepIds);
        var promptTextByStepId = PromptTextLookup.FindForPromptSteps(rawEvents, promptStepIds);

        // What triggered a hook (this task): resolved the identical "eager, batch, no fetch" way
        // promptTextByStepId is, above — bounded by this session's own hook step ids, from the same
        // rawEvents already read for SessionRecording.Build/SpawnResolutionCheck, no second RAW read.
        var hookStepIds = recording.Tape.Steps
            .Where(step => step.Kind == SessionTapeStepKind.Hook)
            .Select(step => step.StepId)
            .ToList();
        var triggeredByToolNameByStepId = HookTriggerNameLookup.FindForHookSteps(rawEvents, hookStepIds);

        return SessionEnvelope.From(
            recording, findings, FindingEnvelope.From, lanes, stepFindings, thinkingByPromptStepId,
            promptTextByStepId, triggeredByToolNameByStepId);
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
