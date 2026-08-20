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

        app.MapGet(DigestRoute, () => Results.Ok(GetDigest(store)));

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
    public static DigestEnvelope GetDigest(LocalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

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
        var scopedSessionIds = repositoryScope.SessionIds.ToHashSet(StringComparer.Ordinal);
        var scopedSessions = sessions.Where(session => scopedSessionIds.Contains(session.SessionId)).ToList();

        var scopedToolCalls = toolCalls.Where(call => scopedSessionIds.Contains(call.SessionId)).ToList();
        var scopedTurns = turns.Where(turn => scopedSessionIds.Contains(turn.SessionId)).ToList();
        var scopedPermissions = permissions.Where(p => scopedSessionIds.Contains(p.SessionId)).ToList();
        var scopedRawEvents = rawEvents.Where(e => scopedSessionIds.Contains(e.SessionId)).ToList();
        var scopedAgents = agents.Where(a => scopedSessionIds.Contains(a.SessionId)).ToList();

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
        };

        var digest = ProcessDigest.Build(
            BuildMastheadCounters(sessions, rawEvents, toolCalls), checkRegistry, findings, repositoryScope);

        return DigestEnvelope.From(digest, FindingEnvelope.From);
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
        IReadOnlyList<Session> sessions, IReadOnlyList<RawEvent> rawEvents, IReadOnlyList<ToolCall> toolCalls) =>
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
            IngestInProgress = false,
        };

    /// <summary>The same culture-invariant, round-trip parse <c>Findings.SessionRecording.ParseTimestamp</c>
    /// already established for a stored RAW/derived timestamp string — the unqualified
    /// <c>DateTimeOffset.Parse</c> overload reads <see cref="CultureInfo.CurrentCulture"/>, a real
    /// portability gap on a codebase whose determinism contract (PRD §3.8) cares about a reproducible
    /// result regardless of the machine it runs on.</summary>
    static DateTimeOffset ParseTimestamp(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
            SessionIds = scopedSessions
                .OrderBy(session => session.StartedAt, StringComparer.Ordinal)
                .ThenBy(session => session.SessionId, StringComparer.Ordinal)
                .Select(session => session.SessionId)
                .ToList(),
        };
    }

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

        var ruleSets = SessionRuleSetLookup.BuildAll(sessions, rawEvents);
        var invocations = ToolInvocationShapeLookup.BuildAll(toolCalls, agents, rawEvents);
        var repositoryScope = BuildRepositoryScope(sessions);

        var selectedVersion = versionHash is null
            ? RulesInventory.MostRecentVersion(ruleSets, repositoryScope.SelectedRepository)
            : new RuleSetVersionId { Repository = repositoryScope.SelectedRepository, Hash = versionHash };

        if (selectedVersion is null)
        {
            return null;
        }

        var statements = ruleSets
            .SelectMany(set => set.Blocks)
            .SelectMany(block => block.Statements)
            .Distinct()
            .ToList();
        var classify = RulesInventoryClassifier.BuildClassifier(
            RuleShapeCatalogue.MatchAll(statements), invocations);

        try
        {
            return RulesInventoryEnvelope.From(RulesInventory.Build(ruleSets, selectedVersion, classify));
        }
        catch (UnknownRuleSetVersionException)
        {
            return null;
        }
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

        // No check orchestrator runs against the live store yet (FR-21 part 2 of 3, S-52, issue
        // #16) — the same "not yet wired to a live corpus" gap `ProcessDigest`/`DigestEnvelope`
        // document for their own `findings` input (`Findings/CLAUDE.md`, `Api/CLAUDE.md`). An empty
        // list is the honest answer today: `SessionFindings.For` still runs, so the chip row's own
        // designed "no findings" state is real, not a placeholder skipped by short-circuiting here.
        var findings = SessionFindings.For(sessionId, []);

        // FR-22 (S-09, issue #18): one lane per subagent, each carrying the report it actually
        // produced — resolved from the same `rawEvents` read above, ordered by `StartedAt` so the
        // served list is deterministic rather than whatever order the store happened to return rows
        // in.
        var lanes = agents
            .OrderBy(agent => agent.StartedAt, StringComparer.Ordinal)
            .ThenBy(agent => agent.AgentId, StringComparer.Ordinal)
            .Select(agent => SessionAgentLaneEnvelope.From(agent, SubagentOutputLookup.Find(rawEvents, agent)))
            .ToList();

        return SessionEnvelope.From(recording, findings, FindingEnvelope.From, lanes);
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
