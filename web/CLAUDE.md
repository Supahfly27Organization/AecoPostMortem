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
| `src/routes/ComingSoon.tsx` | the placeholder a surface with no real content yet renders — naming its own story and release rather than sharing one generic message |
| `src/routes/RulesInventoryPage.tsx` | still `ComingSoon`, its own story/release (S-22) — not yet built |
| `src/routes/DigestPage.tsx` | FR-41's real content (S-36 + S-54, issues #44/#45): the masthead's repository selector plus the ranked findings, each an expandable `FindingRow`. Fetches `/api/digest` via `useDigest`; loading renders nothing, a failed fetch renders its own `role="alert"` message, the same shape `AppStateBanner` established |
| `src/api/digest.ts` | the `DigestEnvelope`/`FindingEnvelope`/`SuggestionEnvelope`/`RepositoryScopeEnvelope`/`AdherenceFigure` shapes and `fetchDigest`, hand-kept in sync with `AecoPostMortem.Api.DigestEnvelope` (`src/AecoPostMortem.Api/DigestEnvelope.cs`) — no generated client exists yet, the same gap `api/appState.ts` documents |
| `src/api/useDigest.ts` | the fetch-once-on-mount hook `DigestPage` reads, mirroring `useAppState`'s loading/error/loaded shape |
| `src/digest/FindingRow.tsx` | one digest row (Scenario 1, issue #45): collapsed by default; expanding it reveals the quoted evidence, `ProvenanceBadge`, `RecurrenceStrip` and `SuggestionBlock` |
| `src/digest/AdherenceFigureBlock.tsx` | FR-33 (S-24, issue #38): the only place in the app that renders an adherence percentage — and it renders the per-operand resolution table and rule-set version with it, so no surface can show the number alone |
| `src/digest/RecurrenceStrip.tsx` | Scenario 2: names every session a finding touched (`Recurrence.occurrences`), not only the count |
| `src/digest/ProvenanceBadge.tsx` | PRD §3.8's three provenance levels, rendered distinguishably — a `data-provenance` attribute drives a distinct colour per level, alongside the badge's own text label |
| `src/digest/SuggestionBlock.tsx` | Scenario 4: renders `SuggestionEnvelope`'s `present`/`absent` states — an explicit "No suggestion is offered." for `absent`, never a blank area |
| `src/digest/RepositorySelector.tsx` | Scenario 3 / PRD Part 8 Q5: shows the selected repository and offers every available one — the seam for a later cross-repository view, not that view itself |
| `src/api/session.ts` | the `SessionEnvelope`/`SessionMasthead`/`SessionTapeStep` shapes and `fetchSession`, hand-kept in sync with `AecoPostMortem.Api.SessionEnvelope` (`src/AecoPostMortem.Api/SessionEnvelope.cs`) |
| `src/api/useSession.ts` | the fetch-per-`sessionId` hook `SessionPage` reads; loading renders nothing, an error (404 or unreachable API) is one explicit state |
| `src/routes/SessionPage.tsx` | FR-21, part 1 of 3 (S-08, issue #15): the Flight Recorder — masthead and time-ordered tape, real as of this story. Reads `sessionId` from the route; no `sessionId` (bare `/sessions`) states "no session selected" rather than reusing `ComingSoon`, since the surface itself is built |

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

### The repository selector is a seam, not a working cross-repository switch

PRD Part 8 Q5 decided the digest defaults to one repository, selectable. `RepositorySelector` is a
real, interactive `<select>` — choosing a different `availableRepositories` entry does change what
it displays — but nothing in `DigestPage` re-fetches a cross-repository digest when that happens
(no orchestration for a second repository's digest exists yet either). This is this story's own
edge case, not an oversight: implement the default, keep the selector itself real, leave the
cross-repository view to later work.

## Playbook — adding a route

1. Add the page component under `src/routes/`. A surface with no real content yet renders
   `<ComingSoon surface="…" story="S-NN" release="Release N" />` — do not invent a second
   placeholder shape.
2. Register the route in `App.tsx`, under the shared `AppShell` element so navigation and the
   app-state banner stay present.
3. Add its nav link in `AppShell.tsx` if it is one of the three primary surfaces.
4. Cover it in `App.routing.test.tsx` — the route resolves, and (if still a placeholder) the
   correct story/release text is present.

## Status

Routing (React Router: `App.tsx`, `AppShell.tsx`), the two zero-data states plus the "API
unreachable" state (`AppStateBanner.tsx`, `api/useAppState.ts`, `api/appState.ts`), and all three
surfaces reachable — S-48. `RulesInventoryPage.tsx` remains a named `ComingSoon` placeholder; its
own story (S-22) builds it.

The Process Digest (`routes/DigestPage.tsx`, `api/digest.ts`, `api/useDigest.ts`, `digest/`) has its
real content — S-36 (issue #44) built the masthead/ranking contract, S-54 (issue #45) built row
expansion, the recurrence strip, the repository scope contract and this route's actual UI. No story
has wired `/api/digest` to real derived data yet (see the non-obvious decision above), so today the
route always renders its own loading/error state against a real browser.

The session view (`routes/SessionPage.tsx`, `api/session.ts`, `api/useSession.ts`) is real as of
S-08 (FR-21, part 1 of 3, issue #15): the masthead and the time-ordered tape, reading
`GET /api/sessions/{sessionId}`. Finding chips and the inspector (Detail/Thinking/Raw tabs) are
S-52/S-53, not built here.

Test tooling: `vitest` + `@testing-library/react` + `jsdom`, configured in `vitest.config.ts`
(read instead of `vite.config.ts` when both exist, so the React plugin is duplicated there
rather than shared) and `src/vitest-setup.ts` (jest-dom matchers, and `afterEach(cleanup)` since
`test.globals` is off and testing-library's usual auto-cleanup never registers without it).
`npm test` runs the suite once; `npm run build` still type-checks (`tsc -b`) ahead of `vite build`.
