# web

The React + TypeScript + Vite app: the digest, the session view, the Rules Inventory and the
Monitor comparison.

**All frontend commands run from here, never from the repository root** (Repo Rule 3, PRD §3.1).
There is no `package.json` at the repository root, and a containment test fails if one appears.
`scripts/build-web.ps1` is the scripted form of the build; it pushes into this directory rather than
passing `--prefix`, for the same reason.

`dotnet test` does not build this project — that would make the .NET suite depend on Node.
`npm test` (`vitest run`) is this project's own test command; nothing on the .NET side calls it.

## Structure

| File | What it holds |
|---|---|
| `src/App.tsx` | the routes (`/`, `/sessions/:sessionId`, `/rules`, `/monitor`) plus a `*` catch-all (`NotFound`), all under `AppShell`. Router-agnostic on purpose — `main.tsx` supplies `BrowserRouter`, tests supply `MemoryRouter`. `/monitor` (FR-39) is the fourth routed surface, added once `MonitorComparisonBlock`/`fetchMonitorComparison` had no door to the real app — see `routes/MonitorPage.tsx`'s own doc comment for why it earns a route rather than a section on the Digest or the Rules Inventory |
| `src/AppShell.tsx` | the nav to the Digest, Rules Inventory and Monitor (always reachable, S-48 Scenario 1 plus FR-39's own extension of it); the session view is reachable only via session id links from the digest, plus `AppStateBanner`, above whichever route's `<Outlet />` content is showing |
| `src/AppStateBanner.tsx` | S-48 Scenarios 2 and 3: fetches `/api/app-state` and renders its diagnosis, distinctly per state — no-source-found, empty-store, and a fourth state (unreachable API) neither Gherkin scenario names but a real machine can hit |
| `src/api/appState.ts` | the `AppStateReport`/`AppStateKind` shapes and `fetchAppState`, hand-kept in sync with `AecoPostMortem.Api.AppStateReport` (`src/AecoPostMortem.Api/AppStateReport.cs`) — no generated client exists yet |
| `src/api/useAppState.ts` | the fetch-once-on-mount hook `AppStateBanner` reads; loading renders nothing rather than a message that might not apply a moment later |
| `src/routes/ComingSoon.tsx` | the placeholder a surface with no real content yet renders — naming its own story and release rather than sharing one generic message. **Currently unreferenced**: all four routed surfaces (Digest, Rules Inventory, session view, Monitor) are built as of the Monitor comparison's missing-door task. Kept because the playbook below still points a new route at it, so the next one does not invent a second placeholder shape |
| `src/routes/RulesInventoryPage.tsx` | FR-40's real content (S-22, issue #35): one rule-set version's statements, each with exactly one status, its source file, its carrying sessions, its in-force window and its retirement — plus the version scope, the status breakdown and the two designed "no rules found" states. Fetches `/api/rules-inventory` via `useRulesInventory`. Mockup parity item #7 added a "Violations" column (`ViolationCountCell`): a Watched row's real count, a stated "No check built" for a Watched row whose matched shape has no orchestrator, or a plain dash for every non-Watched row — three visually distinct states, never one collapsed into another |
| `src/api/rulesInventory.ts` | the `RulesInventoryEnvelope` shapes and `fetchRulesInventory`, hand-kept in sync with `AecoPostMortem.Api.RulesInventoryEnvelope` (`src/AecoPostMortem.Api/RulesInventoryEnvelope.cs`) — the same no-generated-client gap `api/appState.ts` documents. `VersionParameter` is the query parameter naming which version to render. Mockup parity item #7 added `RuleViolationCountEnvelope` (`counted`/`notAvailable`) and `RulesInventoryRowEnvelope.violationCount` |
| `src/api/useRulesInventory.ts` | the fetch-per-`versionHash` hook `RulesInventoryPage` reads, mirroring `useSession`'s re-fetch-on-change shape rather than `useDigest`'s fetch-once |
| `src/routes/DigestPage.tsx` | FR-41's real content (S-36 + S-54, issues #44/#45): the masthead's repository selector plus the ranked findings, each an expandable `FindingRow`. Fetches `/api/digest` via `useDigest`; loading renders nothing, a failed fetch renders its own `role="alert"` message, the same shape `AppStateBanner` established. FR-48 (issue #52, S-42) added the "Judgment calls" section for `digest.inferredFindings` — renders nothing when that list is empty, the same "no section at all" discipline `SessionPage.tsx`'s `AgentLanes` already established for an empty `envelope.lanes`. Mockup parity item #6 (FR-42, issue #46) added `<CleanChecks checks={digest.silentChecks} />` below it, the same "no section at all when empty" discipline. Mockup parity item #9 added `<MethodologyFooter masthead={digest.masthead} />` at the very bottom. Digest session-naming Slice 2 threads `digest.masthead.repositoryScope.sessionLabels` down to each `FindingRow` alongside its existing `sessionIds` prop. The pager & date-range filter task added `<DateRangeFilter>` (below the repository selector) driving a `range` state passed to `useDigest`, and `<Pager>` beneath the ranked-findings list slicing `digest.rankedFindings` client-side at `PAGE_SIZE = 25`; `applyRange` resets `page` to 1 whenever the range changes, since a new range re-scopes the whole list server-side (see the non-obvious decision in `AecoPostMortem.Api/CLAUDE.md`, "A date-range filter re-scopes the whole analysis"). Code review round: a `<p role="status">Updating…</p>` renders while `query.isRefetching` is true rather than blanking the page; `rangeActive`/`noSessionsInRange` (derived from `range` and `scope.sessionIds.length`) gate a fourth designed-state sentence and suppress the ranked list, pager, judgment calls and clean-checks sections together — see the non-obvious decision below |
| `src/api/digest.ts` | the `DigestEnvelope`/`FindingEnvelope`/`SuggestionEnvelope`/`RepositoryScopeEnvelope`/`AdherenceFigure`/`SilentCheckEnvelope` shapes and `fetchDigest`, hand-kept in sync with `AecoPostMortem.Api.DigestEnvelope` (`src/AecoPostMortem.Api/DigestEnvelope.cs`) — no generated client exists yet, the same gap `api/appState.ts` documents. `RepositoryScopeEnvelope.sessionIds` (mockup parity item #2) was added here in the same change that added the server field, once the prerequisite check found it missing — see the note below. FR-48 (issue #52, S-42) added `DigestEnvelope.inferredFindings` — real, served data that had silently gone undeclared (and therefore dropped on arrival) since `InferredFindings` shipped server-side; see "A missing wire field can hide even after its server-side story ships" below. Mockup parity item #5 added `FindingEnvelopeBase.headline` — a full written sentence naming the problem, mirroring `AecoPostMortem.Api.FindingEnvelope.Headline`. Mockup parity item #6 (FR-42, issue #46) added `SilentCheckEnvelope` and `DigestEnvelope.silentChecks`, added to the server contract in the same change. Mockup parity item #15 changed `RuleCoverageStatus` from `'NotYetAnalyzed'` (a bare string literal type) to a closed `{state:'notYetAnalyzed'}` / `{state:'analyzed'; counts: RulesInventoryStatusCountsEnvelope}` union, importing `RulesInventoryStatusCountsEnvelope` from `./rulesInventory` rather than redeclaring it — mirroring `AecoPostMortem.Api.RuleCoverageStatusEnvelope` exactly. Digest session-naming Slice 2 added `RepositoryScopeEnvelope.sessionLabels: Record<string, string>` — a session's own display label keyed by session id, mirroring `AecoPostMortem.Api.RepositoryScopeEnvelope.SessionLabels`. The pager & date-range filter task added `FromParameter`/`ToParameter` and the `DateRange` type (`{from, to}`, both `string \| null`, `yyyy-MM-dd`) — `fetchDigest`'s new optional first parameter, appended to the query string only when non-null, mirroring `AecoPostMortem.Api.ApiHost.FromParameter`/`ToParameter` exactly |
| `src/digest/CleanChecks.tsx` | Mockup parity item #6 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`, FR-42, issue #46): "Checks that found nothing" — a card per `SilentCheckEnvelope`, each naming the check (its abstract `CheckId` humanised, e.g. `hook-failure` → `Hook Failure` — a pure display transform, not a served display name), its population, its zero count, and a `ProvenanceBadge` reused verbatim from `FindingRow`'s own. Renders no section at all when `checks` is empty |
| `src/digest/MethodologyFooter.tsx` | Mockup parity item #9 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`, "Methodology footer"): states what was measured and how the per-finding session strip's positions are sourced. Unlike the mockup's own footer — one fixed set of numbers hand-typed for one frozen date (`~/.copilot/` on 2026-08-16) — every figure here is read straight off the `MastheadEnvelope` this page already fetched, the same "nothing on this page counts anything" discipline `Masthead.tsx` documents for its own figures; no separate fetch, no recomputation. Carries no "not measured, shown only to demonstrate the layout" caveat paragraph — that is the mockup admitting its own placeholder data, and this app's findings are all real. `formatSpan`/number formatting are reimplemented locally (not imported) rather than exported from `Masthead.tsx`, since this story's own scope kept that file untouched. Code review Important (both reviews): an optional `range` prop (`{from, to} | null`, default `null`) adds a second paragraph — "Ranked over N of M sessions, …" — and a clause on the session-strip sentence ("within the applied date range") whenever a filter is active, so this footer's own stated job ("what was measured") stays true instead of contradicting the corpus-wide first paragraph; `null` (no filter) renders neither, byte-for-byte the same as before the prop existed |
| `src/digest/DateRangeFilter.tsx` | The pager & date-range filter task: two `<input type="date">` fields (`From`/`To`, explicit `htmlFor`/`id` pairing rather than implicit label wrapping — a real accessibility gap a live-browser check caught, see the non-obvious decision below), an `Apply` button that reports the pending values on submit (never on keystroke, so typing does not itself trigger a re-fetch of a corpus this large), and a `Clear` button, shown only while a filter is active, that resets both fields and reports `(null, null)`. `role="search"`/`aria-label="Date range"` names the whole control as one reachable group, the same "one named group" pattern `Masthead`'s own `role="group"` establishes. A `Filters by session start date (UTC).` hint states what the two dates filter on and that the boundary is UTC (code review Minor, both reviews). Code review Critical (both reviews — see the non-obvious decision below): `submit` refuses an inverted range (`From` after `To`, compared as plain ISO strings) and renders an inline `role="alert"` instead of calling `onApply`, so the request that would 400 is never sent; `min`/`max` on the two inputs are a second, earlier line of defence via the native picker, and `noValidate` on the `<form>` keeps this component's own check as the sole gate rather than a browser constraint-validation failure silently blocking the submit event before it runs |
| `src/digest/Pager.tsx` | The pager & date-range filter task: `Previous`/`Next` buttons plus a `Page X of Y` status, `role="group"`/`aria-label="Findings pages"`. Renders nothing at all when `pageCount <= 1` — the same "no control unless there is a real reason for one" discipline `RuleCoverageBar`/`StepFlag` already follow. Client-side only — see the non-obvious decision below for why a server-side offset/limit contract was not built. Code review Important (both reviews): the status text is `role="status"` (an implicit live region) and a focus target (`tabIndex={-1}`) — every page change after first mount moves focus onto it, so landing on the first/last page (which disables the very button just clicked) never drops focus to `<body>` with nothing announced |
| `src/api/useDigest.ts` | the fetch hook `DigestPage` reads, mirroring `useAppState`'s loading/error/loaded shape. The pager & date-range filter task added two optional scalar parameters, `from`/`to` (not a `{from, to}` object — see the non-obvious decision below), and re-fetches whenever either changes — the same "a new request, not a filter over what is already loaded" shape `useRulesInventory` established for switching rule-set versions, including the identical `aborted` guard on the resolved path (not only the rejected one) so a stale response settling after the range changed can never overwrite the new request's state. Code review Important (both reviews): `DigestQuery`'s `'loaded'` shape now also carries `isRefetching: boolean` — a re-fetch after the first successful load keeps the previous `digest` attached with `isRefetching: true` instead of reverting to bare `'loading'`, so `DigestPage` never blanks the masthead, selector or filter control mid-interaction; only the true first fetch (no previous digest to keep showing) still reports `'loading'` |
| `src/digest/Masthead.tsx` | FR-41's corpus masthead (S-36, issue #44): sessions, span, repositories, events, tool calls and rule coverage, every figure read straight off `MastheadEnvelope`'s ingest-time counters. Marks itself `data-provisional` mid-ingest and says an empty corpus has no span. Mockup parity item #15 replaced the rule-coverage cell's plain `ruleCoverageText` lookup with `<RuleCoverageBar>` once `masthead.ruleCoverage.state === 'analyzed'`, keeping the `notYetAnalyzed` text branch unchanged |
| `src/digest/RuleCoverageBar.tsx` | Mockup parity item #15: the masthead's rule-coverage bar — a real proportional four-color bar (watched/checkable-not-built/normative-but-unobservable/not-a-rule) plus a legend, ported from the mockup's `.covbar`/`.covkey` with this app's own design tokens. Proportional over all four statuses (`RulesInventoryStatusCountsEnvelope.total`), a deliberate divergence from the mockup's own three-segment bar — see the component's own remarks for why. States an honest empty sentence for a rule-set version with zero extracted statements, never an invisible zero-width bar |
| `src/digest/FindingRow.tsx` | one digest row (Scenario 1, issue #45): collapsed by default; expanding it reveals the quoted evidence, `ProvenanceBadge`, `RecurrenceStrip` and `SuggestionBlock`. The `sessionsAffected` count (S-36) leads the summary at display size and stays visible while collapsed. Mockup parity item #2 added `SessionStrip`, also visible while collapsed. FR-48 (issue #52, S-42) added `variant?: 'ranked' \| 'unranked'` (default `'ranked'`) — `'unranked'` omits that leading count for a `DigestEnvelope.inferredFindings` entry, since a hypothesis is deliberately never ranked by it. Mockup parity item #5 replaced the collapsed row's label — `finding.headline` (a full written sentence) instead of the bare `finding.recurrence.key` — in the renamed `finding-row__headline` span (`FindingRow.css`, sans font instead of the mono font a bare key used). Digest session-naming Slice 2 added the optional `sessionLabels` prop (default `{}`, the same "optional, no fixture edits forced" convention `findings`/`thinking` already established on `SessionTapeStep`), passed straight through to `RecurrenceStrip` |
| `src/digest/SessionStrip.tsx` | Mockup parity item #2 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`): one cell per session in `masthead.repositoryScope.sessionIds`, lit where a finding's own `recurrence.occurrences` touched that session — the mockup's `.strip`, ported. Hidden under 820px, mirroring the mockup's own breakpoint |
| `src/digest/AdherenceFigureBlock.tsx` | FR-33 (S-24, issue #38): the only place in the app that renders an adherence percentage — and it renders the per-operand resolution table and rule-set version with it, so no surface can show the number alone. FR-39 (S-35, issue #43) added `data-emphasis="prominent"` on the percentage span, the marker `MonitorComparisonBlock.tsx`'s own session count shares |
| `src/digest/RecurrenceStrip.tsx` | Scenario 2: names every session a finding touched (`Recurrence.occurrences`), not only the count. Mockup parity item #21 (issue TBD): each session id is a `react-router-dom` `<Link to={`/sessions/${sessionId}`}>` rather than plain text — an operator previously had to copy a session id and hand-edit the URL bar to reach `/sessions/:sessionId`, the route that already renders it. Digest session-naming Slice 2 added the optional `sessionLabels` prop (default `{}`): the link's own visible text is `sessionLabels[sessionId] ?? sessionId` — a session's own resolved display label when one exists, the raw id otherwise — with the raw session id always carried as the link's `title` (tooltip) |
| `src/digest/ProvenanceBadge.tsx` | PRD §3.8's three provenance levels, rendered distinguishably — a `data-provenance` attribute drives a distinct colour per level, alongside the badge's own text label |
| `src/digest/SuggestionBlock.tsx` | Scenario 4: renders `SuggestionEnvelope`'s `present`/`absent` states — an explicit "No suggestion is offered." for `absent`, never a blank area. Mockup parity item #3 added a small uppercase `Suggested change` label (`.suggestion-block__label`, styled like `ProvenanceBadge`/`FindingRow`'s own mono-font labels) above the `present` sentence only — the `absent` state stays label-free, since a "Suggested change" heading over "No suggestion is offered." would read as self-contradictory, and the mockup itself never depicts an absent-suggestion box |
| `src/digest/RepositorySelector.tsx` | Scenario 3 / PRD Part 8 Q5: shows the selected repository and offers every available one — the seam for a later cross-repository view, not that view itself |
| `src/api/monitor.ts` | FR-39's `MonitorComparisonEnvelope` shape and `fetchMonitorComparison`, hand-kept in sync with `AecoPostMortem.Api.MonitorComparisonEnvelope` (`src/AecoPostMortem.Api/MonitorComparisonEnvelope.cs`) — reuses `AdherenceFigure` from `digest.ts` and `RuleSetVersionEnvelope` from `rulesInventory.ts` rather than redeclaring either |
| `src/digest/MonitorComparisonBlock.tsx` | FR-39 (S-35, issue #43): renders a `MonitorComparisonEnvelope` as two sides, Before and After, each an `AdherenceFigureBlock` preceded by its own session count at the identical visual weight (`adherence-figure__percentage`'s own class plus `data-emphasis="prominent"`) — Scenario 2's "as visible as the percentage". Mounted for real by `routes/MonitorPage.tsx` |
| `src/api/useMonitorComparison.ts` | The Monitor comparison's own reachable-surface task: the fetch-per-`(before, after)` hook `MonitorPage` reads, mirroring `useRulesInventory`'s re-fetch-on-change shape. Resolves adjacency locally — a real sort of `availableVersions` by `firstSessionStartedAt`, the identical key `Rules.RuleSetVersionAdjacency.RequireAdjacentPair` sorts by — *before* ever calling `fetchMonitorComparison`, so a non-adjacent pair never reaches the network (`'notAdjacent'`) and a 404 for a pair already confirmed adjacent is unambiguous (`'noComparableRule'`) — see the non-obvious decision below for why the server alone cannot be asked to distinguish the two, and for the code-review round that made the sort real rather than a trust in array order |
| `src/routes/MonitorPage.tsx` | The Monitor comparison's own reachable-surface task (FR-39, S-35, issue #43): a fourth routed page, `/monitor`. Fetches `/api/rules-inventory` (no version — the default fetch already carries the full, chronologically ordered `availableVersions`) via `useRulesInventory`, defaults to the two most recent versions (derived at render time, not via an effect), and offers two independent, numbered selects (`VersionPairPicker`/`VersionSelect`) so the operator can freely choose any pair — including a deliberately non-adjacent one, to see the honest refusal. States a distinct message when the selected repository is `null` (no repository recorded anywhere in the store) *before* ever reaching `useMonitorComparison`, since that scope's own 404 would otherwise be mislabelled `'noComparableRule'`. Renders `MonitorComparisonBlock` on success, and one of three distinct, stated states otherwise (not adjacent / no comparable rule / unreachable API) — never a blank area for any of them |
| `src/api/session.ts` | the `SessionEnvelope`/`SessionMasthead`/`SessionTapeStep`/`SessionFindingChip`/`SessionRecordingStatus` shapes and `fetchSession`, hand-kept in sync with `AecoPostMortem.Api.SessionEnvelope` (`src/AecoPostMortem.Api/SessionEnvelope.cs`). FR-21 part 2 of 3 (S-52, issue #16) added `ThinkingEnvelope`/`RawStepEventEnvelope`/`StepEvidenceEnvelope` and `fetchStepEvidence`, mirroring `AecoPostMortem.Api.StepEvidenceEnvelope`; FR-21 part 3 of 3 (S-53, issue #17) added `SessionRecordingStatus`; FR-22 (S-09, issue #18) added `AgentOutcome`, `SubagentOutputEnvelope` and `SessionAgentLane`, plus the required `SessionEnvelope.lanes` field; FR-23 (S-10, issue #19) added `ModelReasoningReadability` and `ThinkingEnvelope.Unavailable.readabilityByModel` (optional, unlike the server's `required`-but-nullable field, so pre-existing test literals still type-check). Mockup parity item #14 added `SessionMasthead.startedAt`/`.endedAt`, mirroring `AecoPostMortem.Api.SessionMastheadEnvelope`'s own two new fields. Mockup parity item #17 added `SessionTapeStep.findings?: FindingEnvelope[]`, mirroring `AecoPostMortem.Api.SessionTapeStepEnvelope.Findings` — deliberately `?:` (optional) rather than a required field the way the server's own field is: the server always sends it, but three engineers' worktrees touch `Tape.test.tsx`'s many literal step fixtures this same round, and a required field would force an edit to every one of them for a field their own tests never exercise (see the note below). A tool call's own result added `StepEvidenceEnvelope.result: RawStepEventEnvelope`, mirroring `AecoPostMortem.Api.StepEvidenceEnvelope.Result` — required, matching the server's own `required`ness (this file is not touched by other concurrent worktrees the way `Tape.test.tsx`'s fixtures are), reusing the identical `RawStepEventEnvelope` union rather than a new type. What triggered a hook added `SessionTapeStep.triggeredBy?: string \| null` (optional, the same "existing test literals still type-check" convention `promptText`/`findings`/`thinking` already establish), plus `HookTriggerArguments`/`HookTriggerEnvelope` and the required `StepEvidenceEnvelope.trigger: HookTriggerEnvelope`, mirroring `AecoPostMortem.Api.HookTriggerArguments`/`HookTriggerEnvelope`/`StepEvidenceEnvelope.Trigger` — see `AecoPostMortem.Api/CLAUDE.md`'s matching non-obvious decision for the full shape and the real-corpus verification behind it |
| `src/api/useSession.ts` | the fetch-per-`sessionId` hook `SessionPage` reads; loading renders nothing, an error (404 or unreachable API) is one explicit state |
| `src/api/useStepEvidence.ts` | FR-21 part 2 of 3 (S-52, issue #16): the fetch-per-`(sessionId, stepId, kind)` hook the inspector reads once a step is selected, mirroring `useSession`'s loading/error/loaded shape |
| `src/routes/SessionPage.tsx` | FR-21, part 1 of 3 (S-08, issue #15): the Flight Recorder — masthead and time-ordered tape. FR-21, part 2 of 3 (S-52, issue #16) added the finding chip row, step selection (delegated to `session/Tape.tsx`), and the inspector's Detail/Thinking/Raw tabs, with an explicit "pick a step" state when none is selected. FR-21, part 3 of 3 (S-53, issue #17): renders the chip row, tape and inspector only when `envelope.status.kind === 'complete'`; otherwise renders `NonFinalState`, one distinct message per non-happy `SessionRecordingStatus` kind. Reads `sessionId` from the route; no `sessionId` (bare `/sessions`) states "no session selected" rather than reusing `ComingSoon`, since the surface itself is built. FR-22 (S-09, issue #18) added `AgentLanes`/`SubagentOutputPanel`: one entry per subagent, rendered between the finding chip row and the tape, each carrying the report it actually produced (or a stated "no output"/"failed" state) — renders nothing when `envelope.lanes` is empty, the same "no section at all" discipline `ComingSoon`'s sibling surfaces avoid reinventing. Mockup parity item #14 added the masthead's own "Wall clock" field (`formatWallClockRange`) — the real start→end range, alongside (not instead of) `Elapsed`. Mockup parity item #11 added `<MethodologyFooter masthead={envelope.masthead} />` at the very bottom of `LoadedSession`, rendered for every status (not only `'complete'`) since every field it reads comes off the masthead, which itself renders regardless of `SessionRecordingStatus`. A tool call's own result added `RawEventBlock` (a small shared helper for the "literal payload, or a stated reason" rendering both `raw` and `result` need) and widened `RawPanel` to render two labeled blocks, "Call" and "Result", instead of one. What triggered a hook added a conditional "Triggered by" row on `DetailPanel` (rendered only for a `'hook'` step, `step.triggeredBy ?? 'No tool trigger resolved — see Raw tab for detail.'`) and a third `RawPanel` block, `TriggerBlock`, reusing the identical `.inspector__raw`/`.inspector__raw-payload` rendering (and its existing `max-height`/`overflow-y` bound) `RawEventBlock` already gives "Call"/"Result" — no new CSS |
| `src/session/Tape.tsx` | FR-21, part 3 of 3 (S-53, issue #17): the tape itself, moved out of `SessionPage.tsx` — fixed-row-height virtualisation (only the scrolled-to window plus overscan is mounted, proven at the largest measured session scale, 84 turns + 764 tool calls) and full keyboard reachability (a single roving tab stop on the list itself; Arrow/Home/End/PageUp/PageDown move a `selectedIndex` that pulls its row into the mounted window before selecting it, `aria-activedescendant` names it for assistive technology). Reconciled with FR-21 part 2 of 3 (S-52, issue #16)'s step-selection contract: each row's content sits inside a `tabIndex={-1}` button — a click target, never a second tab stop — so `SessionPage`'s inspector gets the same `onSelectStep` callback from a mouse click that it already got from keyboard Enter/Space. FR-22 (S-09, issue #18) added per-row lane markers: `data-owner-kind`/`data-agent-id`/`data-agent-lane`, the last a deterministic hash of `agentId` into one of 8 colours (`laneIndex`), rendered as a coloured left border via the `--session-tape-lane` CSS custom property. Mockup parity item #17 added `StepFlag` and `data-flagged`: a small `role="img"` glyph, rendered only when `step.findings` is non-empty, with one joined `aria-label` naming every flagging finding's own `headline` — the same "one accessible marker naming everything it represents" precedent `SessionStrip.tsx` already established for its own compact cells |
| `src/session/Tape.css` | `Tape.tsx`'s absolute-positioning layout (each mounted row placed by `top: index * rowHeight` inside a spacer-sized scroll container), the `aria-selected` highlight, and the `tabIndex={-1}` row button's own layout. Mockup parity item #17 added the `[data-flagged='true']` background (the `--flag`/`--flag-soft` tokens `SessionPage.css`'s finding chips already use) and `.session-tape__flag`'s own colour |
| `src/session/MethodologyFooter.tsx` | Mockup parity item #11 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`, "Methodology footer — Session"): the session Flight Recorder's sibling to `digest/MethodologyFooter.tsx` (item #9). States what was measured (turns, tool calls, subagents, skill invocations, recorded date — all read straight off the `SessionMasthead` `SessionPage.tsx` already fetched, no new fetch), that this app's rule findings are tool-choice checks rather than code-content checks, and general, always-true context for how the Thinking tab's readable-vs-encrypted split is measured per model once a step is selected — deliberately no live percentage, since `readabilityByModel` is served per step (`StepEvidenceEnvelope`), not part of `SessionMasthead`. Reimplements its own `plural`/date formatters locally rather than importing from `digest/MethodologyFooter.tsx`, the same "don't share across page-specific components" precedent that file establishes |
| `src/index.css` | global reset plus this app's design tokens — see "Design tokens are ported verbatim from the mockups" below |

## Non-obvious decisions

### The app-state banner is shown above every route, not only the digest

S-48's Scenarios 2 and 3 say "when the operator opens the app," not "opens the digest" — the
diagnosis is a fact about the store and the source, not about any one surface. `AppShell` renders
`AppStateBanner` once, above the routed `<Outlet />`, so the same message appears regardless of
which of the three routes the operator lands on, and steps aside on its own (renders nothing) once
the state is `Ready`.

### A fourth, unnamed state: the API host is unreachable

Neither Gherkin scenario names "the API is not running," but a real browser pointed at a dead
`serve` process hits exactly that — `useAppState`'s `fetch` rejects rather than resolving to a
diagnosis. `AppStateBanner` renders a distinct `role="alert"` message for it
("Could not reach the local API. Is `aecopostmortem serve` running?"), rather than either folding
it into `EmptyStore` (wrong diagnosis: the store might not be empty at all) or showing nothing
(silent from the operator's side, which is the exact failure PRD §3.1 opens this story to prevent).

### Session entry point is always a session id; bare `/sessions` was removed

`App.tsx` registers only `sessions/:sessionId` — the bare route was removed because the digest
now provides real, clickable entry points to sessions (PR #120: every session id on the Digest is a
`<Link to="/sessions/:id">`). The bare `/sessions` route had no session id and thus nothing to show,
making it unreachable once the nav link was removed. Session selection via a list picker is a later
story, not a gap; only `/sessions/:sessionId` renders the masthead and tape.

Removing it opened a second, subtler gap that the route removal alone did not close: an operator
arriving at `/sessions` from a stale bookmark or a hand-typed URL matched *no* route at all, and
React Router renders nothing for an unmatched path — not even `AppShell`, so the page came up
completely blank, with no navigation and no way back. Verified in a real browser before fixing it
(`document.body.innerText` was the empty string). `App.tsx`'s `*` catch-all (`NotFound`) closes it
for every unrouted URL, not just this one, with the same "state it, never render a blank area"
discipline `SuggestionBlock`'s absent case and `NonFinalState` already follow.

`SessionPage` still guards on a missing `sessionId`, and that guard is deliberately *not* dead code
even though `/sessions/:sessionId` is now the only route that mounts the page: `useParams` types
every param optional and offers no way to tell React Router otherwise, so the guard is what narrows
`string | undefined` to `string`. Removing it does not merely lose a UI state — it fails the build
(`tsc -b`, `TS2322`). It renders a stated message rather than asserting non-null with `!`, so a
future route registration that forgets the param fails visibly instead of rendering a session view
against `undefined`.

### A step's offset and elapsed time are plain numbers, not a serialised duration

`session.ts`'s `SessionMasthead.elapsedMs`/`SessionTapeStep.offsetMs` are milliseconds
(`number`/`number | null`), matching `AecoPostMortem.Api.SessionEnvelope`'s own choice to serialise
`TimeSpan` as milliseconds rather than a duration string — one fewer format both sides would
otherwise have to agree on by hand. `startedAt`/`endedAt` (mockup parity item #14) stay plain
ISO-8601 strings instead — there is no duration to convert here, only a timestamp, matching
`AecoPostMortem.Api.SessionMastheadEnvelope`'s own choice to leave `DateTimeOffset` as-is.

### The masthead's wall-clock range needs real time-of-day, unlike the Digest's own date-only span

`SessionPage.tsx`'s `formatWallClockRange` is a local formatter, not an import from
`digest/Masthead.tsx`'s `formatSpan` — deliberately: a corpus spans months, so `formatSpan` only
ever needs a date; one session is typically minutes to hours, so a date-only range here would read
as identical start and end for the common case (or worse, silently misleading). `formatWallClockRange`
therefore always shows the start as a full date and time, and shows the end as a bare time-of-day
only when it falls on the same UTC day as the start — a session that happens to cross midnight still
gets a full date on the end too, so the range never reads ambiguously. Both formatters fix
`timeZone: 'UTC'` explicitly, the same determinism `Masthead.tsx`'s own `day` formatter already
established, so a test's assertion does not depend on the host machine's local timezone.
`endedAt === null` (a session still ingesting, per `SessionRecordingStatusEnvelope` — the masthead
renders even for a non-`complete` status) renders as "… – still running", never a blank or a
misleading dash: this state is real, not hypothetical, and `SessionPage.test.tsx` covers it with a
dedicated fixture rather than assuming the happy path is the only one worth testing.

### The two Gherkin empty states are still hand-kept in sync between server and client

`web/src/api/appState.ts`'s `AppStateKind` union (`'noSourceFound' | 'emptyStore' | 'ready'`) is
typed by hand against `AecoPostMortem.Api.AppStateReport`'s `AppStateKind` enum — no shared schema
or generated client exists yet. `AecoPostMortem.Api.CLAUDE.md` documents the matching regression
test (`ApiHostTests.The_kind_field_is_serialised_as_camelCase_on_the_wire`) that exists precisely
because this hand-kept contract drifted silently once already (a missing naming policy shipped
`"EmptyStore"` instead of `"emptyStore"`, and neither side's mocked tests caught it).

### `/api/digest` is served for real, with no frontend change needed to light it up

FR-41's real orchestration (six of the seven check orchestrators plus `MastheadCounters` and a
`RepositoryScope`, all assembled into one `ProcessDigest`) landed in `ApiHost.GetDigest`
(`AecoPostMortem.Api/CLAUDE.md`, S-36, issue #44). `fetchDigest`/`useDigest` had targeted `/api/digest`
ahead of that wiring, the same seam `fetchAppState`/`useAppState` established for `/api/app-state`
before S-48 served it for real — and the prediction held: a real browser against the live 35-session
reference corpus now renders the masthead and 295 ranked findings with the exact frontend code that
was already here, no change required. `DigestPage.test.tsx` and `App.routing.test.tsx` still mock
`/api/digest`'s response directly rather than standing up a real store, the same as every other
route's tests.

### The tape's virtualisation math is driven by fixed constants, never by measuring the real DOM

`Tape.tsx`'s `ROW_HEIGHT_PX`/`VIEWPORT_HEIGHT_PX`/`OVERSCAN_ROWS` constants decide which window of
steps is mounted, computed only from `scrollTop` state — never from `getBoundingClientRect` or a
`ResizeObserver`. jsdom reports zero for every element's real layout size, so a measured-height
approach would be untestable without a real browser; a fixed-height approach is the same technique
`react-window`'s `FixedSizeList` uses for the identical reason, and it is what makes
`Tape.test.tsx`'s scale assertions (848 steps, far fewer than 100 `<li>`s mounted) deterministic.
The scroll container's own `scrollTop` is a controlled value — a `useEffect` pushes React state onto
the DOM node's `scrollTop` property, and `onScroll` reads it back — because setting `scrollTop`
programmatically (keyboard navigation scrolling a distant row into view) does not itself dispatch a
`scroll` event in either a real browser or jsdom.

### The tape's keyboard model is a single roving tab stop with a state-driven selection, not per-row focus

FR-21's Scenario 2 (S-53, issue #17) — "every step can be reached and selected" — cannot be built as
one `tabIndex={0}` per row: real DOM focus only works on a mounted node, and a virtualised row is
deliberately not mounted most of the time. `Tape`'s `selectedIndex` is React state instead of DOM
focus; only the `<ul>` itself is a tab stop (`tabIndex={0}`), Arrow/Home/End/PageUp/PageDown move
`selectedIndex` and call `ensureVisible` (which adjusts `scrollTop` if the target row falls outside
the current window) before rendering the newly selected row, and `aria-activedescendant` on the
`<ul>` names the selected row's `id` so assistive technology still announces which step is current —
the same composite-widget pattern (one host element, `aria-activedescendant` naming the active
descendant) a combobox or a virtualised listbox already uses for exactly this "many options, one tab
stop" shape. `Tape.test.tsx` proves reachability at both ends (`End` then `Home` on an 848-step
tape) and via a page jump (20× `PageDown`), rather than keystroking every intermediate step, since
the same `moveSelection`/`ensureVisible` code path handles every index identically.

### The tape keeps `role="list"`/`role="listitem"` — S-08's existing tests are the reason

`SessionPage.test.tsx`'s S-08-era assertions (`findByRole('list', { name: 'Tape' })`,
`findAllByRole('listitem')`) predate virtualisation and keyboard navigation; `Tape.tsx` keeps the
same `<ul aria-label="Tape">`/`<li>` shape rather than switching to an explicit
`role="listbox"`/`role="option"` pair, so those assertions still hold unchanged. The one addition
inside the list — a spacer `<li aria-hidden="true">` that gives the scroll container its real
`scrollHeight` — is excluded from `getAllByRole('listitem')` by testing-library's own default
(`aria-hidden="true"` removes an element from the accessibility tree), so it never shows up as an
extra row in either a test or a screen reader.

### A non-final session state replaces the tape entirely, rather than rendering it faded or partial

FR-21 part 3 of 3's Scenario 3 says "states that the session is incomplete rather than rendering a
partial tape as final" — read literally as "no tape rendered at all" here, not a softened or
greyed-out one: `LoadedSession` branches on `envelope.status.kind` before deciding whether to render
`<Tape>` or `<NonFinalState>` at all, so an `ingestIncomplete`/`reconstructionFailed` session never
mounts the tape (or the masthead's own turn/tool-call counts read as final) in the first place.
`NonFinalState` renders one message per kind — `ingestIncomplete`'s prose distinct from
`reconstructionFailed`'s "Reconstruction failed for this session." plus its own `<ul>` of
`Skipped` reasons — and neither reuses `session-page__alert`'s `role="alert"` styling or wording,
since a load failure (S-08, "could not load this session") is a third, distinct condition from
either.

### One component owns both halves of an adherence figure, so neither can render alone

FR-33 (S-24, issue #38) says the layer used per operand and the resulting call counts are shown
*with* the figure. `AdherenceFigureBlock` renders the percentage **and** the resolution table, and
`FindingRow` delegates the whole `figure` to it rather than reading `figure.percentage` itself —
so there is no component in this app that can put a percentage on the page without the operands
beside it. Splitting them into a `Percentage` and a `Resolution` component would have made the
pairing a caller's responsibility, which is the convention-not-structure failure the story's own
edge case rules out ("a second client bypassing the UI must be equally unable to get a bare
figure" — the server contract handles that half; this is the same discipline on this side).

The collapsed row deliberately shows no figure at all. Putting the percentage in the summary and
the resolution one click away inside the detail would technically satisfy "both are on the page",
but the operator would read the number before ever seeing what produced it — the exact reading
FR-33 exists to prevent. `FindingRow.test.tsx`'s `does not show the percentage until the row is
expanded` is that decision as an assertion, not an accident of layout.

A `null` percentage (PRD §5.5's zero-occurrence case) renders as a sentence — "No calls were
observed for this rule, so it has no adherence percentage." — never `0%`, which would read as
measured disobedience rather than absent data. The resolution table still renders, so the operator
can see *which* operands found nothing and through which layer.

### The Monitor comparison's session count shares the percentage's own class, rather than a lookalike

FR-39 Scenario 2 (issue #43): "the session count on each side is as visible as the percentage."
`MonitorComparisonBlock`'s per-side sample size carries `adherence-figure__percentage` — the
identical CSS class `AdherenceFigureBlock` puts on its own percentage span — plus the same
`data-emphasis="prominent"` marker, rather than a second class in `MonitorComparisonBlock.css` that
happens to declare the same `font-size`/`font-weight` values today. Two classes that merely agree by
coincidence can drift the moment either file is edited on its own; one shared class cannot.
`MonitorComparisonBlock.test.tsx` asserts both spans carry `adherence-figure__percentage` and
`data-emphasis="prominent"` for exactly this reason — a structural equality check, not a comparison
of literal pixel values jsdom cannot reliably report anyway.

Reusing `AdherenceFigureBlock` for each side's percentage and resolution table (rather than
re-rendering the percentage inside `MonitorComparisonBlock` itself) is what keeps this file's own
"one component owns both halves of an adherence figure" rule, above, intact: there is still no
component in this app — this one included — that renders a bare percentage without the operand
table beside it. The session count is a second, equally prominent figure placed beside that
pairing, never a second percentage competing with it.

### The ranking count is the one figure that stays visible while a row is collapsed

S-36's edge case (issue #44): "a finding touching one session is an anecdote and must be visually
subordinate to one touching thirty — that's the ranking's entire purpose, so make the 'sessions
affected' count visually prominent, not a small annotation." A count only reachable by expanding the
row cannot do that job, so `FindingRow` puts `sessionsAffected` in the always-visible summary, at
display size, in a fixed-width leading column — scanning the list compares counts against each other
rather than against whatever length each finding's recurrence key happens to be. The expanded
`RecurrenceStrip` still names the individual sessions: the count ranks, the names explain. The number
itself comes from the envelope (`FindingEnvelope.SessionsAffected`), never recounted here from
`recurrence.occurrences` — see `AecoPostMortem.Api/CLAUDE.md` for why the server owns it.

### The per-finding session strip is scoped to the selected repository, not the whole corpus

Mockup parity item #2's own mockup (`docs/product-superpowers/discovery/mockups/digest.html`)
assumes one corpus, so its strip's "N" is simply every session the mockup knows about. This app's
digest is repository-scoped (PRD Part 8 Q5) and every ranked finding's own `recurrence.occurrences`
is already a subset of the *selected repository's* sessions, never the whole corpus
(`AecoPostMortem.Findings/CLAUDE.md`'s remarks on `ProcessDigest.Build`'s scoped `findings`
parameter) — `MastheadEnvelope.sessionCount` deliberately counts every session in the store
regardless of repository (`AecoPostMortem.Api/CLAUDE.md`'s `GetDigest` remarks), so it is the wrong
denominator for this strip. `SessionStrip` is instead handed `masthead.repositoryScope.sessionIds`
(the new field this story added, `RepositoryScopeEnvelope.sessionIds`), the exact session set every
check `ApiHost.GetDigest` runs was scoped to — a finding's own occurrences can therefore always be
positioned against it. This was confirmed as a real prerequisite gap during this story's own
brainstorming pass, not assumed: neither `MastheadEnvelope` nor `DigestEnvelope` exposed any ordered
session list before this change, only a bare `sessionCount`, so there was no way to answer "which of
the N sessions" at all — closing it needed a small, real backend addition
(`Findings.RepositoryScope.SessionIds`, `AecoPostMortem.Api/CLAUDE.md`'s matching note), not a
pure-frontend change as the item's original prioritisation-doc estimate assumed.

### Nothing on this page counts anything

FR-41's masthead is the one surface tempted to scan the corpus, and measurement says it must not
(126 ms per million rows on SQLite, 118 ms on Postgres). `Masthead` renders `MastheadEnvelope`'s
fields directly — counters maintained at ingest — and deliberately derives nothing, not even from
data already in hand: it never computes a session total from `rankedFindings`, for instance, because
a figure that happens to be cheap today is still the wrong contract and would quietly disagree with
the stored counter once the digest is scoped to one repository. The server-side half of this
guarantee is structural (`MastheadEnvelopeStructureTests`, `ProcessDigestStructureTests`).

### Three designed states, three different sentences

`DigestState` distinguishes "nothing analysed yet" from "analysis incomplete", and a genuine
zero-finding result is distinct from both — `AecoPostMortem.Findings.DigestState` draws the same
three-way split one layer down. `DigestPage` gives each its own wording ("Nothing has been analysed
yet…", "Analysis is incomplete…", "Every check ran and found nothing."), and `Masthead` adds the
mid-ingest note plus `data-provisional="true"` so the counts beside it are not read as final. Rule
coverage was a fourth honest not-yet through Release 1: a `Record<RuleCoverageStatus, string>` with
exactly one entry, rendering "Rules not yet analysed" with structurally no branch that could produce
a bare zero. Mockup parity item #15 replaced that lookup once `RuleCoverageStatus` gained a real
`analyzed` shape (`AecoPostMortem.Findings/CLAUDE.md`'s own remarks) — `Masthead` now branches on
`masthead.ruleCoverage.state` directly: `'notYetAnalyzed'` still renders the identical sentence,
`'analyzed'` renders `<RuleCoverageBar>`, a real proportional four-color bar plus a legend (see that
component's own row above) — the same two-designed-states discipline, just with the second state
finally real.

### The repository selector is a seam, not a working cross-repository switch

PRD Part 8 Q5 decided the digest defaults to one repository, selectable. `RepositorySelector` is a
real, interactive `<select>` — choosing a different `availableRepositories` entry does change what
it displays — but nothing in `DigestPage` re-fetches a cross-repository digest when that happens
(no orchestration for a second repository's digest exists yet either). This is this story's own
edge case, not an oversight: implement the default, keep the selector itself real, leave the
cross-repository view to later work.

### Switching rule-set versions is a new request, not a filter over what is already loaded

`RulesInventoryPage` holds the requested version hash in state and `useRulesInventory` re-fetches
whenever it changes — unlike `RepositorySelector`, which only changes what the digest *displays*
because no cross-repository digest exists to fetch. The difference is deliberate: FR-40's Scenario 6
says the inventory shows one version at a time, and a client-side filter would mean the response had
carried several versions' statements in the first place. Nothing in `rulesInventory.ts` can express
that — `availableVersions` carries identities and windows, never rows.

### The Monitor's two refusals are resolved on the client, not the server

FR-39's own PRD-level design says `/api/monitor-comparison` refuses in two structurally different
ways — a non-adjacent pair (`Rules.NonAdjacentRuleSetVersionsException`) and an adjacent pair whose
`after` version carries no comparable `PreferAOverB` statement (`ApiHost.GetMonitorComparison`'s own
early return) — but both collapse to a bare `Results.NotFound()` with no distinguishing body on the
wire (`AecoPostMortem.Api/CLAUDE.md`'s `GetMonitorComparison` remarks). The task that gave this
comparison its first real door had two ways to close that gap: widen the server's response to carry
a reason, or resolve the ambiguity entirely on the client. This picked the second, deliberately —
`api/useMonitorComparison.ts`'s `isAdjacent` re-implements the identical adjacency check
`Rules.RuleSetVersionAdjacency.RequireAdjacentPair` performs server-side: sorted by
`RuleSetVersionEnvelope.firstSessionStartedAt` (ordinal string comparison), tied-broken by
`firstSessionId` — a real sort over `availableVersions`, computed fresh from that array every time,
never a trust that the array's own order already matches. A pair the check calls non-adjacent
therefore never reaches the network at all (`'notAdjacent'`, confirmed via `read_network_requests`
in a real browser: zero `/api/monitor-comparison` calls), and a 404 for a pair the check already
confirmed adjacent can only be the other refusal reachable through this UI (`'noComparableRule'`) —
the request that would have produced the first kind of 404 was structurally never sent.

This was the smaller, more honest change available: it needed no change to `MonitorComparisonEnvelope`
or any C# test beyond one new field, and it does not risk the two sides' adjacency logic ever
disagreeing, since the client's own check is a real port of the server's rather than a heuristic guess
at "probably adjacent." Widening the server's response with an explicit reason field remains the more
robust fix if a second client (a future CLI report, a second frontend) ever needs the same distinction
without re-deriving it — deliberately deferred, not proposed as out of scope by oversight.

**Code review (round 2) caught three real gaps in this design's first pass, all fixed in the same
round rather than a later story:**

1. **The adjacency port initially trusted array order, not real chronology.** The first version of
   `isAdjacent` compared array *position* only, silently assuming `availableVersions` always arrived
   pre-sorted — a claim this file's own prose asserted but nothing on the TypeScript side enforced,
   since the wire envelope carried no sort key at all. `RuleSetVersionEnvelope.firstSessionStartedAt`
   (`src/AecoPostMortem.Api/RulesInventoryEnvelope.cs`) is the fix — `Rules.RuleSetVersion` already
   carried the field (the PR #108/#112 chronology fix, `AecoPostMortem.Rules/CLAUDE.md`), it simply
   never travelled onto the wire before this task. `isAdjacent` now sorts a copy of the array by that
   field before checking indices, so the claim "a real port, not a trust" is now literally true, and
   `useMonitorComparison.test.ts`'s `judges adjacency by real chronological order, not by the array
   order it is handed` hands the hook a deliberately scrambled array to prove it.

2. **`'noComparableRule'` had one real, reachable mislabel: no repository resolved at all.**
   `ApiHost.GetMonitorComparison` refuses unconditionally, before checking adjacency or any rule,
   when the whole store resolves no repository (`repositoryScope.SelectedRepository is null`) — a
   real scope `GetRulesInventory` happily serves (`RuleSetVersionEnvelope.repository: null`, "no
   recorded repository" in `RulesInventoryPage`). Reaching this page's picker in that scope would
   have fired a real request and mislabelled every resulting 404 as "no comparable rule," a false
   explanation neither this hook nor the page could actually verify. `MonitorPage.tsx` now checks
   `inventory.selectedVersion.repository === null` and states that fact plainly, before ever reaching
   `useMonitorComparison` with a real selection — the third, structurally unreachable-from-the-UI 404
   cause is now genuinely unreachable, not merely assumed to be.

3. **The array-identity dependency in `useMonitorComparison`'s effect was a foot-gun waiting for a
   second caller.** The first version depended on `availableVersions` by reference — safe only
   because `MonitorPage.tsx` had to introduce a module-level stable `NoVersions` constant to avoid an
   infinite render loop a fresh `[]` literal would otherwise cause. `useMonitorComparison` now computes
   `adjacent` (a plain boolean) during render and depends on that instead of the array itself —
   `NoVersions` is gone entirely, and no future caller passing a freshly-constructed array can
   reintroduce the loop, since the effect no longer looks at the array's identity at all.

`MonitorPage.tsx` was also simplified in the same round, beyond what review asked for: the
`useEffect` + `beforeHash !== null || afterHash !== null` guard that used to set the default pair
once is gone, replaced by deriving `defaultBeforeHash`/`defaultAfterHash` at render time
(`beforeHash ?? defaultBeforeHash`) — an explicit operator selection always wins, and there is no
render where the two selects can show a stale or mismatched default the way an effect-driven one
briefly could. `VersionPairPicker`'s two option lists are also numbered by chronological position now
(`1.`, `2.`, …, with `— most recent` on the last), the same "hashes are opaque without a marker"
reasoning `RulesInventoryPage`'s own single picker already applies — review named this the whole
premise of the page (adjacency) being otherwise undiscoverable from two bare hash lists.

### No status count is styled as a problem count

`StatusBreakdown` renders all four counts with the same class and an explicit
`data-emphasis="neutral"`, and `RulesInventoryPage.test.tsx` asserts it on every tile. This is the
story's own edge case: "Not a rule" is the largest bucket on the reference corpus (a measured 21 of
43) because FR-26's extraction unit is a markdown list item, so most list items were never going to
be rules. A warning colour on that tile would turn the corpus's own shape into an accusation and
undo the reason the surface exists.

### A statement row is named by `aria-label`, not by its contents

Each `<tr>` carries `aria-label={`Statement: ${text}`}`. A row's accessible name would otherwise be
computed from its cells, which means a retired row and an in-force row are addressed differently
purely because one has a retirement date in it — the label keeps every statement row addressable the
same way, and the rule text is still in the first cell for a reader.

### A retired row stays fully legible

`tr[data-retired='true']` gets a faint background and nothing else — no dimming, no reduced
contrast. FR-40's Scenario 5 says a retired rule stays *visible* with its adherence frozen; frozen
is not withdrawn, and styling it as faded would read as "less true" rather than "no longer in
force".

### Tape rows are buttons, not list items alone — selection has to be keyboard- and click-reachable

FR-21 part 2 of 3's Scenario 1 says "the operator selects any step" — `Tape`'s `<li>` wraps a real
`<button type="button">` rather than an `onClick` on the `<li>` itself, so a step is reachable by
keyboard (Tab, Enter/Space) the same way any other interactive control on the page is, and
`aria-pressed` states which step is currently selected without a second, parallel "selected" class a
test or a screen reader would have to infer separately.

### The Thinking tab's encrypted state names its model and never averages two models' readable shares

FR-23 (S-10, issue #19): `ThinkingPanel` renders `thinking.text` verbatim for the `'present'` case
(Scenario 1) unchanged from S-52. For `'unavailable'`, it now also renders `readabilityByModel` when
the server sent it (only for the provider-encryption reason) — a `ReadabilityByModel` list component
with one `<li>` per model, each showing that model's own `readableSharePercent` and the counts it was
computed from (never a bare percentage, the same "a figure never appears without what it's computed
over" discipline `AdherenceFigureBlock` follows for its own figure). A session using two models
therefore renders two list items, never a merged or averaged one — matching the story's own edge
case and `AecoPostMortem.Api.CLAUDE.md`'s matching non-obvious decision for where the figure is
computed. `readabilityByModel` is read straight off the wire with no client-side computation: this
page derives nothing, the same discipline `Masthead` and `AdherenceFigureBlock` already follow.

### The inspector fetches only Thinking/Raw; Detail needs no request of its own

`SelectedStepInspector` calls `useStepEvidence` once a step is selected, but `DetailPanel` reads
straight from the `SessionTapeStep` already in `envelope.steps` — every field FR-21's Detail tab
needs (`kind`, `stepId`, `label`, `timestamp`, `offsetMs`, `ownerKind`, `agentId`) already travelled
with the tape. Fetching evidence again for Detail would duplicate data already in hand and make the
Detail tab depend on network latency the other two tabs already have to tolerate.

### All three tab panels render together, hidden by `hidden` rather than only the active one mounted

`SelectedStepInspector` renders `DetailPanel`/`ThinkingPanel`/`RawPanel` inside three `<div>`s, each
toggled by the native `hidden` attribute rather than a conditional that unmounts the inactive two.
This is what the story's own edge case asks for structurally: "the Raw tab... must never be the tab
that gets cut under pressure" — a client that only mounts the active panel could plausibly ship a
later change that never mounts Raw at all if some future gate short-circuits before reaching it; a
component tree where all three tabs unconditionally exist has no such gap to introduce by accident.

### A finding chip's label is still `finding.recurrence.key` — `FindingRow`'s own label moved on, this one deliberately did not

`FindingChips` (`routes/SessionPage.tsx`) renders `chip.finding.recurrence.key` as a session chip's
visible label (FR-21 part 2 of 3), the same convention `digest/FindingRow.tsx` established for its
own row before mockup parity item #5 (below) gave it a real `headline` field to read instead. The
two surfaces now show two different labels for the same finding — a raw recurrence key on the chip
row, a full written sentence on the digest row — a real, known divergence this story's own scope cut
left open rather than a rendering bug: item #5's brainstorming pass scoped the change to
`FindingRow.tsx`/`api/digest.ts` only ("does NOT need to touch `SessionPage.tsx`"), and `FindingChips`
reads `SessionFindingChipEnvelope`, not `FindingEnvelope`, through `api/session.ts` rather than
`api/digest.ts` — `SessionFindingChip.finding` does carry the same `FindingEnvelope` shape
(`Api/CLAUDE.md`'s `SessionFindingChipEnvelope` remarks), including the new `headline` field, so
pointing this chip at `chip.finding.headline` instead is a small, well-scoped follow-up whenever a
story picks the chip row back up, not a new gap to close.

### A missing wire field can hide even after its server-side story ships

FR-48 (issue #52, S-42) shipped `DigestEnvelope.InferredFindings` server-side, real and populated
(`AecoPostMortem.Api/CLAUDE.md`) — but `api/digest.ts`'s `DigestEnvelope` interface never declared
it, so the field arrived on the wire and was silently dropped on every fetch: `fetchDigest`'s
`response.json() as DigestEnvelope` cast does not fail when the parsed object carries a field the
TypeScript type doesn't name, it simply never gets read. This is the same "hand-kept in sync, no
generated client" gap `api/appState.ts` documents, but a sharper case of it: unlike a naming-policy
mismatch (`AppStateKind`'s own past incident, `AecoPostMortem.Api/CLAUDE.md`), a *missing* field
produces no error on either side — both this file's own type and every test that mocks
`DigestEnvelope` stay green, because nothing forces the fixture to carry a field the interface
doesn't ask for either. `DigestPage.test.tsx`'s `digestWith` fixture now sets `inferredFindings`
explicitly (defaulting to `[]`) so a future field addition to the real envelope has to touch this
file to type-check, the same discipline that would have caught this gap immediately had the field
been required in a test fixture from the start.

`FindingRow`'s `variant` prop (above) is the one further wrinkle this closed: `Findings/CLAUDE.md`
already resolved *not* to rank a hypothesis by `sessionsAffected` server-side, but reusing
`FindingRow` unchanged for an inferred entry would have shown that exact number in the same leading,
display-size column S-36's own edge case designed specifically to look like a rank — a rendering
gap the server-side decision alone could not close. `variant='unranked'` omits that column without
losing the figure: `RecurrenceStrip`, unconditionally rendered on expand, already names every
session the finding touched.

### Design tokens are ported verbatim from the mockups, not redesigned

`src/index.css` defines this app's whole palette (`--ground`/`--surface`/`--sunk`/`--ink`/`--ink-2`/
`--ink-3`/`--rule`/`--rule-2`/`--accent`/`--accent-soft`/`--flag`/`--flag-soft`/`--ok`/`--ok-soft`/
`--infer`/`--infer-soft`/`--lane`/`--mono`/`--sans`) as CSS custom properties on `:root`, copied
hex-for-hex from `docs/product-superpowers/discovery/mockups/{digest,flight-recorder}.html`'s own
`:root` blocks — including a dark-mode set both via `@media (prefers-color-scheme: dark)` and via an
explicit `:root[data-theme='dark']` override for a future toggle (`data-theme='light'` forces light
regardless of system preference). Every other file in `src/` was then edited to replace its
hardcoded hex colours with `var(--token)` references to these. This is a values-only diff — no
component/JSX changed, and colours were not adjusted for contrast or otherwise redesigned along the
way, even where the mockups' own token choices read close to WCAG AA at small text sizes (e.g.
`--accent` on `--accent-soft`) — the point of this pass was parity with the approved mockups, not an
independent palette review. If the mockups' own palette changes later, this is where to re-port from.

One value is a known, deliberate inconsistency inherited from the mockups themselves, not introduced
here: `--ink-3` differs between the two dark-mode blocks (`8E96A4` under the media query vs `6B7280`
under `[data-theme='dark']`) in both mockup files — currently harmless since nothing in this app sets
`data-theme` yet, but worth flagging to whoever builds the toggle rather than assuming it's a typo in
this port.

The mockups' own `:focus-visible` rule also sets `border-radius: 2px` — deliberately dropped here.
The mockup is one inline `<style>` block where declaration order made that harmless; this app's
`main.tsx` imports `./App.tsx` (and therefore every component's own CSS) before `./index.css`, so
Vite bundles `index.css` last and its rules win every equal-specificity tie — a `border-radius` there
would override every component's own corner radius the moment it receives keyboard focus, visibly
snapping the session tape's roving tab stop. The rule keeps its `outline`/`outline-offset`, just no
`border-radius`.

### A step's flag is a small, per-row marker naming the finding — for the two finding shapes the server covers, deliberately not every one

Mockup parity item #17: the mockup shows a flag on the exact tape row a finding is about, not only
the session-level chip bar `FindingChips` already renders. `SessionTapeStep.findings` is served for
every step (empty for the overwhelming majority — only `AecoPostMortem.Api.SessionTapeStepFindingLookup`'s
two covered shapes, a hook failure and a tool-failure rate, ever populate it; see
`AecoPostMortem.Api/CLAUDE.md`'s own remarks for which eight checks are left uncovered and why),
and `Tape.tsx`'s `StepFlag` renders nothing at all when a step's own list is empty — the same "no
glyph unless there is a real reason for one" discipline `StepGlyph`'s per-kind icons already follow.

`SessionTapeStep.findings` is declared `?:` (optional) on the TypeScript side even though the server
always sends it (never omits, empty array for an unflagged step) — a deliberate divergence from this
project's usual "mirror the server's `required`ness exactly" convention. Two other engineers'
worktrees touch `Tape.test.tsx`'s many literal step-fixture object literals in this same round
(mockup parity items #13 and #16); making the field required would force an edit to every one of
those fixtures for a field their own tests never read. `step.findings ?? []` at the one read site
(`StepFlag`) treats a missing field identically to a served empty array, so nothing about rendering
depends on the distinction — the deviation costs correctness nothing and meaningfully shrinks the
diff three concurrent worktrees would otherwise conflict over.

Verified against the live 35-session reference corpus: a real `GET /api/sessions/{id}` request for a
session in the dominant repository (`supahfly27/UpFront`) served 20 real flagged steps out of 2,249 —
the real `sessionStart` hook failure and every real failed `view`/`grep`/`glob` call in that session —
confirmed both in the raw JSON response and in a real browser's own accessibility tree (`role="img"`,
`aria-label="Flagged: …"`) on the matching row, with every co-located, non-matching row correctly
carrying no flag at all.

### The pager is client-side; the real corpus size is what decided it, not an assumption

The live 35-session reference corpus serves 297 ranked findings for its dominant repository, and the
whole `DigestEnvelope` (already fetched in one shot by `useDigest`, unchanged by this task) is about
1.3 MB — well within a single response. `Pager` slices the already-served `rankedFindings` array
client-side at a fixed `PAGE_SIZE = 25` (`DigestPage.tsx`) rather than adding a server-side
offset/limit wire contract: a server-side page would need a new `total` field and a new pagination
parameter pair, and a second surface where the served count could disagree with what the page shows —
not justified at this corpus' measured scale. Deliberately deferred, not forgotten: if a real corpus
ever grows enough that shipping the whole ranked list in one response becomes materially slow, that is
the point to revisit this decision, not before. `inferredFindings` (the separate "Judgment calls"
section) is not paginated at all — it is typically small and, per FR-48, deliberately never ranked or
otherwise treated the same way as `rankedFindings`.

`page` resets to 1 whenever the date-range filter changes (`DigestPage.tsx`'s `applyRange`): a new
range re-scopes the whole analysis server-side (`AecoPostMortem.Api/CLAUDE.md`'s "A date-range filter
re-scopes the whole analysis"), so the previous range's page position has no meaning against the new
list. `currentPage` is additionally clamped (`Math.min(page, pageCount)`) before rendering, a second,
structural guarantee against ever indexing past the end of what is actually being served — the same
"never serve a number the data doesn't support" discipline this app follows everywhere else
(`Masthead`'s own "nothing on this page counts anything" rule, applied here to an index rather than a
count).

### `DateRangeFilter`'s two `<input type="date">` fields use explicit `htmlFor`/`id`, not implicit label wrapping — a real gap a live-browser check caught

The first version wrapped each input in a bare `<label>From<input/></label>` / `<label>To<input/>
</label>` — valid HTML, and `@testing-library/react`'s `getByLabelText` resolved both correctly under
jsdom, so the component's own test suite passed. A real Chrome accessibility-tree read during this
task's mandatory real-browser verification pass showed the second field losing its accessible name
entirely (`label` with no computed text) while the first kept "From" — jsdom's implicit-label
computation and Chrome's own did not agree for two sibling implicit labels in this exact structure.
Rather than debug why the two engines diverge, `From`/`To` now carry `id="date-range-filter-from"`/
`"date-range-filter-to"` and their `<label>`s an explicit `htmlFor` pointing at the same id — the
unambiguous, universally-supported association every accessibility API agrees on, still nested inside
the label the same way (so nothing about the visible layout changed). Re-verified in the same real
browser after the fix: both fields now resolve a real accessible name. This is the kind of gap
`superpowers:verification-before-completion`'s own "a green test suite is not sufficient evidence" rule
exists to catch — a passing `npm test` run alone would have shipped the broken field.

### A native `<input type="date">` cannot be filled by typing literal `yyyy-mm-dd` text via synthetic keystrokes

Confirmed while exercising this task's own real-browser check: a browser automation tool's `type`
action sending the literal characters `2026-04-28` (or the digit-only `04282026`) into a real Chrome
date input leaves its `.value` empty — a native date input's keyboard-entry model is locale- and
segment-based, not a plain text field, and neither of those two encodings happened to match what this
particular Chrome build's date-entry parser accepted from synthetic key events. This is a real
limitation of the *testing tool's* keystroke simulation, not of `DateRangeFilter` itself: setting the
input's value through the same native property setter React's own controlled-input machinery uses
(`Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set`) followed by a real
`input` event — functionally identical to what a real operator's mouse-driven date picker or correctly
locale-formatted keyboard entry produces — then clicking the real `Apply` button end to end, confirmed
the full path works: the served finding count, session count, and silent-check populations all moved
to the exact figures a direct `curl` request for the identical range already established server-side.
Worth knowing for whoever next automates a browser check against this component: don't spend time
debugging `type` against a `date` input before trying this workaround, and don't read the earlier
failure as evidence the component is broken.

### Code review round: an inverted range must never reach the server, and a filter change must never blank the page

Two independent code reviews of the pager & date-range filter task's own first pass (an opus
subagent, and separately the coordinator's own pass) converged on the same real gaps, one of which
was structurally identical to a failure this app had already ruled out once before (`AppStateBanner`'s
own "the API is not running" state) resurfacing in a new place:

**An inverted range was an unrecoverable dead end that blamed the wrong thing.** `From` after `To`,
Apply → the server correctly answers 400 (`AecoPostMortem.Api/CLAUDE.md`'s "An inverted range... is a
caller error" decision) — but nothing on the client stopped that request from being sent, and
`fetchDigest` collapsed every non-2xx response into the same generic thrown `Error` `useAppState`'s
own network-failure case throws. `useDigest`'s `'error'` state and `DigestPage`'s early-return error
branch (`Could not reach the local API. Is aecopostmortem serve running?`) are correct for a genuinely
unreachable API, but false for a 400 the server answered with a real, specific reason — and the early
return unmounts `DateRangeFilter` itself, so the operator had no control left to correct the mistake
except a full page reload. The fix is entirely client-side, at the point closest to the mistake:
`DateRangeFilter.submit` (above) now refuses to call `onApply` at all for an inverted range, so the
400-producing request is never sent — confirmed with `read_network_requests` during this round's own
real-browser check: submitting an inverted range produces zero `/api/digest` requests. This is
narrower than distinguishing a 4xx from a network failure in `useDigest` (a real, separate
improvement `fetchDigest`/`useDigest` could still make for other 4xx cases this task did not create),
but it closes the one path this task's own feature made newly, easily reachable.

**A date range matching zero sessions rendered the wrong one of this app's own three designed
states — a genuine fourth one, not a display bug.** See `AecoPostMortem.Api/CLAUDE.md`'s matching
entry for why the server still honestly serves `DigestState.Analyzed` and non-empty `SilentChecks`
(every check population `0`) for this case — a real, checked fact, not a bug to suppress server-side.
`DigestPage.tsx` is where the fourth sentence belongs, the same place the other three
(`NotYetAnalyzed`/`Incomplete`/"found nothing") already live: `rangeActive` (`range.from !== null ||
range.to !== null`) and `noSessionsInRange` (`rangeActive && scope.sessionIds.length === 0`) gate a
new sentence — "No sessions in the selected repository started in the applied date range — nothing
was looked at, which is a different fact from every check running clean." — and suppress the ranked
list, `Pager`, "Judgment calls" and `CleanChecks` sections together, rather than letting `CleanChecks`
render ten "0 found · 0 checked" cards under a heading whose own copy warns against exactly that
conflation. Reachable only through an *active* filter — an unfiltered digest with a truly empty
repository scope is a different, pre-existing case (an empty store) this task does not touch.
Verified in a real browser against the live corpus: `from=2026-01-01&to=2026-01-31` (zero of the
dominant repository's 25 sessions fall in January) rendered exactly this sentence, no "found
nothing", no clean-checks grid, `Sessions 35` still honest on the corpus-wide masthead, and the
footer's own new sentence read "Ranked over 0 of 35 sessions, 1 Jan 2026 to 31 Jan 2026".

**Applying a filter used to blank the whole page.** `useDigest` previously reported bare
`{status: 'loading'}` for every fetch, including a re-fetch triggered by a changed range —
`DigestPage`'s loading branch renders only a bare heading, so the masthead, the repository selector
and `DateRangeFilter` itself all unmounted mid-interaction while the new range's own request was in
flight. `useDigest`'s `'loaded'` state now carries `isRefetching: boolean`; a re-fetch after the first
successful load keeps the previous `digest` attached with `isRefetching: true` rather than reverting
to `'loading'`, so `DigestPage` renders a small `<p role="status">Updating…</p>` (an implicit
`aria-live="polite"` region, so it announces without stealing focus the way `role="alert"` would)
instead of unmounting anything. Verified in a real browser: applying a range while the previous
digest is on screen keeps the masthead, `RepositorySelector` and `DateRangeFilter` all mounted and
interactive throughout.

**Pager focus/announcement was below the bar this app's own tape keyboard model already set.**
Landing on the last page disables the exact "Next" button just clicked (the same for "Previous" into
page 1), which drops keyboard focus to `<body>` with nothing announced — a real accessibility gap for
a codebase that built `session/Tape.tsx`'s own roving tab stop and `aria-activedescendant` wiring.
`Pager`'s own status text is now `role="status"` and a real focus target; every page change after
first mount moves focus onto it (skipped on mount itself, via a `hasMounted` ref, since nothing has
navigated yet). Verified in a real browser: clicking "Next" ten times in a row (with a real re-render
between each, not a synchronous loop that would all read the same stale `page` closure) lands on the
last page with `Next` genuinely disabled and focus confirmed on the status paragraph, never `<body>`.

**Minor, also fixed in the same round**: `useDigest`'s dependency array now closes over two plain
scalar parameters (`from`, `to`) rather than a `range` object — the object literal `DigestPage` used
to construct fresh on every render made `react-hooks/exhaustive-deps` correctly flag a missing
`range` dependency that could never be added without re-fetching every render; two scalars sidestep
it structurally, the same shape `useRulesInventory` already uses for its own `versionHash` parameter.
`npm run lint` (`oxlint`) reports exactly the same 2 pre-existing warnings (`session/TapeMinimap.tsx`,
an unrelated file this task never touches) before and after this whole round.

## Playbook — adding a route

1. Add the page component under `src/routes/`. A surface with no real content yet renders
   `<ComingSoon surface="…" story="S-NN" release="Release N" />` — do not invent a second
   placeholder shape.
2. Register the route in `App.tsx`, under the shared `AppShell` element so navigation and the
   app-state banner stay present.
3. Add its nav link in `AppShell.tsx` if it is a primary surface (four today: Digest, Rules
   Inventory, Monitor, plus the session view reachable only by link).
4. Cover it in `App.routing.test.tsx` — the route resolves, and (if still a placeholder) the
   correct story/release text is present. A route that fetches needs its own arm in that file's
   shared `fetch` stub, which throws on an unexpected URL by design.

## Status

Routing (React Router: `App.tsx`, `AppShell.tsx`), the two zero-data states plus the "API
unreachable" state (`AppStateBanner.tsx`, `api/useAppState.ts`, `api/appState.ts`), and all three
surfaces reachable — S-48.

The Rules Inventory (`routes/RulesInventoryPage.tsx`, `api/rulesInventory.ts`,
`api/useRulesInventory.ts`) is real as of S-22 (issue #35, FR-40) — it was the last of the three
surfaces still rendering `ComingSoon`. It shows exactly one rule-set version at a time and names it,
lists every extracted statement with exactly one of the four statuses (a reason travelling with
"Not checkable"), its source file, its carrying sessions and its in-force window, keeps a retired
rule visible with its adherence frozen at the removal date, and states both designed "no rules
found" states rather than rendering an empty table. `/api/rules-inventory` is now wired to real
corpus-wide extraction (`AecoPostMortem.Api/CLAUDE.md`, `ApiHost.GetRulesInventory`) — a real browser
today renders this repository's own `CLAUDE.md`/`AGENTS.md` rules with no change to any file in this
directory, the same promise `DigestPage` fulfilled first. `Watched` can now appear for real — the
server resolves a `PreferAOverB` statement's operands against a real `ToolInvocationShape` corpus
(`AecoPostMortem.Api/CLAUDE.md`'s own remarks on `RulesInventoryClassifier` say what it does and does
not attempt) — but no rule statement in the live 35-session reference corpus happens to be phrased
narrowly enough on both sides to reach it yet; every extracted statement today still renders
"Checkable — not yet built" or "Not a rule". "Not checkable" (with a reason) still never appears.

Mockup parity item #7 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`, Part 3
"Violations" column) added the table's own "Violations" column: a Watched row's real count no longer
lives only on the Digest, one hop away. `api/rulesInventory.ts`'s `RuleViolationCountEnvelope`
(`counted`/`notAvailable`) mirrors `AecoPostMortem.Api.RuleViolationCountEnvelope` verbatim, and
`ViolationCountCell` (`routes/RulesInventoryPage.tsx`) renders three visually distinct states: a real
number (`data-violation="counted"`, including a real, checked zero), a stated "No check built"
(`data-violation="no-built-check"`, a Watched row whose matched shape — `PreferAOverB` today — has no
Finding-producing orchestrator), and a plain dash (`data-violation="not-applicable"`) for every row
that isn't Watched at all. This was verified against the live 35-session reference corpus to be a
real, non-trivial backend gap, not the pure-frontend read the prioritisation doc's own "S–M effort"
estimate risked assuming: `ApiHost.GetRulesInventory` (`AecoPostMortem.Api/CLAUDE.md`) had never run
any of the four piece-3 check orchestrators before this story, only classified statements. A real
browser today renders the dominant repository's one Watched row (`NeverReadPath`) with its real,
served count — no client-side computation, the same "this app derives nothing" discipline `Masthead`
and `AdherenceFigureBlock` already follow.

The Process Digest (`routes/DigestPage.tsx`, `api/digest.ts`, `api/useDigest.ts`, `digest/`) has its
real content — S-36 (issue #44) built the masthead/ranking contract and, in a follow-up, the rendered
masthead (`digest/Masthead.tsx`), the prominent `sessionsAffected` count on each row, and the three
designed states' distinct wording; S-54 (issue #45) built row expansion, the recurrence strip, the
repository scope contract and this route's actual UI. `/api/digest` is now wired to real derived data
(`ApiHost.GetDigest`, `AecoPostMortem.Api/CLAUDE.md`) — a real browser today renders the live corpus'
masthead and ranked findings with no change to any file in this directory.

Mockup parity item #2 (the per-finding session strip) added `digest/SessionStrip.tsx` and
`RepositoryScopeEnvelope.sessionIds`: every ranked row now renders a small lit/unlit cell bar,
visible while collapsed, positioned against `masthead.repositoryScope.sessionIds` — see "The
per-finding session strip is scoped to the selected repository, not the whole corpus" above for why
that field, not `sessionCount`, is the right denominator. This closed a real prerequisite gap this
story's own brainstorming pass found before writing any frontend code: the item's original
prioritisation-doc estimate (Effort 4/5, "pure frontend, no backend change") assumed
`RecurrenceEnvelope.occurrences` alone was enough, but neither `MastheadEnvelope` nor
`DigestEnvelope` exposed any ordered, full session list before this change — only a bare
`sessionCount` — so there was no way to answer "which of the N sessions" at all. Verified against
the live 35-session, two-repository reference corpus in a real browser: the strip renders on every
row, cell count matches the selected repository's own session count, and lit positions match each
finding's `sessionsAffected` figure.

FR-48 (issue #52, S-42) closed a real gap: `DigestEnvelope.inferredFindings` had been served by
`ApiHost.GetDigest` for a prior story but was never declared on `api/digest.ts`'s own type, so it
was silently dropped on arrival (see "A missing wire field can hide even after its server-side story
ships" above). `DigestPage` now renders it as its own "Judgment calls" section, below the ranked
list and rendered only when the list is non-empty, each entry reusing `FindingRow` with
`variant="unranked"` so a hypothesis is never shown with the same rank-metric column a measured
finding gets. Verified against the live 35-session reference corpus: `inferredFindings` is
structurally empty today — `ApiHost.GetDigest` wires no check that produces a `Provenance.Inferred`
finding yet (`ToolFailureClusterFinding`, the one that would, is documented there as "not run here"
pending a mandating rule) — an honest empty result, the same "mechanism real, corpus doesn't happen
to exercise it yet" pattern this project has hit before. The rendering path itself was proven two
ways: `DigestPage.test.tsx`'s own synthetic-finding test, and a real browser against the live host
with `window.fetch` patched to inject one synthetic inferred finding into the real response —
confirmed rendering the section heading, the dashed violet `INFERRED` badge, the evidence, the
recurrence strip and the "No suggestion is offered." absent state, with no leading session-count
column.

The "Every check ran and found nothing." message (above) now also requires
`inferredFindings.length === 0`, not only `rankedFindings.length === 0`: `Digest.cs` derives
`DigestState.Analyzed` from whether any check ran, independent of how many findings resulted in
which list, so a corpus where every check that ran happened to produce only hypotheses would leave
`rankedFindings` empty while `inferredFindings` is not — without this guard the page would state
"found nothing" directly above a populated "Judgment calls" section.

Mockup parity item #5 (the finding headline) closed the "Finding headline" gap the mockup-parity
discovery doc named: `FindingRow`'s collapsed summary now shows `finding.headline` — a full written
sentence naming the problem, e.g. "The sessionStart hook failed in 25 of 25 sessions." or "view
failed 126 of 4460 calls (2.8%) across 20 sessions." — never the bare `finding.recurrence.key` (a raw
tool name or a rule statement's own text) it showed before. All 11 `Finding`-producing check
orchestrators in `AecoPostMortem.Findings` (the 10 named in the prioritisation doc's own "touches all
10 finding builders" estimate, plus `ToolFailureClusterFinding`, which the doc's own count missed —
see `AecoPostMortem.Findings/CLAUDE.md`) now build a real, grounded headline sentence; see that file
for the exact wording per check kind. Verified against the live 35-session reference corpus in a real
browser: every check kind the corpus actually exercises (hook failures, interruption load, failed
tool calls, repeated file reads, aborted turns, phase churn, and the one real `NeverReadPath`
violation) renders its own real sentence — `BannedToolFinding`/`UseAAfterBFinding`/
`AlwaysPassParamFinding` still produce zero findings on this corpus scope (a pre-existing, documented
gap unrelated to this change, `AecoPostMortem.Api/CLAUDE.md`'s own status notes), so their headline
text is proven only at the unit level, the same "mechanism real, corpus doesn't happen to exercise it
yet" pattern those checks already carry. `FindingChips` (`routes/SessionPage.tsx`) was left
unchanged, on purpose — see "A finding chip's label is still `finding.recurrence.key`" above.

Mockup parity item #6 (FR-42, issue #46, `docs/product-superpowers/discovery/2026-08-21-ui-mockup-
parity.md`) added `digest/CleanChecks.tsx` and `api/digest.ts`'s `SilentCheckEnvelope`/
`DigestEnvelope.silentChecks`: "Checks that found nothing", mounted below the "Judgment calls"
section, rendering nothing when `digest.silentChecks` is empty — confirming this session's own
brainstorming pass ahead of writing code, the item's real gap was much smaller than the
prioritisation doc's "M effort" estimate assumed. `SilentCheckEnvelope.From`
(`AecoPostMortem.Api/CLAUDE.md`) needed zero changes and `ApiHost.GetDigest` already built the exact
`CheckRegistry` this surface needs — the only real gaps were `ProcessDigest` dropping that registry
after computing `DigestState` (`AecoPostMortem.Findings/CLAUDE.md`) and `CheckRegistryEntry` carrying
no `Provenance` for the mockup's own badge per card, both closed in the same change as this file's.
Verified against the live 35-session reference corpus in a real browser: three of `ApiHost.GetDigest`'s
ten checks render as clean cards (`Banned Tool Used`, `Use A After B`, `Always Pass Param`, each
`0 found · 24 checked`, `DERIVED`) and `never-read-path-used` — the one piece-3 adherence check with
a real violation on this corpus — correctly does not appear among them.

Mockup parity item #21 ("Digest has no way to click through to a session") made `RecurrenceStrip.tsx`'s
session ids real `<Link to={`/sessions/${sessionId}`}>`s instead of plain text — the smallest of the
mockup-parity items, scoped to one component on purpose since two other engineers were mid-flight on
`SessionStrip.tsx`/`Masthead.tsx`/`Tape.tsx`/`SessionPage.tsx`/`RulesInventoryPage.tsx`/`DigestPage.tsx`
in parallel worktrees. `SessionStrip.tsx`'s cells were deliberately left as plain decorative markers
(one `role="img"` with a single aria-label for the whole strip, not per-cell links) — turning those
into links is a materially different redesign this item did not ask for. The one real ripple: once
`RecurrenceStrip` renders a `<Link>`, every test that mounts it (directly, or indirectly through
`FindingRow`/`DigestPage`, once a row is expanded) needs a `MemoryRouter` ancestor or `react-router`
throws `Cannot destructure property 'basename' of 'React.useContext(...)' as it is null` — so
`FindingRow.test.tsx` and `DigestPage.test.tsx` also picked up a `MemoryRouter` wrapper around every
`render(...)` call in this change, with no change to either component's own source.

Mockup parity item #15 ("Rule coverage bar") replaced the masthead's plain "Rules not yet analysed"
text with a real bar once `AecoPostMortem.Findings.RuleCoverageStatus` carries a real four-way
breakdown (`AecoPostMortem.Findings/CLAUDE.md`'s own remarks) — `digest/Masthead.tsx`'s Rule coverage
cell now branches on `masthead.ruleCoverage.state`, and `digest/RuleCoverageBar.tsx` renders the
`'analyzed'` case as a real proportional four-color bar (watched/checkable-not-built/
normative-but-unobservable/not-a-rule) with a legend, ported from the mockup's own `.covbar`/`.covkey`
with this app's tokens. This diverges from the mockup's own layout deliberately: the mockup's bar is
proportional only over "actual rules" (excluding "not a rule," stated as separate plain text), while
this bar is proportional over all four statuses, since "not a rule" is often the corpus' largest
bucket (`RulesInventoryPage`'s own "No status count is styled as a problem count" note) and a
three-segment bar would give it no visual representation at all. Verified against the live
35-session reference corpus via a real `serve` + `GET /api/digest` request: the dominant repository's
own masthead served a real `{watched:1, checkableNotYetBuilt:6, notCheckable:0, notARule:10,
total:17}` breakdown, matching `GET /api/rules-inventory`'s own `statusCounts` for the same version
exactly — no client-side computation, the same "this app derives nothing" discipline `Masthead`/
`AdherenceFigureBlock` already follow.

The pager & date-range filter task added `digest/DateRangeFilter.tsx` and `digest/Pager.tsx` to the
ranked-findings list: `DateRangeFilter` submits an optional `from`/`to` (both plain `yyyy-MM-dd`
dates) that `useDigest` re-fetches against — server-side, re-scoping the whole analysis, per the
non-obvious decision above and `AecoPostMortem.Api/CLAUDE.md`'s own matching entry — and `Pager`
slices the resulting `rankedFindings` client-side at 25 per page, rendering nothing at all when
everything already fits on one page. Verified against the live 35-session reference corpus in a real
browser: the dominant repository's default (unfiltered) digest renders "Page 1 of 12" over its real
297 ranked findings; applying `from=2026-04-28&to=2026-05-10` (a real sub-range of that repository's
own 25-session span) re-renders the top finding's own count from "25 of 25 sessions" to "16 of 16
sessions" and the silent-checks section's own population from "24 checked" to "15 checked" — both
matching a direct `GET /api/digest?from=…&to=…` request's own JSON exactly — and `Clear` restores the
unfiltered digest. Server-side offset/limit pagination and a combined filter-plus-pager query-string
round trip (deep-linkable filtered/paged URLs) are both explicitly deferred, not built here — see the
non-obvious decision above and this task's own PR description for why.

Two independent code reviews of that first pass (an opus subagent, and separately the coordinator's
own pass) each caught real gaps in the same round — see "Code review round: an inverted range must
never reach the server..." above for the fixes and their own real-browser verification: an inverted
range now never reaches the server at all (confirmed via `read_network_requests` — zero `/api/digest`
calls for a submitted inverted range), a date range matching zero sessions renders a genuine fourth
designed state rather than a false "found nothing", applying a filter no longer blanks the page
(`isRefetching`), and the pager moves focus to its own live-region status on every page change so a
disabled "Next"/"Previous" never drops focus to `<body>`. Re-verified end to end in a real browser
against the live corpus after these fixes: applying `from=2026-04-28&to=2026-05-10` again produced
"16 of 16 sessions"/"15 checked" and the new footer sentence "Ranked over 16 of 35 sessions, 28 Apr
2026 to 10 May 2026"; paging to the last page (12) with a real re-render between each click left focus
on the status paragraph, not `<body>`; and `from=2026-01-01&to=2026-01-31` (zero matching sessions)
rendered the new honest sentence with no ranked list, pager or clean-checks grid.

The session view (`routes/SessionPage.tsx`, `api/session.ts`, `api/useSession.ts`,
`session/Tape.tsx`) is real as of S-08 (FR-21, part 1 of 3, issue #15), S-52 (FR-21, part 2 of 3,
issue #16) and S-53 (FR-21, part 3 of 3, issue #17): the masthead and the time-ordered tape,
reading `GET /api/sessions/{sessionId}`. The tape virtualises at scale and is fully
keyboard-navigable (`session/Tape.tsx`) — a single roving tab stop moves a `selectedIndex` via
Arrow/Home/End/PageUp/PageDown, confirmed with Enter/Space — and each row also carries a
`tabIndex={-1}` button as a mouse click target, so both input methods converge on the same
`onSelectStep` callback `SessionPage` passes down. Selecting a step drives the finding chip row's
neighbour, the inspector (`Inspector`, `SelectedStepInspector`,
`DetailPanel`/`ThinkingPanel`/`RawPanel`, `api/useStepEvidence.ts`): Detail, Thinking and Raw as
three named tabs, Raw always able to state a skipped-at-ingest step rather than render blank, and
an explicit "pick a step" message when nothing is selected. The finding chip row (`FindingChips`)
reads `envelope.findings` off the same fetch — no second request — and three non-happy states are
distinct from each other and from a load failure: mid-ingest (`ingestIncomplete`), reconstruction
failure (`reconstructionFailed`, states what was skipped) and the unreachable-API/404 case S-08
already built; `NonFinalState` replaces the chip row, tape and inspector entirely for the two
non-`complete` states rather than rendering any of them as provisional. A real browser today sees a
real chip row's *shape* but an empty one — nothing wires a live `Finding` list into
`ApiHost.GetSession` yet (`AecoPostMortem.Api/CLAUDE.md`'s own status note) — while the inspector's
Thinking/Raw tabs are fully live against any ingested store, since they read `RawEvent` rows
directly rather than a not-yet-wired derived pipeline.

FR-23 (S-10, issue #19) closed the Thinking tab's own gap: the empty state for provider-encrypted
reasoning now names the model (when the raw event carried one) and renders the session's own
measured readable share per model (`ReadabilityByModel` in `SessionPage.tsx`, `readabilityByModel`
in `api/session.ts`) — no new lane rendering, no subagent-lane change (that is S-09/FR-22's and
issue #18's job, not touched here), and no client-side computation: the figure travels from
`GET /api/sessions/{sessionId}/steps/{stepId}?kind=` already, unchanged endpoint shape otherwise.

FR-25 (S-12, issue #21) added `SessionTapeStep.pluginName`/`.pluginVersion` — a `'skill'` step's
plugin and version, rendered next to its name (`session-tape__plugin`, shown only when
`pluginName` is non-null; `formatPlugin` in `session/Tape.tsx` — the tape's own row rendering,
after S-53's extraction — joins the version in only when both are present). A subagent's skill
already carried `ownerKind`/`agentId` correctly since S-08 (the
same generic attribution every step kind gets) — this story only closed the plugin/version gap, it
added no new lane-rendering: grouping steps visually by lane was S-09's job (FR-22), landed below.

FR-22 (S-09, issue #18) closed that gap: subagent lanes and the report each one actually produced.
`session/Tape.tsx` gained a per-row lane marker (`data-owner-kind`, `data-agent-id`,
`data-agent-lane`) rather than a contiguous block per agent — see the non-obvious decision below for
why. `SessionPage.tsx`'s new `AgentLanes` component renders one entry per `envelope.lanes`
(`SessionAgentLane`), each showing the subagent's identity, how it finished, and
`SubagentOutputPanel`'s rendering of its `SubagentOutputEnvelope` — the last `assistant.message`
under that subagent's own `agentId` (`present`), an explicit "no output was recorded" state
(`notRecorded`, never a fall-back to the parent's truncated `read_agent` stub), or the failure and
its recorded error (`failed`) when `Data.Execution.Agent.Outcome` is `Failed`. Resolution happens
server-side (`AecoPostMortem.Api.SubagentOutputLookup`, see `AecoPostMortem.Api/CLAUDE.md`) — this
project only renders whichever of the three shapes the server already decided.

### A subagent's lane is a per-row marker, not a contiguous block

The mockup this story could have followed groups a subagent's steps under one `<div class="lane">`
header, on the assumption that a subagent's steps arrive as one contiguous run. FR-22's own
Scenario 5 asks for "concurrent subagents," and the tape is one flat, wall-clock-ordered list across
every thread (`SessionRecording.Build`, `AecoPostMortem.Findings/CLAUDE.md`) — two subagents running
at once interleave their steps in time rather than each occupying one uninterrupted block. A
block-grouping renderer would either misattribute an interleaved row to the wrong block or have to
detect and re-sort around the interleaving, which is not this story's job (the tape's own ordering
is FR-21's, S-08, and stays untouched here). `session/Tape.tsx` instead marks every row
independently: `data-owner-kind` (`'main'`/`'agent'`) distinguishes the main thread outright, and for
an agent-owned row, `laneIndex` hashes its own `agentId` into one of 8 colours
(`--session-tape-lane`, a CSS custom property consumed by `Tape.css`'s `hsl()` border-left rule) —
the same colour every time that `agentId` appears, however its rows are interleaved with any other
agent's or the main thread's. `Tape.test.tsx`'s two lane tests prove both halves: a main-thread row
and an agent-owned row carry distinct `data-owner-kind` values, and two different concurrent
subagents' rows carry distinct `data-agent-lane` values while the same subagent's own two
non-contiguous rows share one.

### `AgentLanes` needs no lane list to correlate against the tape's own rows

`SessionAgentLane` (the lane's identity, outcome and output) and `SessionTapeStep.agentId` (which
row belongs to which agent) are two independent reads off the same `envelope` — `Tape` never
receives `envelope.lanes` at all, and `AgentLanes` never receives `envelope.steps`. Nothing joins
them client-side: a lane's own `agentId` is the same string a step's `agentId` already carries, so
a reader can already tell which rows belong to which lane by eye (matching border colour) without
this app computing that correspondence itself. Wiring the two together into one combined view (e.g.
scrolling the tape to a lane's rows on click) is left for a later story — this one renders both,
distinctly, and stops there.

FR-39 (S-35, issue #43) added the Monitor comparison's own block (`digest/MonitorComparisonBlock.tsx`,
`api/monitor.ts`): a `MonitorComparisonEnvelope` renders as two `AdherenceFigureBlock`s, Before and
After, each preceded by its own session count sharing the percentage's own CSS class and
`data-emphasis` marker (see "The Monitor comparison's session count..." above). `/api/monitor-
comparison` was served for real (piece 4, `AecoPostMortem.Api/CLAUDE.md`'s own status note), but for
a full round through this project's stories, nothing mounted it — `MonitorComparisonBlock.test.tsx`
exercised it directly, and `fetchMonitorComparison` had no caller. `MonitorComparisonBlock.test.tsx`
still exercises it directly against the reference corpus's own measured 41.8% → 71.7% edit (3
sessions, then 4) — a unit-level fixture, not the live corpus (see below for the real numbers).

**The Monitor comparison's missing door task** closed that gap: `routes/MonitorPage.tsx` and
`api/useMonitorComparison.ts` are the real page and hook, `/monitor` is the fourth routed surface and
`AppShell`'s fourth nav link (see this file's own remarks on `App.tsx`/`AppShell.tsx` above for why a
whole new page earns the slot rather than a section on the Digest or the Rules Inventory). See the
non-obvious decision below, "The Monitor's two refusals are resolved on the client, not the server,"
for how the page tells apart the endpoint's two structurally different 404s with no server change.

Verified against the live 35-session reference corpus, both via direct `curl` against a real
`aecopostmortem serve --port 5110` and in a real browser: the dominant repository's own rule-set
history carries 23 versions (22 adjacent pairs). 20 of the 22 answer a real 200 with a genuine
adherence comparison (a real `PreferAOverB` statement — `` "Prefer querying codebase-memory-mcp over
Glob/Grep/Read..." `` — resolved for both sides, including one pair where the *after* side's own
percentage is honestly `null`, no calls observed). The other 2 both answer 404 for the
"no comparable rule" reason, confirmed by directly requesting each pair and separately confirming
both are adjacent by array position — the exact same 2 `AecoPostMortem.Rules/CLAUDE.md`'s own
`RuleSetVersionAdjacency` remarks name, now against a corpus that has grown from 22 to 23 versions
since PR #112 measured it (17 → 20 succeeding is corpus growth, not a behaviour change). A real
browser at `/monitor` renders: the default pair (the two most recent versions) as a real comparison;
the same real 2-of-22 "no comparable rule" pair, selected via the two dropdowns, as that exact
sentence; and a deliberately non-adjacent pair (picked freely from the two dropdowns) as "not
adjacent," with `read_network_requests` confirming zero `/api/monitor-comparison` calls for that
last case — the client-side adjacency check works as designed, never asking the server a question it
can already answer itself. The third designed state (`MonitorPage`'s "no repository is recorded"
message, added in code review round 2) has no real-corpus case to trigger it against — every session
in the live reference corpus resolves a repository — so it is verified only at the unit level
(`MonitorPage.test.tsx`'s dedicated fixture), the same "mechanism real, corpus doesn't happen to
exercise it yet" pattern this project has hit before for other genuinely rare states.

Mockup parity item #14 added the session masthead's own real wall-clock start→end range: `Masthead`
gained a new "Wall clock" field (`formatWallClockRange`, above `Elapsed`), reading `startedAt`/
`endedAt` off `SessionMasthead` — both now real fields the server was already computing but never
serving (`AecoPostMortem.Api/CLAUDE.md`'s matching note). No client-side computation beyond
formatting: this app derives nothing, the same discipline `Masthead`/`AdherenceFigureBlock` already
follow elsewhere. Verified against the live 35-session reference corpus via a real
`GET /api/sessions/{sessionId}` request: a completed session serves a real start/end pair matching
its own `elapsedMs`, and a still-recording session serves a real start with `endedAt` honestly
`null`.

Mockup parity item #11 added the session masthead's own methodology footer
(`session/MethodologyFooter.tsx`), mounted at the very bottom of `LoadedSession` — the session
surface's sibling to the Digest's own `digest/MethodologyFooter.tsx` (item #9). It states what was
measured (turns, tool calls, subagents, skill invocations, the recorded date), all read straight off
`SessionMasthead` with no new fetch and no recomputation; that this app's rule findings are
tool-choice checks, not code-content checks; and general, always-true context for how the Thinking
tab's readable-vs-encrypted split is measured per model, once a step is selected — deliberately no
live percentage up front, since `readabilityByModel` is served per step
(`api/session.ts`'s `StepEvidenceEnvelope`) rather than carried on `SessionMasthead`, and eagerly
fetching it for the footer would need a fetch this story does not call for. Verified with the full
`npm test` (128 tests, 17 files) and `npm run build` green.

Mockup parity item #17 added a small per-row flag: `session/Tape.tsx`'s `StepFlag` renders a
`role="img"` glyph on the specific tape row a finding is unambiguously about, reading the new
`SessionTapeStep.findings` field (`api/session.ts`) the server now serves for two finding shapes —
see `AecoPostMortem.Api/CLAUDE.md`'s own remarks on `SessionTapeStepFindingLookup` for the full
scoping reasoning and which finding-producing checks were deliberately left uncovered. This is a
narrow slice, not a general "attach any finding to any step" mechanism: only a hook failure and a
tool-failure rate can be matched to a specific step with no guessing today. Verified against the live
35-session reference corpus in a real browser: a session in the dominant repository served 20 real
flagged steps (the real `sessionStart` hook failure, every real failed `view`/`grep`/`glob` call),
each carrying its own correct finding headline in its accessible label, with no false flags on any
co-located, non-matching row.

A tool call's own result closed a real gap: the Raw tab showed a call going out
(`tool.execution_start`) but never what came back. `RawPanel` now renders two labeled blocks, "Call"
and "Result", both reading `api/session.ts`'s `RawStepEventEnvelope` union — see
`AecoPostMortem.Api/CLAUDE.md`'s matching non-obvious decision for the real corpus verification
(`tool.execution_complete` carries the full result for every tool call, MCP or not, confirmed against
16,076 real events across the 35-session reference corpus) and why the result reuses that same union
rather than a new type. A call still lacks a recorded result (still running, or the session ended
mid-call) states that fact in its own "Result" block rather than an empty one.

`.inspector__raw-payload` (`SessionPage.css`) gained a `max-height: 24rem`/`overflow-y: auto` bound
(code review): the server deliberately serves a result whole, never truncated
(`AecoPostMortem.Api/CLAUDE.md`'s matching decision), and the real corpus's own measured max
(~43 KB) is small enough that server-side truncation would be the wrong fix for the wrong layer — but
an unbounded block still pushes the rest of the tape off-screen once two such blocks (Call and
Result) can stack in one panel. This is a display-only bound: the full payload is still in the DOM
and still scrollable, never cut, so there is no truncation for an operator to be misled by, only a
scrollbar.

What triggered a hook closed a real gap: a hook row said a hook ran, never what it ran in response
to. `DetailPanel` gained a conditional "Triggered by" row, shown only for a `'hook'` step, reading
the eagerly-served `step.triggeredBy` (`api/session.ts`) — no fetch, the same "already in hand"
discipline the Detail tab already follows for its other five fields. `RawPanel` gained a third
block, `TriggerBlock`, reusing `RawEventBlock`'s identical `.inspector__raw`/`.inspector__raw-payload`
rendering (and its existing `max-height`/`overflow-y` bound) for a hook's own resolved tool name,
arguments and result — see `AecoPostMortem.Api/CLAUDE.md`'s matching non-obvious decision for the
full design (why two separate server-side readers, why "no trigger" is a stated value at both
layers, and the real corpus measurements: 2,992 of 3,027 real `hook.start` events are `postToolUse`
with a real tool trigger, 35 are `sessionStart` with none, and the trigger's own `toolResult` measured
a larger real max — 199,831 characters — than the precedent this task measured against, served whole
for the identical reason). Verified against the live 35-session reference corpus and a real browser —
see that file's own `Status` section for the exact session, step and counts inspected.

Real-browser verification also caught, and the same file's own non-obvious decision documents, a
real pre-existing defect unrelated to this task's own frontend change: a `Hook` step's Raw tab had
never resolved anything against real data (`AecoPostMortem.Api.StepEvidenceLookup` matched the wrong
field), which this task fixed server-side — no change was needed on this side, since this project's
Detail/Raw panels already render whichever value the server resolves rather than computing anything
themselves. Confirmed in a real browser against session `03655527-e563-4df7-a73f-eea0903a1752`: the
`sessionStart` hook step renders the stated-absence text on both tabs, and the `postToolUse` (`skill`)
hook step renders "Triggered by: skill" on Detail and the full tool name, arguments and an
18,574-character real `toolResult` on Raw, scrolling inside `TriggerBlock`'s own bounded block.

Test tooling: `vitest` + `@testing-library/react` + `jsdom`, configured in `vitest.config.ts`
(read instead of `vite.config.ts` when both exist, so the React plugin is duplicated there
rather than shared) and `src/vitest-setup.ts` (jest-dom matchers, and `afterEach(cleanup)` since
`test.globals` is off and testing-library's usual auto-cleanup never registers without it).
`npm test` runs the suite once; `npm run build` still type-checks (`tsc -b`) ahead of `vite build`.
