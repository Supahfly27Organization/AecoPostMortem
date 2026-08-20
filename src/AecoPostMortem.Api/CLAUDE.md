# AecoPostMortem.Api

Endpoints for the three surfaces, and the host that serves them.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General` and `FindingEnvelope.Adherence`, and the `From`/`FromAdherence` factories that assemble them from a `Finding` |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |
| `SilentCheckEnvelope.cs` | FR-42's "checks that found nothing" surface — `SilentCheckEnvelope.From(CheckRegistry)` projects only the entries that ran clean |
| `DigestEnvelope.cs` | FR-41 (issue #44, S-36): `MastheadEnvelope` and `DigestEnvelope` — the served corpus masthead and the findings already ranked by sessions affected |
| `AppStateReport.cs` | S-48's zero-data diagnosis — `AppStateKind` (`NoSourceFound` / `EmptyStore` / `Ready`) and `AppStateReport.Diagnose`, the two-empty-states-are-different-fixes rule as one pure function over two booleans |
| `ApiHost.cs` | builds the ASP.NET Core host: `GET /api/app-state` (`AppStateRoute`) and, when a built web app is available, the static files that serve it from the same process; `DiagnoseAppState` is the same diagnosis without a listener |

## References

`Findings` — the API is a thin host over the finding classes and their orchestration for the
finding endpoints FR-59 unblocks; nothing here reaches into `Data` or `Rules` for that part.

`Data` and `Ingestion` — added by S-48, for a different reason: `ApiHost.DiagnoseAppState` has to
know whether the store carries any RAW events (`Data.LocalStore`) and whether the Copilot
session-state root exists (`Ingestion.SessionDiscovery`, reusing FR-1's own discovery rather than a
second `Directory.Exists` check). This is a genuine widening of the "thin host" description below,
not an oversight — S-48 is one of the stories `FindingEnvelope.cs`'s own doc comment named as
building "real endpoints," and the app-state endpoint is not a finding endpoint at all.

## Non-obvious decisions

### `FindingEnvelope` is two closed shapes, not one type with a nullable resolution

`Finding.Resolution` is nullable because only adherence classes carry one (FR-33). The response
envelope makes that distinction structural rather than repeating the nullable field: `General` has no
`Resolution` or `RuleVersion` members at all, and `Adherence` is the only shape that has them — both
`required`. Assembling an `Adherence` envelope without a resolution and rule version is a compile
error (CS9035), the same guarantee `Finding.Provenance` already gives (issue #23). `FR-33`'s refusal
therefore lives here, structurally, at build time; `S-24` is the story that exercises the resulting
behaviour at the API boundary — this contract only has to make the bare figure unrepresentable, not
implement the refusal itself.

Both shapes derive from `FindingEnvelope` through a private constructor, so nothing outside this file
can add a third shape — the same closed-hierarchy trick `SuggestionEnvelope` uses. `[JsonPolymorphic]`
/ `[JsonDerivedType]` carry a `"kind"` discriminator (`"general"` / `"adherence"`) so a client can tell
the two apart without inspecting which optional fields happen to be present.

### `SuggestionEnvelope` makes "no suggestion" a value, not an absence

`Finding.Suggestion` is nullable because a finding class with no template (FR-56) ships with none.
Wrapping it in a nullable field on the envelope would let "no suggestion" collide with "the field was
omitted by mistake." `SuggestionEnvelope` is instead a required, closed two-state union —
`Present { Text }` and the `Absent` singleton (backed by the nested `AbsentSuggestion` record) — so
every served finding's `Suggestion` field is present in the JSON, and its value states explicitly
which case applies. `SuggestionEnvelope.Of(Suggestion?)` does the mapping from the domain type.

### `SilentCheckEnvelope.From` filters, it never synthesises

FR-42's surface has exactly one producer, `From(CheckRegistry)`, and it is a pure filter over
`CheckRegistry.Entries` — `Status == Ran && FindingCount == 0` — never a step that fabricates an
entry for a check the registry doesn't carry. That is what makes all three of this story's negative
scenarios (issue #46) hold structurally rather than by caller discipline:

- A `Refused` entry is dropped here; it belongs to the Rules Inventory (FR-53) as "not checkable",
  a different surface this project does not yet implement — showing it here as clean is exactly the
  "silence reading as compliance" failure PRD §3.9 names.
- A check the registry has no entry for at all (not built yet this release, e.g. the contradiction
  check before S-38) has nothing for `From` to project — absence in, absence out. There is no
  hard-coded list of expected `CheckId`s this type could complete against; it only ever reflects
  what `CheckRegistry.Entries` actually contains.
- A `Ran` entry with `FindingCount > 0` is also dropped: this surface is specifically checks that
  found *nothing*, not every check that ran. `FindingCount` is a real int on every served
  `SilentCheckEnvelope` (never null, since `Refused` entries — the only ones with a null
  `FindingCount` — are filtered out before the projection), and it is always `0` by construction of
  the filter, carried explicitly rather than left for the reader to infer from mere presence.

Unlike `FindingEnvelope` and `SuggestionEnvelope`, `SilentCheckEnvelope` is a single plain
`sealed record` rather than a closed hierarchy behind a private constructor. Those two types close
off a *discriminated union* — "which of these shapes is this?" is part of what a client needs to
know. This surface serves only one shape (a clean check's id, population and zero count); there is
no second variant to keep a client from constructing by mistake, so there is nothing for the
closed-hierarchy trick to protect here.

### `FindingEnvelope` and `SuggestionEnvelope` are still a contract, not endpoints

S-50 / FR-59 published the response shape so the stories that build real finding endpoints against
it (S-08, S-22, S-24, S-36, S-37, S-42) have something structural to target. Nothing here reads
through `Data` or calls into `Rules` for those two types yet — the factory methods take a `Finding`
(and, for `FromAdherence`, a `Resolution` and rule version) as plain inputs. `ApiHost` does not
serve `FindingEnvelope`, `SilentCheckEnvelope` or `DigestEnvelope` yet either; the app-state endpoint
is the first real endpoint this project ships, and it does not need the finding contract at all.
`SilentCheckEnvelope.From` follows the same plain-input pattern — a `CheckRegistry` in, a projected
list out — nothing here reads through `Data` or calls into `Rules` for it either.

### `ApiHost.Build` returns an unstarted `WebApplication`

The caller (`AecoPostMortem.Cli`'s `serve` command) decides when to start it and how long to run
it. That is what keeps the host testable without a Kestrel listener staying up for the life of a
test run: a test starts it, makes a request, and stops it again, all inside one `[Fact]`.

### `127.0.0.1`, not `localhost`, and a camelCase enum on the wire

`UseUrls` binds `127.0.0.1` rather than `localhost` — Kestrel refuses a dynamic port (`--port 0`)
bound to the `localhost` host name, which the test suite needs to avoid claiming a fixed port
another test (or another `dotnet test` run) might also want, and `127.0.0.1` is what `localhost`
resolves to for the operator's browser regardless.

`AppStateKind` is serialised as a camelCase string (`"emptyStore"`, not `"EmptyStore"`) via
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` — the naming policy has to be passed to the
converter explicitly; it is not inherited from `ConfigureHttpJsonOptions`'s own camelCase property
naming. `ApiHostTests.The_kind_field_is_serialised_as_camelCase_on_the_wire` is a regression test
for exactly this: an earlier version of this host shipped `"EmptyStore"` because of the missing
naming policy, silently mismatching `web/src/api/appState.ts`'s `AppStateKind` union without either
side's own tests catching it (both sides mocked past the real wire format).

### The web shell is optional, never a hard dependency on Node

`ApiHost.Build`'s `webRootPath` parameter is resolved by the CLI's `ServeWebRoot.Resolve()`, which
walks up from the running executable looking for `web/dist/index.html` (the output of
`scripts/build-web.ps1`). `dotnet build` and `dotnet test` never run that script (`web/CLAUDE.md`),
so a machine that has only built the .NET solution has no web shell to serve; `serve` still answers
`/api/app-state`, it just falls through on `/` instead of returning `index.html`. This is why
`Build` accepts `webRootPath: null` as a normal case rather than throwing.

### `DigestEnvelope.From` takes a mapper, not a fixed factory

`DigestEnvelope.From(ProcessDigest, Func<Finding, FindingEnvelope>)` cannot assume every ranked
finding maps through `FindingEnvelope.From` — an adherence finding needs `FromAdherence` with its
resolution and rule version instead (FR-33), and only the caller (which already has the resolution)
knows which shape a given finding needs. The mapper preserves `ProcessDigest.RankedFindings`' order:
the ranking already happened in `Findings`, this only converts each entry to its wire shape.

### `DigestState` and `RuleCoverageStatus` serialise as their names, not ordinals

Both enums are declared in `Findings` with no serialisation attributes of their own — domain types
stay serialisation-agnostic, the same separation `FindingEnvelope`/`SuggestionEnvelope` already draw.
`MastheadEnvelope.RuleCoverage` and `DigestEnvelope.State` each carry their own
`[JsonConverter(typeof(JsonStringEnumConverter))]` here instead, so a client reads `"NotYetAnalyzed"`
rather than an opaque integer for a state whose entire point (S-36's Gherkin) is to be stated in
words.

## Status

The response envelope contract (`FindingEnvelope`, `SuggestionEnvelope`, `SilentCheckEnvelope`,
`DigestEnvelope`, `MastheadEnvelope`) — still unconsumed by any finding endpoint. The app-state
endpoint and host (`AppStateReport`, `ApiHost`) that S-48 adds are the first real endpoint this
project ships: `serve` (`AecoPostMortem.Cli`) builds and runs this host, and `web/`'s
`AppStateBanner` is the client that reads it. No finding endpoint exists yet — that arrives with the
stories `FindingEnvelope.cs` already named.
