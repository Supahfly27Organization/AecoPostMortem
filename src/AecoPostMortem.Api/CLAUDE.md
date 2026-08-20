# AecoPostMortem.Api

Endpoints for the three surfaces, and the host that serves them.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General`, `FindingEnvelope.Adherence` and `FindingEnvelope.BaseRate` (FR-44, issue #41), and the `From`/`FromAdherence`/`FromBaseRate` factories that assemble them from a `Finding`. FR-48 (issue #52, S-42) added `ProvenanceLabel`, required on every shape |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |
| `SilentCheckEnvelope.cs` | FR-42's "checks that found nothing" surface — `SilentCheckEnvelope.From(CheckRegistry)` projects only the entries that ran clean |
| `DigestEnvelope.cs` | FR-41 part 1 (issue #44, S-36): `MastheadEnvelope` and `DigestEnvelope` — the served corpus masthead and the findings already ranked by sessions affected; FR-41 part 2 (issue #45, S-54): `RepositoryScopeEnvelope`, carried on `MastheadEnvelope`. FR-48 (issue #52, S-42) added `InferredFindings`, served separately from `RankedFindings` |
| `AppStateReport.cs` | S-48's zero-data diagnosis — `AppStateKind` (`NoSourceFound` / `EmptyStore` / `Ready`) and `AppStateReport.Diagnose`, the two-empty-states-are-different-fixes rule as one pure function over two booleans |
| `ApiHost.cs` | builds the ASP.NET Core host: `GET /api/app-state` (`AppStateRoute`), `GET /api/sessions/{sessionId}` (`SessionRouteTemplate`), and, when a built web app is available, the static files that serve it from the same process; `DiagnoseAppState` and `GetSession` are the same two without a listener |
| `SessionEnvelope.cs` | FR-21, part 1 of 3 (S-08, issue #15): `SessionTokenFiguresEnvelope`, `SessionMastheadEnvelope`, `SessionTapeStepEnvelope`, `SessionEnvelope` — the served masthead and tape, assembled from `Findings.SessionRecording`. FR-21 part 3 of 3 (S-53, issue #17) added `SessionRecordingStatusEnvelope` (`Complete`/`IngestIncomplete`/`ReconstructionFailed`) and the required `SessionEnvelope.Status` field |

## References

`Findings` — the API is a thin host over the finding classes and their orchestration for the
finding endpoints FR-59 unblocks; nothing here reaches into `Data` or `Rules` for that part.

`Data` and `Ingestion` — added by S-48, for a different reason: `ApiHost.DiagnoseAppState` has to
know whether the store carries any RAW events (`Data.LocalStore`) and whether the Copilot
session-state root exists (`Ingestion.SessionDiscovery`, reusing FR-1's own discovery rather than a
second `Directory.Exists` check). This is a genuine widening of the "thin host" description below,
not an oversight — S-48 is one of the stories `FindingEnvelope.cs`'s own doc comment named as
building "real endpoints," and the app-state endpoint is not a finding endpoint at all.

`ApiHost.GetSession` (S-08) widens the same `Data` reference further: it opens a `PostMortemContext`
and reads `Sessions`/`Turns`/`ToolCalls`/`Agents`/`Skills`/`Hooks` directly by session id, then hands
the plain rows to `Findings.SessionRecording.Build` — the same "read through `Data`, feed `Findings`
plain inputs" split `Findings/CLAUDE.md` documents for its own checks, just performed here instead of
inside `Findings` because nothing in `AecoPostMortem.Findings` may query the store on its own. See
`SessionEnvelope.cs`'s own remarks below for why this reads the *derived* tables rather than
re-deriving them from RAW in this project.

S-53 (issue #17, FR-21 part 3 of 3) widens `GetSession` once more, narrowly: alongside the derived-
table read above, it also reads this session's own `RawEvents` and runs them through
`Ingestion.ExecutionRecordBuilder.Build` purely to read back its `SpawnResolutionCheck` — never for
the `Turn`/`ToolCall`/`Agent` rows that same call also returns, which stay unused here. See
`SessionEnvelope.cs`'s "`SessionRecordingStatusEnvelope`" remark below for why this second, narrow
read does not reopen the "duplicate reconstruction path" question the paragraph above already
settled against.

## Non-obvious decisions

### `FindingEnvelope` is three closed shapes, not one type with a nullable resolution

`Finding.Resolution` is nullable because only adherence classes carry one (FR-33). The response
envelope makes that distinction structural rather than repeating the nullable field: `General` has no
`Resolution` or `RuleVersion` members at all, and `Adherence` is the only shape that has them — both
`required`. Assembling an `Adherence` envelope without a resolution and rule version is a compile
error (CS9035), the same guarantee `Finding.Provenance` already gives (issue #23). `FR-33`'s refusal
therefore lives here, structurally, at build time; `S-24` is the story that exercises the resulting
behaviour at the API boundary — this contract only has to make the bare figure unrepresentable, not
implement the refusal itself.

All three shapes derive from `FindingEnvelope` through a private constructor, so nothing outside this
file can add a fourth shape — the same closed-hierarchy trick `SuggestionEnvelope` uses.
`[JsonPolymorphic]` / `[JsonDerivedType]` carry a `"kind"` discriminator (`"general"` / `"adherence"`
/ `"baseRate"`) so a client can tell the shapes apart without inspecting which optional fields happen
to be present.

### `FindingEnvelope.BaseRate` labels a conditional rule's figure, and cannot be mistaken for a resolved one (FR-44, issue #41)

A conditional rule — the parallel-tool-calling rule's worked example: a measured 43.6% single-call
rate across 7,449 tool-issuing messages, whose *availability of a second independent call* was never
measured — is not a violation rate, and PRD §3.9 names presenting it as one as the exact failure to
avoid. `BaseRate` structurally cannot carry `Resolution` or `RuleVersion` (same as `General` — a base
rate is not a resolved adherence percentage FR-33 could attach a layer to), and instead carries a
`required string UnevaluatedCondition` stating what the logs could not check. Assembling one without
that condition is the same CS9035 compile error `Adherence` gives for its own two required fields —
Scenario 1's "the unevaluated condition is stated" is therefore not optional prose a caller could
forget to add.

Scenario 2 ("A base rate is never ranked as a violation") turned out to be satisfied more strongly
than this story alone set out to: every `BaseRate` finding carries `Provenance.Inferred` (this
story's own worked example), and FR-48 (issue #52, S-42, landed after this one) partitions
`ProcessDigest.Build`'s *entire* input by provenance — every `Inferred` finding, `BaseRate` included,
is structurally excluded from `RankedFindings` and served through `DigestEnvelope.InferredFindings`
instead, never interleaved by rank at all. `DigestEnvelopeTests.A_base_rate_item_never_appears_in_
ranked_findings_and_serialises_a_distinct_kind_in_inferred_findings` proves both halves together: a
base rate is absent from `RankedFindings` and, wherever it is served, still carries the `"baseRate"`
wire discriminator distinct from `"adherence"` — belt and suspenders, not a fallback on the
discriminator alone. `FromBaseRate` still sets that discriminator regardless of which list a caller
eventually serves the envelope through, since this contract has no way to know FR-48 would add a
second list when it was written.

`FromBaseRate(Finding, string unevaluatedCondition)` follows `FromAdherence`'s own precedent:
`unevaluatedCondition` is a required parameter, not read off the `Finding` (which has no field for
it — the same reasoning `FromAdherence` gives for taking `resolution`/`ruleVersion` as parameters
rather than trusting `Finding.Resolution` alone). `SampleConditionalRuleFinding` in both
`FindingEnvelopeTests` and `DigestEnvelopeTests` gives the finding `Provenance.Inferred`, not
`Observed`: the message count itself is a plain fact, but treating it as bearing on the rule at all
assumes the unmeasured condition held, the same reasoning FR-48 gives for labelling the
MCP-unreliability-causes-disobedience hypothesis Inferred even though its two failure rates are each
Observed on their own.

### `ProvenanceLabel` rides alongside `Provenance`, not in place of it

FR-48 (issue #52, S-42) requires the three provenance levels to be distinguishable without reading
the enum's own name, and an Inferred finding's distinguishing text to survive being quoted out of
its original styling. `FindingEnvelope.ProvenanceLabel` is a second, `required` string field —
`Findings.ProvenanceLabel.For(finding.Provenance)` — carried next to the existing `Provenance` enum
member rather than replacing it: `Provenance` stays the machine-readable value a client branches on,
`ProvenanceLabel` is the fixed human sentence a client can render or quote verbatim. Both `From` and
`FromAdherence` set it from the same finding's `Provenance`, so the two can never disagree.

`ProvenanceLabel` only supplies the *textual* half of FR-48's Scenario 2 ("distinguishable without
reading the label"), which read literally also asks for a non-textual discriminator — an icon or
shape a client could use without parsing the sentence at all. That half is not defined on this
contract: `Provenance` itself is still served, so a future client can map it to a shape/icon, but no
such mapping exists here because no rendering surface consumes this contract yet. See
`Findings/CLAUDE.md`'s matching note under "`InferredFindings` is a separate, deliberately unranked
field."

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

### `RepositoryScopeEnvelope` mirrors `RepositoryScope` exactly — a plain projection, not a filter

FR-41 part 2 (issue #45, S-54): `RepositoryScopeEnvelope.From` copies `SelectedRepository` and
`AvailableRepositories` straight across, the same shape `MastheadEnvelope.From` already uses for
every other masthead field. It does not re-derive or re-filter anything — `DigestEnvelope.From`'s
`RankedFindings` mapping is untouched by which repository is selected, because the caller of
`ProcessDigest.Build` (`AecoPostMortem.Findings/CLAUDE.md`) already scoped `findings` to one
repository before this envelope is ever assembled.

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
the ranking already happened in `Findings`, this only converts each entry to its wire shape. The
same mapper is reused for `ProcessDigest.InferredFindings` (FR-48, issue #52, S-42) — there is no
second, Inferred-only mapping function, because an Inferred finding needs exactly the same
`General`/`Adherence` shape decision any other finding does.

### `DigestState` and `RuleCoverageStatus` serialise as their names, not ordinals

Both enums are declared in `Findings` with no serialisation attributes of their own — domain types
stay serialisation-agnostic, the same separation `FindingEnvelope`/`SuggestionEnvelope` already draw.
`MastheadEnvelope.RuleCoverage` and `DigestEnvelope.State` each carry their own
`[JsonConverter(typeof(JsonStringEnumConverter))]` here instead, so a client reads `"NotYetAnalyzed"`
rather than an opaque integer for a state whose entire point (S-36's Gherkin) is to be stated in
words.

### `GetSession` reads the derived tables as they are today — empty — rather than re-deriving from RAW here

`AecoPostMortem.Ingestion.ExecutionRecordBuilder` can rebuild `Turn`/`ToolCall`/`Agent` (not
`Skill`/`Hook`, which it does not parse) from a session's `RawEvent`s, but nothing in this
repository yet writes any of the six shapes `GetSession` needs into the store at ingest time
(`AecoPostMortem.Ingestion/CLAUDE.md`, "not yet wired into the store"). Two options existed: have
this endpoint replay RAW through `ExecutionRecordBuilder` itself, or read the already-mapped but
still-empty `Data.Execution` tables and let a later story's writer populate them. This project took
the second path — `GetSession` queries `context.Sessions`/`Turns`/`ToolCalls`/`Agents`/`Skills`/`Hooks`
exactly the way it would once a writer exists, rather than duplicating a second, partial (no
`Skill`/`Hook`) reconstruction path inside `Api` that the eventual ETL story would have to reconcile
with or replace. `SessionRouteTests` seeds those tables directly through `PostMortemContext` — the
same stand-in `OwnershipTests` (`AecoPostMortem.Data.Tests`) already uses — to exercise the read path
ahead of the writer that will populate it for real.

### `SessionTokenFiguresEnvelope` closes the same gap `SuggestionEnvelope` closed for FR-56

`Findings.SessionTokenFigures` was published (S-11, issue #20) ahead of anything that would render
it, deliberately left unwrapped by any envelope until "S-08's masthead is the story that will call
`From` and render its two states" (`Findings/CLAUDE.md`). This is that story: `SessionTokenFiguresEnvelope`
is a closed two-shape union behind a private constructor — `Observed` and `NotRecorded` — the same
`[JsonPolymorphic]`/`[JsonDerivedType]` mechanism `SuggestionEnvelope` uses, so "context size at end"
is never a nullable field a client could misread as zero.

### `SessionMastheadEnvelope.ElapsedMs`/`SessionTapeStepEnvelope.OffsetMs` carry milliseconds, not a serialised `TimeSpan`

The domain layer (`Findings.SessionMasthead.Elapsed`, `SessionTapeStep.Offset`) uses `TimeSpan`;
the envelope converts both to `long`/`long?` milliseconds instead of letting `System.Text.Json`
serialise `TimeSpan` directly, so a client reads a plain number rather than needing to agree with
the server on a `TimeSpan` text format. `DateTimeOffset` (`SessionTapeStepEnvelope.Timestamp`) is
left as-is — `MastheadEnvelope.SpanStart`/`SpanEnd` already establish that this type serialises
losslessly and needs no format agreement of its own.

### `SessionRecordingStatusEnvelope` widens `GetSession`'s read on purpose, and states why that is not the duplicate-reconstruction-path this project already ruled out

FR-21 part 3 of 3 (S-53, issue #17): the "reconstruction failed, states what was skipped" scenario
has exactly one real signal already built anywhere in this repository — `Ingestion.
ExecutionRecordBuilder`'s `SpawnResolutionCheck` (FR-9), which counts a `subagent.started` event
whose `toolCallId` never resolves against a spawning `task` call rather than silently dropping it.
Nothing in `Data.Execution.Agent` records an unresolved spawn (an unresolved one is excluded from
`Agent` entirely — `Ingestion/CLAUDE.md`), so there is no way to answer Scenario 4 from the derived
tables `GetSession` already reads; the only place the signal exists is a fresh pass over the
session's own RAW events. `GetSession` therefore reads `context.RawEvents` a second time, scoped to
the one session being requested (bounded, not a corpus scan), and calls `ExecutionRecordBuilder.
Build` — but reads only `.SpawnResolutionCheck` off the result, discarding the `Turns`/`ToolCalls`/
`Agents` it also returns. That is the deliberate line: the "GetSession reads the derived tables ...
rather than re-deriving them from RAW" remark above is about not maintaining two competing paths
that both produce the masthead/tape's *rows* — this second read produces nothing that overlaps with
that; it exists only to answer one yes/no diagnostic question the derived tables cannot answer today.

`SessionRecordingStatusEnvelope` itself is a closed three-shape union behind a private constructor
— `Complete`, `IngestIncomplete`, `ReconstructionFailed { Skipped }` — the same `[JsonPolymorphic]`/
`[JsonDerivedType]` mechanism `SessionTokenFiguresEnvelope` and `SuggestionEnvelope` already use, so
a client reads which of the three states applies from the wire `"kind"` discriminator
(`"complete"`/`"ingestIncomplete"`/`"reconstructionFailed"`) rather than inferring it from which
optional fields happen to be present. `SessionEnvelope.Status` is `required` — every served session
carries one of the three, never an implicit fourth "unspecified" state.

### `SessionTapeStepEnvelope.Kind` and `OwnerKind` get their camelCase wire form from the global converter, not a per-property override

Unlike `MastheadEnvelope.RuleCoverage`/`DigestEnvelope.State` (which opt out of the global naming
policy via an explicit, un-parameterised `[JsonConverter(typeof(JsonStringEnumConverter))]` so a
client reads the exact word, e.g. `"NotYetAnalyzed"`), `SessionTapeStepKind` and
`AecoPostMortem.Data.Execution.OwnerKind` carry no such override — there is no "must read as this
exact English word" requirement here, so both fall through to the
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` `ApiHost.Build` already registers globally,
the same as `AppStateKind`. A step reads `"toolCall"`/`"mcpCall"`/etc., never `"ToolCall"`.

## Status

The response envelope contract (`FindingEnvelope`, `SuggestionEnvelope`, `SilentCheckEnvelope`,
`DigestEnvelope`, `MastheadEnvelope`, `RepositoryScopeEnvelope`) — still unconsumed by any live
`/api/digest` endpoint in `ApiHost`. The app-state endpoint and host (`AppStateReport`, `ApiHost`)
that S-48 adds are the first real endpoint this project ships: `serve` (`AecoPostMortem.Cli`) builds
and runs this host, and `web/`'s `AppStateBanner` is the client that reads it. No digest endpoint
exists yet — `web/`'s `DigestPage` (S-54, issue #45) already targets `/api/digest` ahead of it, the
same seam `AppStateBanner` used for `/api/app-state` before S-48 wired it, but `ApiHost.Build` does
not `MapGet` it: assembling a real `ProcessDigest` from the live store (a `MastheadCounters`
populated at ingest, a `CheckRegistry`, and every `Finding` from every check orchestrator) is later,
unwired work no story has done yet.

FR-48 (issue #52, S-42) added `FindingEnvelope.ProvenanceLabel` (required on every shape) and
`DigestEnvelope.InferredFindings` (served separately from `RankedFindings`, mirroring
`ProcessDigest`'s own split). Both are contract-only from this project's side — no endpoint serves
either through a live store — but `web/src/digest/ProvenanceBadge.tsx` (S-54, issue #45) is a real
consumer of the shape once `DigestPage` does have data to render, closing the gap that was still
open when S-42 alone had landed.

`GET /api/sessions/{sessionId}` (`SessionEnvelope.cs`, `ApiHost.GetSession`, S-08, issue #15) is the
second real endpoint: FR-21's masthead and tape, read through `Data.Execution` and assembled by
`Findings.SessionRecording.Build`. `web/src/routes/SessionPage.tsx` is the client. Returns 404 for a
session id the store carries no `Session` row for; a session with rows but no steps still serves its
masthead with an empty `Steps` list. Finding chips (a different data path — findings joined per
session) and the inspector are S-52, not served here.

FR-21 part 3 of 3 (S-53, issue #17) added `Status` (`SessionRecordingStatusEnvelope`) to the same
envelope: `GetSession` now also runs the session's own RAW events through `Ingestion.
ExecutionRecordBuilder` for its `SpawnResolutionCheck` alone (see the non-obvious decision above),
so a session with an unresolved subagent spawn is served as `reconstructionFailed` and one with no
recorded end as `ingestIncomplete`, distinctly from the ordinary `complete` case.
