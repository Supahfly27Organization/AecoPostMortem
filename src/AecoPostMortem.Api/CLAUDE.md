# AecoPostMortem.Api

Endpoints for the three surfaces, and the host that serves them.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General` and `FindingEnvelope.Adherence`, and the `From`/`FromAdherence` factories that assemble them from a `Finding` |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |
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

### `FindingEnvelope` and `SuggestionEnvelope` are still a contract, not endpoints

S-50 / FR-59 published the response shape so the stories that build real finding endpoints against
it (S-08, S-22, S-24, S-36, S-37, S-42) have something structural to target. Nothing here reads
through `Data` or calls into `Rules` for those two types yet — the factory methods take a `Finding`
(and, for `FromAdherence`, a `Resolution` and rule version) as plain inputs. `ApiHost` does not
serve `FindingEnvelope` yet either; the app-state endpoint is the first real endpoint this project
ships, and it does not need the finding contract at all.

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

## Status

The response envelope contract (`FindingEnvelope`, `SuggestionEnvelope`) — still unconsumed by any
endpoint. The app-state endpoint and host (`AppStateReport`, `ApiHost`) that S-48 adds: `serve`
(`AecoPostMortem.Cli`) builds and runs this host, and `web/`'s `AppStateBanner` is the client that
reads it. No finding endpoint exists yet — that arrives with the stories `FindingEnvelope.cs`
already named.
