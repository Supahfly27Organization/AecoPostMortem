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
| `src/api/digest.ts` | the `DigestEnvelope`/`FindingEnvelope`/`SuggestionEnvelope`/`RepositoryScopeEnvelope` shapes and `fetchDigest`, hand-kept in sync with `AecoPostMortem.Api.DigestEnvelope` (`src/AecoPostMortem.Api/DigestEnvelope.cs`) — no generated client exists yet, the same gap `api/appState.ts` documents |
| `src/api/useDigest.ts` | the fetch-once-on-mount hook `DigestPage` reads, mirroring `useAppState`'s loading/error/loaded shape |
| `src/digest/FindingRow.tsx` | one digest row (Scenario 1, issue #45): collapsed by default; expanding it reveals the quoted evidence, `ProvenanceBadge`, `RecurrenceStrip` and `SuggestionBlock` |
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
