# web

The React + TypeScript + Vite app: the digest, the session view and the Rules Inventory.

**All frontend commands run from here, never from the repository root** (Repo Rule 3, PRD §3.1).
There is no `package.json` at the repository root, and a containment test fails if one appears.
`scripts/build-web.ps1` is the scripted form of the build; it pushes into this directory rather than
passing `--prefix`, for the same reason.

`dotnet test` does not build this project — that would make the .NET suite depend on Node.
`npm test` (`vitest run`) is this project's own test command; nothing on the .NET side calls it.

## Structure

| File | What it holds |
|---|---|
| `src/App.tsx` | the routes (`/`, `/sessions`, `/sessions/:sessionId`, `/rules`), all under `AppShell`. Router-agnostic on purpose — `main.tsx` supplies `BrowserRouter`, tests supply `MemoryRouter` |
| `src/AppShell.tsx` | the nav to all three surfaces (always reachable, S-48 Scenario 1) plus `AppStateBanner`, above whichever route's `<Outlet />` content is showing |
| `src/AppStateBanner.tsx` | S-48 Scenarios 2 and 3: fetches `/api/app-state` and renders its diagnosis, distinctly per state — no-source-found, empty-store, and a fourth state (unreachable API) neither Gherkin scenario names but a real machine can hit |
| `src/api/appState.ts` | the `AppStateReport`/`AppStateKind` shapes and `fetchAppState`, hand-kept in sync with `AecoPostMortem.Api.AppStateReport` (`src/AecoPostMortem.Api/AppStateReport.cs`) — no generated client exists yet |
| `src/api/useAppState.ts` | the fetch-once-on-mount hook `AppStateBanner` reads; loading renders nothing rather than a message that might not apply a moment later |
| `src/routes/ComingSoon.tsx` | the placeholder a surface with no real content yet renders — naming its own story and release rather than sharing one generic message. **Currently unreferenced**: all three surfaces are built as of S-22. Kept because the playbook below still points a new route at it, so the next one does not invent a second placeholder shape |
| `src/routes/RulesInventoryPage.tsx` | FR-40's real content (S-22, issue #35): one rule-set version's statements, each with exactly one status, its source file, its carrying sessions, its in-force window and its retirement — plus the version scope, the status breakdown and the two designed "no rules found" states. Fetches `/api/rules-inventory` via `useRulesInventory` |
| `src/api/rulesInventory.ts` | the `RulesInventoryEnvelope` shapes and `fetchRulesInventory`, hand-kept in sync with `AecoPostMortem.Api.RulesInventoryEnvelope` (`src/AecoPostMortem.Api/RulesInventoryEnvelope.cs`) — the same no-generated-client gap `api/appState.ts` documents. `VersionParameter` is the query parameter naming which version to render |
| `src/api/useRulesInventory.ts` | the fetch-per-`versionHash` hook `RulesInventoryPage` reads, mirroring `useSession`'s re-fetch-on-change shape rather than `useDigest`'s fetch-once |
| `src/routes/DigestPage.tsx` | FR-41's real content (S-36 + S-54, issues #44/#45): the masthead's repository selector plus the ranked findings, each an expandable `FindingRow`. Fetches `/api/digest` via `useDigest`; loading renders nothing, a failed fetch renders its own `role="alert"` message, the same shape `AppStateBanner` established |
| `src/api/digest.ts` | the `DigestEnvelope`/`FindingEnvelope`/`SuggestionEnvelope`/`RepositoryScopeEnvelope`/`AdherenceFigure` shapes and `fetchDigest`, hand-kept in sync with `AecoPostMortem.Api.DigestEnvelope` (`src/AecoPostMortem.Api/DigestEnvelope.cs`) — no generated client exists yet, the same gap `api/appState.ts` documents |
| `src/api/useDigest.ts` | the fetch-once-on-mount hook `DigestPage` reads, mirroring `useAppState`'s loading/error/loaded shape |
| `src/digest/Masthead.tsx` | FR-41's corpus masthead (S-36, issue #44): sessions, span, repositories, events, tool calls and rule coverage, every figure read straight off `MastheadEnvelope`'s ingest-time counters. Marks itself `data-provisional` mid-ingest and says an empty corpus has no span |
| `src/digest/FindingRow.tsx` | one digest row (Scenario 1, issue #45): collapsed by default; expanding it reveals the quoted evidence, `ProvenanceBadge`, `RecurrenceStrip` and `SuggestionBlock`. The `sessionsAffected` count (S-36) leads the summary at display size and stays visible while collapsed |
| `src/digest/AdherenceFigureBlock.tsx` | FR-33 (S-24, issue #38): the only place in the app that renders an adherence percentage — and it renders the per-operand resolution table and rule-set version with it, so no surface can show the number alone. FR-39 (S-35, issue #43) added `data-emphasis="prominent"` on the percentage span, the marker `MonitorComparisonBlock.tsx`'s own session count shares |
| `src/digest/RecurrenceStrip.tsx` | Scenario 2: names every session a finding touched (`Recurrence.occurrences`), not only the count |
| `src/digest/ProvenanceBadge.tsx` | PRD §3.8's three provenance levels, rendered distinguishably — a `data-provenance` attribute drives a distinct colour per level, alongside the badge's own text label |
| `src/digest/SuggestionBlock.tsx` | Scenario 4: renders `SuggestionEnvelope`'s `present`/`absent` states — an explicit "No suggestion is offered." for `absent`, never a blank area |
| `src/digest/RepositorySelector.tsx` | Scenario 3 / PRD Part 8 Q5: shows the selected repository and offers every available one — the seam for a later cross-repository view, not that view itself |
| `src/api/monitor.ts` | FR-39's `MonitorComparisonEnvelope` shape and `fetchMonitorComparison`, hand-kept in sync with `AecoPostMortem.Api.MonitorComparisonEnvelope` (`src/AecoPostMortem.Api/MonitorComparisonEnvelope.cs`) — reuses `AdherenceFigure` from `digest.ts` and `RuleSetVersionEnvelope` from `rulesInventory.ts` rather than redeclaring either |
| `src/digest/MonitorComparisonBlock.tsx` | FR-39 (S-35, issue #43): renders a `MonitorComparisonEnvelope` as two sides, Before and After, each an `AdherenceFigureBlock` preceded by its own session count at the identical visual weight (`adherence-figure__percentage`'s own class plus `data-emphasis="prominent"`) — Scenario 2's "as visible as the percentage" |
| `src/api/session.ts` | the `SessionEnvelope`/`SessionMasthead`/`SessionTapeStep`/`SessionFindingChip`/`SessionRecordingStatus` shapes and `fetchSession`, hand-kept in sync with `AecoPostMortem.Api.SessionEnvelope` (`src/AecoPostMortem.Api/SessionEnvelope.cs`). FR-21 part 2 of 3 (S-52, issue #16) added `ThinkingEnvelope`/`RawStepEventEnvelope`/`StepEvidenceEnvelope` and `fetchStepEvidence`, mirroring `AecoPostMortem.Api.StepEvidenceEnvelope`; FR-21 part 3 of 3 (S-53, issue #17) added `SessionRecordingStatus` |
| `src/api/useSession.ts` | the fetch-per-`sessionId` hook `SessionPage` reads; loading renders nothing, an error (404 or unreachable API) is one explicit state |
| `src/api/useStepEvidence.ts` | FR-21 part 2 of 3 (S-52, issue #16): the fetch-per-`(sessionId, stepId, kind)` hook the inspector reads once a step is selected, mirroring `useSession`'s loading/error/loaded shape |
| `src/routes/SessionPage.tsx` | FR-21, part 1 of 3 (S-08, issue #15): the Flight Recorder — masthead and time-ordered tape. FR-21, part 2 of 3 (S-52, issue #16) added the finding chip row, step selection (delegated to `session/Tape.tsx`), and the inspector's Detail/Thinking/Raw tabs, with an explicit "pick a step" state when none is selected. FR-21, part 3 of 3 (S-53, issue #17): renders the chip row, tape and inspector only when `envelope.status.kind === 'complete'`; otherwise renders `NonFinalState`, one distinct message per non-happy `SessionRecordingStatus` kind. Reads `sessionId` from the route; no `sessionId` (bare `/sessions`) states "no session selected" rather than reusing `ComingSoon`, since the surface itself is built |
| `src/session/Tape.tsx` | FR-21, part 3 of 3 (S-53, issue #17): the tape itself, moved out of `SessionPage.tsx` — fixed-row-height virtualisation (only the scrolled-to window plus overscan is mounted, proven at the largest measured session scale, 84 turns + 764 tool calls) and full keyboard reachability (a single roving tab stop on the list itself; Arrow/Home/End/PageUp/PageDown move a `selectedIndex` that pulls its row into the mounted window before selecting it, `aria-activedescendant` names it for assistive technology). Reconciled with FR-21 part 2 of 3 (S-52, issue #16)'s step-selection contract: each row's content sits inside a `tabIndex={-1}` button — a click target, never a second tab stop — so `SessionPage`'s inspector gets the same `onSelectStep` callback from a mouse click that it already got from keyboard Enter/Space |
| `src/session/Tape.css` | `Tape.tsx`'s absolute-positioning layout (each mounted row placed by `top: index * rowHeight` inside a spacer-sized scroll container), the `aria-selected` highlight, and the `tabIndex={-1}` row button's own layout |

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

### `/sessions/:sessionId` is additive; bare `/sessions` states "no session selected"

`App.tsx` registers `sessions` and `sessions/:sessionId` as two separate routes onto the same
`SessionPage` component rather than one route with an optional segment — `SessionPage` branches on
whether `useParams().sessionId` is present. Nothing yet lets an operator pick a session from a list
(the digest's finding chips are a later story), so the bare route is real, deliberate UI — "select a
session first" — not a leftover placeholder; only `/sessions/:sessionId` renders the masthead and
tape.

### A step's offset and elapsed time are plain numbers, not a serialised duration

`session.ts`'s `SessionMasthead.elapsedMs`/`SessionTapeStep.offsetMs` are milliseconds
(`number`/`number | null`), matching `AecoPostMortem.Api.SessionEnvelope`'s own choice to serialise
`TimeSpan` as milliseconds rather than a duration string — one fewer format both sides would
otherwise have to agree on by hand.

### The two Gherkin empty states are still hand-kept in sync between server and client

`web/src/api/appState.ts`'s `AppStateKind` union (`'noSourceFound' | 'emptyStore' | 'ready'`) is
typed by hand against `AecoPostMortem.Api.AppStateReport`'s `AppStateKind` enum — no shared schema
or generated client exists yet. `AecoPostMortem.Api.CLAUDE.md` documents the matching regression
test (`ApiHostTests.The_kind_field_is_serialised_as_camelCase_on_the_wire`) that exists precisely
because this hand-kept contract drifted silently once already (a missing naming policy shipped
`"EmptyStore"` instead of `"emptyStore"`, and neither side's mocked tests caught it).

### `/api/digest` is not served yet — `DigestPage` targets the seam, not a live endpoint

FR-41's real orchestration (assembling `MastheadCounters`, a `CheckRegistry` and every `Finding`
from the live store into one `ProcessDigest`) is later work no story has wired into `ApiHost` yet
(`AecoPostMortem.Api/CLAUDE.md`). `fetchDigest`/`useDigest` target `/api/digest` ahead of that
wiring anyway, the same seam `fetchAppState`/`useAppState` established for `/api/app-state` before
S-48 served it for real — today a real browser sees `DigestPage`'s own "Could not reach the local
API" message; the moment a future story serves the route, this page starts rendering live data with
no frontend change. `DigestPage.test.tsx` and `App.routing.test.tsx` mock `/api/digest`'s response
directly rather than waiting for that wiring.

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
coverage is a fourth honest not-yet: `ruleCoverageText` is a `Record<RuleCoverageStatus, string>`
with exactly one entry, so it renders "Rules not yet analysed" and structurally has no branch that
could produce a bare zero — and when FR-26/FR-40 add a `RuleCoverageStatus` member, that `Record`
fails to type-check until its wording is supplied deliberately.

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

### A finding chip's label is `finding.recurrence.key`, the same convention `FindingRow` already established

`FindingEnvelope` carries no separate "title" or "name" field — `digest/FindingRow.tsx` already
renders `finding.recurrence.key` as a digest row's own visible label (FR-41). `FindingChips` reuses
that exact convention rather than inventing a second one for this surface, so the same finding reads
with the same label whether it is seen on the digest or on a session's chip row.

## Playbook — adding a route

1. Add the page component under `src/routes/`. A surface with no real content yet renders
   `<ComingSoon surface="…" story="S-NN" release="Release N" />` — do not invent a second
   placeholder shape.
2. Register the route in `App.tsx`, under the shared `AppShell` element so navigation and the
   app-state banner stay present.
3. Add its nav link in `AppShell.tsx` if it is one of the three primary surfaces.
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
found" states rather than rendering an empty table. `/api/rules-inventory` is not served yet
(`api/rulesInventory.ts` documents why), so a real browser sees this route's own
"could not reach the local API" message today — the same seam `DigestPage` uses.

The Process Digest (`routes/DigestPage.tsx`, `api/digest.ts`, `api/useDigest.ts`, `digest/`) has its
real content — S-36 (issue #44) built the masthead/ranking contract and, in a follow-up, the rendered
masthead (`digest/Masthead.tsx`), the prominent `sessionsAffected` count on each row, and the three
designed states' distinct wording; S-54 (issue #45) built row expansion, the recurrence strip, the
repository scope contract and this route's actual UI. No story
has wired `/api/digest` to real derived data yet (see the non-obvious decision above), so today the
route always renders its own loading/error state against a real browser.

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

FR-25 (S-12, issue #21) added `SessionTapeStep.pluginName`/`.pluginVersion` — a `'skill'` step's
plugin and version, rendered next to its name (`session-tape__plugin`, shown only when
`pluginName` is non-null; `formatPlugin` in `session/Tape.tsx` — the tape's own row rendering,
after S-53's extraction — joins the version in only when both are present). A subagent's skill
already carried `ownerKind`/`agentId` correctly since S-08 (the
same generic attribution every step kind gets) — this story only closed the plugin/version gap, it
added no new lane-rendering: the tape still renders one flat, wall-clock-ordered list, and grouping
steps visually by lane is S-09's job (FR-22), not built here.

FR-39 (S-35, issue #43) added the Monitor comparison's own block (`digest/MonitorComparisonBlock.tsx`,
`api/monitor.ts`): a `MonitorComparisonEnvelope` renders as two `AdherenceFigureBlock`s, Before and
After, each preceded by its own session count sharing the percentage's own CSS class and
`data-emphasis` marker (see "The Monitor comparison's session count..." above). `/api/monitor-
comparison` is not served yet — `AecoPostMortem.Api/CLAUDE.md`'s own status note documents the same
not-yet-wired gap `/api/digest` and `/api/rules-inventory` carry — and no route in `App.tsx` mounts
this block yet either: it is a reusable renderer, the same "built ahead of the page that will place
it" pattern `AdherenceFigureBlock` followed before `FindingRow` existed to call it. `MonitorComparison
Block.test.tsx` exercises it directly against the reference corpus's own measured 41.8% → 71.7% edit
(3 sessions, then 4).

Test tooling: `vitest` + `@testing-library/react` + `jsdom`, configured in `vitest.config.ts`
(read instead of `vite.config.ts` when both exist, so the React plugin is duplicated there
rather than shared) and `src/vitest-setup.ts` (jest-dom matchers, and `afterEach(cleanup)` since
`test.globals` is off and testing-library's usual auto-cleanup never registers without it).
`npm test` runs the suite once; `npm run build` still type-checks (`tsc -b`) ahead of `vite build`.
