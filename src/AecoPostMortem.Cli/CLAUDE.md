# AecoPostMortem.Cli

The command surface FR-58 enumerates: `ingest`, `rebuild`, `purge`, `serve`.

## References

`Api`, `Findings`, `Ingestion`, `Data` — the CLI is the entry point that dispatches into whichever
layer a command needs; it holds no logic of its own beyond parsing and dispatch. `Data` is on that
list because `purge` is a store operation and there is no layer between the two worth inventing for
it.

## Non-obvious decisions

### `CommandSurface.Commands` is the single source of truth for what commands exist

The listing (`CommandListing`) is rendered from it and dispatch (`CommandRunner`) reads from it, so
nothing else enumerates commands. `CommandSpec.ArrivesWith` names what implements each command's
behaviour, and a command whose behaviour has not landed reports that and exits zero rather than
failing — FR-58 requires the surface to enumerate itself before what sits behind it exists.

### `CommandRunner.Run` takes the store as an optional argument

It defaults to `LocalStore.AtDefaultLocation()`, which is the operator's real store. Tests pass a
throwaway one; without that argument the only way to test `purge` would be to delete it.

### `rebuild` drops and recreates the derived layer, and takes no arguments

`rebuild` calls `DerivedSchema.Rebuild` (unconditional drop-and-recreate, distinct from the
version-gated rebuild `LocalStore.Open` already runs via `EnsureCurrent`) and reports the RAW event
count it ran against. It opens the store and nothing else — there is no path argument on its
`CommandSpec` (`Arguments` is `""`, unlike `ingest [path]`), so "the source directory is not read"
holds structurally rather than by a runtime check (S-46, issue #24). The actual re-derivation of
NORMALIZED/FINDINGS rows from RAW is not implemented yet — that logic lands with the E1 ingestion
stories; today `rebuild` empties the derived tables rather than repopulating them, which is the
honest behaviour for a RAW that has no reader yet.

### `ingest` writes RAW and the coverage report; it does not populate the derived layer

`CommandRunner.Ingest` resolves the session-state root — the optional positional `[path]` argument
if given, otherwise `copilotSessionStateRoot` (`serve`'s own default-override parameter, reused
here) — loads the exclusion list from beside the store actually opened
(`Path.Combine(store.Folder, ExclusionListSource.FileName)`, not
`ExclusionListSource.DefaultPath`, so a test store's run never depends on the real machine's own
`exclusions.json`), and calls `AecoPostMortem.Ingestion.IngestionRun.Run`. The returned
`CoverageReport` is written to stdout — sessions found/ingested/excluded with reasons, lines
parsed/skipped, events by type sorted `StringComparer.Ordinal` for a deterministic run-to-run
diff — which is `ingest`'s whole `CommandSpec.OutputChannel` contract (FR-58, FR-14). A missing
Copilot root is not a special case here: `SessionDiscovery.Discover` already reports it as zero
sessions rather than throwing, so the report says so on its own.

This wires FR-1 through FR-7's RAW ingestion, nothing more. `ExecutionRecordBuilder` — the
NORMALIZED-layer reconstruction (`Turn`/`ToolCall`/`Agent`) — is not called from here; populating
those tables from RAW is still the separate, larger piece both this command and `rebuild` are
missing (`AecoPostMortem.Ingestion/CLAUDE.md`'s own Status section names it). `ApiHost.GetSession`
already reconstructs a session's execution record live from RAW for the Flight Recorder, so the
product functions today without that table-population step; `rebuild` still only empties the
derived tables rather than repopulating them.

### `serve` prints the URL, then blocks — `runHost` is the seam that makes that testable

`CommandRunner.Serve` builds the host (`AecoPostMortem.Api.ApiHost.Build`), writes
`http://127.0.0.1:<port>` to stdout (its `CommandSpec.OutputChannel`), then hands the built
`WebApplication` to `runHost`. The real default, `RunUntilShutdown`, calls `app.Run()` — blocking
until the operator stops the process, which is the actual point of `serve`. `CommandRunner.Run`'s
optional `runHost` parameter lets a test supply its own delegate instead: start the host, make a
request, stop it, return — all inside one `[Fact]`, without ever calling the blocking default.

`copilotSessionStateRoot` is `CommandRunner.Run`'s other new optional parameter, for the same
reason `store` already is one: `Serve` defaults it to
`AecoPostMortem.Ingestion.CopilotSourceLocation.DefaultSessionStateRoot` — the real machine's
`~/.copilot/session-state` — and a test overrides it so the app-state result does not depend on
whatever is really on the machine running the test suite.

### `--port 0` is accepted deliberately

The same convention as `dotnet run --urls http://localhost:0`: it asks the OS for an ephemeral
port instead of naming one. `TryParsePort` allows `0` through for exactly this reason — it is what
lets a test run `serve` without claiming a port a parallel test (or a parallel `dotnet test`
invocation) might also want.

### `ServeWebRoot.Resolve()` finds `web/dist` without requiring it to exist

It walks upward from `AppContext.BaseDirectory` looking for `web/dist/index.html`, bounded to eight
levels so a machine with no repository checkout does not walk to the filesystem root. Returns
`null` — not a thrown exception — when nothing is found, because "no web shell built yet" is a
state `ApiHost.Build` already handles (`webRootPath: null`), not a failure. `dotnet build` and
`dotnet test` never run `scripts/build-web.ps1` (`web/CLAUDE.md`), so this has to stay optional.

## Status

The command surface exists (`CommandSpec`, `CommandSurface`, `CommandParser`, `CommandListing`,
`CommandRunner`, `Program`), and all four commands are wired. `serve` builds and runs the local API
and web shell host (`AecoPostMortem.Api.ApiHost`) on a stated default port
(`CommandRunner.DefaultPort`), overridable with `--port <n>`. `ingest` persists RAW through
`IngestionRun.Run` and reports the coverage report; it does not yet populate the NORMALIZED
derived tables (see the non-obvious decision above) — that wiring is still open for both `ingest`
and `rebuild`.

## Playbook — adding a command

1. Add a `CommandSpec` to `CommandSurface.Commands`. Nothing else enumerates commands.
2. Add its name to `CommandSurfaceTests.The_surface_is_exactly_the_four_commands_FR_58_enumerates`
   and to the `[InlineData]` sets in `CommandListingTests` and `CommandRunnerTests`.
3. Implement dispatch in `CommandRunner.Run`.

The listing is rendered from the table, so a command cannot exist without being documented — do not
add a second place that lists commands.
