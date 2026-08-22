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

### `--store <path>` is global, per-invocation, and never persisted

Every command opens the store, so `--store` is taken by `CommandParser` (which stays pure — no
environment, no file system) rather than by each command's own argument handling; a flag that meant
something different, or nothing, depending on the verb would be its own trap. It is documented on all
four `CommandSurface` entries, and `CommandSurfaceTests.Every_command_documents_the_global_store_option`
keeps it that way — that table is the only place commands are enumerated, so a working-but-undocumented
flag is structurally impossible.

**Taking the flag out of the argument list is load-bearing, not tidiness.** `ingest` reads
`Arguments[0]` as its session-state root, so a `--store` left in place would be read as that path.
`CommandParser.TakeStoreOption` lifts the flag and its value out and preserves the order of
everything else; `CommandParserTests` pins both halves, including a `--store` that follows
`serve`'s own `--port`.

Precedence, outermost first: the injected `store` parameter (tests only — the CLI never passes one),
then `--store`, then the default location. The injected store wins so a test can keep itself off the
real store unconditionally.

**Why a flag and not a persisted setting.** `Data.StoreLocation`'s own rationale — one documented
per-user path, nothing to configure and therefore nothing to get wrong — is what shaped this: a
config file or a UI-editable setting would be a second, persisted source of truth that can drift out
of agreement with the store actually open, and `ApiHost.Build` bakes the path in at startup, so a UI
that changed it would be changing something the operator cannot see take effect until they restart
the very process serving that page. A per-invocation flag adds the override without adding any of
that.

**One real consequence**: the exclusion list is loaded from beside the store actually opened
(`Path.Combine(store.Folder, ExclusionListSource.FileName)` — see `ingest`'s own remarks below), so
`--store` moves the exclusion list with it. A store and its exclusions travelling together is the
intended reading, not an accident, but it does mean a store in a fresh directory starts with
`ExclusionListSource.Load`'s own documented fallback rather than whatever the default store's
`exclusions.json` holds.

**A side benefit worth naming**: this is what finally lets a real-browser verification pass run
against a throwaway store. Verifying the `purge` button (PR #146) previously required deleting the
operator's real store and re-ingesting it, because `serve` had no way to be pointed anywhere else.

### `rebuild` drops, recreates, and repopulates the derived layer, and takes no arguments

`rebuild` calls `AecoPostMortem.Ingestion.NormalizedLayerWriter.RebuildAll` — the one shared
definition of "rebuild" (Settings surface task): unconditional drop-and-recreate
(`Data.Execution.DerivedSchema.Rebuild`, distinct from the version-gated rebuild `LocalStore.Open`
already runs via `EnsureCurrent`) followed by `NormalizedLayerWriter.Derive` for every distinct
`SessionId` RAW still holds, so the six NORMALIZED tables come back populated rather than empty. This
used to be inlined directly in `CommandRunner.Rebuild`; it moved to `Ingestion` so
`AecoPostMortem.Api`'s `POST /api/rebuild` (`Api/CLAUDE.md`'s own remarks on why the API calls
`Ingestion` directly rather than reaching into this project) can call the identical sequence rather
than a second copy. `CommandRunner.Rebuild` itself still opens the store and nothing else — there is
no path argument on its `CommandSpec` (`Arguments` is `""`, unlike `ingest [path]`), so "the source
directory is not read" holds structurally rather than by a runtime check (S-46, issue #24):
repopulation reads only RAW, already in the store.

### `ingest` writes RAW, populates the derived layer, and reports the coverage report

`CommandRunner.Ingest` resolves the session-state root — the optional positional `[path]` argument
if given, otherwise `copilotSessionStateRoot` (`serve`'s own default-override parameter, reused
here) — loads the exclusion list from beside the store actually opened
(`Path.Combine(store.Folder, ExclusionListSource.FileName)`, not
`ExclusionListSource.DefaultPath`, so a test store's run never depends on the real machine's own
`exclusions.json`), and calls `AecoPostMortem.Ingestion.IngestionRun.Run`, which now also derives
each successfully ingested session's NORMALIZED rows via `NormalizedLayerWriter` as it goes (and
purges them, alongside RAW, for a session excluded after already being ingested). The returned
`CoverageReport` is written to stdout — sessions found/ingested/excluded with reasons, lines
parsed/skipped, events by type sorted `StringComparer.Ordinal` for a deterministic run-to-run
diff — which is `ingest`'s whole `CommandSpec.OutputChannel` contract (FR-58, FR-14). A missing
Copilot root is not a special case here: `SessionDiscovery.Discover` already reports it as zero
sessions rather than throwing, so the report says so on its own.

Verified end to end against the live 35-session reference corpus, not only against fixtures: the
Flight Recorder (`ApiHost.GetSession`) now renders a real session's masthead, tape and inspector
straight from a real `ingest` run. Getting there surfaced a real defect in already-shipped code —
`Data.Execution.Turn`'s original key assumed `data.turnId` is unique within a session, which is false
on 27 of 35 real sessions — fixed in `Data`/`Ingestion` (see both projects' own CLAUDE.md), not in
this project.

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
(`CommandRunner.DefaultPort`), overridable with `--port <n>`. Every command also accepts the global
`--store <path>`, which opens a store somewhere other than FR-11's documented per-user location — see
the non-obvious decision above for why it is a per-invocation flag rather than a persisted setting. `ingest` persists RAW through
`IngestionRun.Run`, populates the NORMALIZED derived tables through `NormalizedLayerWriter` as it
goes, and reports the coverage report; `rebuild` repopulates the same six tables for every session
RAW holds. Both are verified end to end against the live reference corpus, not only fixtures.

## Playbook — adding a command

1. Add a `CommandSpec` to `CommandSurface.Commands`. Nothing else enumerates commands.
2. Add its name to `CommandSurfaceTests.The_surface_is_exactly_the_four_commands_FR_58_enumerates`
   and to the `[InlineData]` sets in `CommandListingTests` and `CommandRunnerTests`.
3. Implement dispatch in `CommandRunner.Run`.

The listing is rendered from the table, so a command cannot exist without being documented — do not
add a second place that lists commands.
