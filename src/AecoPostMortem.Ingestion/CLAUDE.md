# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion,
volume control (FR-10, FR-12, FR-13), rule-statement resolution from the store (FR-26).

## Structure

| File | What it holds |
|---|---|
| `SourceFiles.cs` | the only door onto `~/.copilot/`; refuses `ExcludedSources`-excluded paths before any OS-level open |
| `CopilotSourceLocation.cs` | S-48: the default per-user Copilot session-state root (`~/.copilot/session-state`), resolved the same way `AecoPostMortem.Data.StoreLocation` resolves the store's own path. Names the path only — `SessionDiscovery` is what asks whether it is really there |
| `SessionDiscovery.cs` | FR-1: finds every session directory under the session-state root and classifies its files (`events.jsonl`, `session.db`, `rewind-snapshots/index.json`, `workspace.yaml`) without reading any of them; a missing root is reported, not thrown |
| `SessionEventReader.cs` | FR-3/FR-6: reads one `events.jsonl` line by line into `RawEvent`s — provider version and event-schema version from line 1 only, malformed lines skipped and counted, a trailing unterminated line stops the read and reports the high-water offset |
| `EventEnvelopeParsers.cs` | `IEventEnvelopeParser`, the envelope-field (`type`/`timestamp`) reader, and the version-keyed registry `SessionEventReader` selects from — falls back to the one shape measured today for a schema version it has not seen |
| `SessionIngestor.cs` | reads a session file and lands what parsed in RAW via `RawEventBatch.Append`; FR-5 (issue #5): runs `RawEventBatch.DetectRewrites` first and refuses the append — reporting the mismatch on `SessionIngestResult.RewriteMismatches`/`RewriteDetected` instead — the moment a rewrite is found. FR-7 (issue #6): checks `SessionExclusion` ahead of both the rewrite check and the append, and retroactively purges via `RawEventBatch.DeleteBySession` when an already-ingested session is now excluded |
| `SessionStartContext.cs` | FR-7's key: reads `session.start.data.context.cwd` off a session's first event only — the "line 1 only" rule `SessionEventReader.ReadDeclaredVersion` already applies to provider/schema version, applied again here |
| `SessionExclusion.cs` | FR-7's pure decision: whether a cwd falls under one of an operator-configured list of excluded roots, and the reason sentence FR-14's coverage report states for it (`SessionExclusionOutcome`) |
| `ExclusionListSource.cs` | FR-7's "operator-configured, not compiled in" half: reads the exclusion list fresh from a JSON file on every call, defaulting to this product's own repository root when no file exists |
| `CoverageReport.cs` | FR-14's report shape: sessions found, ingested, excluded and why, lines parsed, lines skipped, events by type |
| `IngestionRun.cs` | FR-14's builder: walks `SessionDiscovery`'s output through `SessionIngestor` and rolls the results into one `CoverageReport` |
| `MalformedLineCheck.cs` | turns one or more `SessionReadResult`s into the malformed-line `CheckRegistryEntry` (issue #23) |
| `ExcludedSources.cs` | FR-10: recognises Copilot's global `session-store.db` by name, and states why it is skipped |
| `SystemPromptExtractor.cs` | FR-12: pulls `system.message.data.content` out of a RAW event, hashes it, and dedupes a batch down to its distinct texts |
| `RewindSnapshotSource.cs` | FR-13: reads `rewind-snapshots/index.json` as one whole-file RAW event at byte offset zero |
| `ToolArguments.cs` | FR-4's polymorphic `tool.execution_start.data.arguments` parser: `Object` / `String` / `Unparsed`, never coerced |
| `EventEnvelope.cs` | FR-8/FR-9 (issue #7): reads `id`/`parentId`/`agentId`/`data` out of a stored `RawEvent`'s own payload — separate from `EventEnvelopeParsers`, which only reads `type`/`timestamp` at RAW ingest time |
| `ExecutionRecordBuilder.cs` | FR-8/FR-9: rebuilds one session's `RawEvent`s into `Data.Execution.Turn`/`ToolCall`/`Agent` rows plus the causality map, pure and deterministic over `RawEvent.Sequence` order |
| `SpawnResolutionCheck.cs` | turns an `ExecutionRecordBuilder` agent-reconstruction pass into the `unresolvable-spawn` `CheckRegistryEntry` (issue #23, PRD §3.9's name for it) |
| `SessionRuleExtractor.cs` | FR-26 (S-19, issue #32): resolves a session's `<custom_instruction>` blocks from its own `system.message` `RawEvent`s — the only caller that supplies `Rules.RuleStatementExtractor` its prompt text from the ingested store rather than from a file |

## References

`Data` — it writes through the RAW store `Data` owns, and `ExecutionRecordBuilder` returns
`Data.Execution.Turn`/`ToolCall`/`Agent` records built from it.

`Findings` — for two purposes: `MalformedLineCheck` builds a `Findings.CheckRegistryEntry` so the
malformed-line check registers itself (S-02's own acceptance scenario), and `SpawnResolutionCheck`
does the same for FR-9's spawn-resolution check (this story's Scenario 4). Ingestion does not read
any other check or finding shape beyond those two.

`Rules` — since S-19 (issue #32), for `SessionRuleExtractor` alone: it calls
`RuleStatementExtractor.ExtractBlocks` with the text `SystemPromptExtractor.Extract` already
resolved from a `RawEvent`, so `<custom_instruction>` parsing itself stays in `Rules` (this
project's own CLAUDE.md, "Rule-set extraction and the check-shape catalogue") while the RAW-reading
half of the job — which events to feed it, and unioning a session's own blocks across all of
them — stays here, the same split `Findings` uses for every other check shape.

`AecoPostMortem.Api` references this project the other way, for `CopilotSourceLocation` and
`SessionDiscovery` — S-48's app-state diagnosis needs to know whether the Copilot session-state
root exists, and reuses FR-1's own discovery rather than a second, parallel `Directory.Exists`
check. See `AecoPostMortem.Api/CLAUDE.md`.

## Non-obvious decisions

### `SourceFiles.OpenRead` is the only door onto `~/.copilot/`

One place, so "the product never writes to the source" (PRD §3.8) is a property of the code rather
than of everyone who reads a file remembering it. Its share mode is
`FileShare.ReadWrite | FileShare.Delete`: `events.jsonl` is written live by a session that may still
be running, and a reader holding an exclusive share would make the source fail to write. Read-only
has to mean the product neither writes to the source nor stops it being written to.

Since S-07, the same method is also where FR-10's exclusion is enforced: it checks
`ExcludedSources.IsExcluded` before ever constructing the `FileStream` and throws instead, so "the
live database is never opened" is a property of the one door rather than of every future caller
remembering to check first.

### The assembly references no outbound transport, and a test says so

`NoOutboundTransportTests` reads assembly metadata rather than loading and reflecting, so a
networking type referenced but never reached is still a failure. §3.8 asks for no socket, not for no
telemetry, and the store holds the operator's prompts and source code.

### Provider version and event-schema version come from line 1, and only line 1

FR-3: `SessionEventReader` reads `session.start.data.copilotVersion` and `session.start.data.version`
off the first line and stops — it never scans further lines looking for a declaration. A first line
that is not `session.start`, is malformed, or is missing either field reads as
`SessionEventReader.UnknownProviderVersion` / a `null` schema version, not as a reason to keep
looking. A version — provider or schema — this product has not measured still ingests: an unknown
provider version is stored verbatim, and an unknown schema version falls back to
`EventEnvelopeParsers`' only registered parser (edge case: 14 distinct CLI versions already appear
in one corpus, all sharing schema version 1).

### A skipped line is never remembered as bad

`SessionEventReader.Read` re-reads the whole file on every call — there is no per-line skip cache
and no persisted "known bad" marker. That is what makes FR-6's retry rule true structurally rather
than by a mechanism that has to be kept correct: a line that was malformed on one run is attempted
again on the next, and if it has since completed (the file is live-written), it parses. RAW's own
identity index (`ux_raw_identity`) is what keeps a re-ingested line that already succeeded from being
inserted twice — `SessionIngestor` supplies no extra idempotency of its own.

### A detected rewrite refuses the whole read, not just the mismatched lines

`SessionIngestor.Ingest` calls `RawEventBatch.DetectRewrites` before `RawEventBatch.Append`, over
every event the current read produced. If even one event's byte offset disagrees with what RAW
already stores there, `Ingest` appends nothing from that read at all and returns the mismatches on
`SessionIngestResult.RewriteMismatches` (`RewriteDetected` is `true` and `EventsInserted` is `0`).
It does not append the events that don't collide and skip only the mismatched ones: once a file's
bytes at a previously-seen offset no longer match, the byte-offset identity assumption FR-5 rests
on (growth is append-only) is broken for that file, and nothing later in the same read can be
trusted as a genuine continuation rather than a coincidence. This is a refuse-and-report outcome,
not an exception — the same "reported, not thrown" shape `SessionDiscovery` uses for a missing
root — because a rewritten file is an operator-visible condition to investigate, not a defect in
this code path. `IngestionRun.Run` still counts a rewrite-refused session's lines toward
`LinesParsed`/`LinesSkipped` (they were genuinely read this run) but excludes it from
`SessionsIngested` and `EventsByType` — nothing from it reached RAW, so counting it as ingested
would misstate what the store actually holds after the run.

### A trailing line with no newline is unfinished, not malformed

`SessionEventReader` splits on `\n` only. The final unterminated segment, if any, is excluded from
both `LinesRead` and `Events` and never counted as skipped — `HighWaterOffset` stops at the byte
immediately after the last complete line, which is where the next read should logically continue
from. `events.jsonl` is written live, so a trailing partial line is normal, not an error.

### `session.db` is classified, not opened

`SessionDiscovery` records whether the file exists and nothing more (FR-1: v1 does not ingest it).
Classifying without reading keeps discovery itself allocation-light and keeps the "never write to,
never require opening the source's SQLite file" property easy to see from the code.

### `session-store.db` (global) and `session.db` (per-session) are different files, excluded for different reasons

`ExcludedSources` only ever names `session-store.db`, the global file FR-10 excludes: it is
live-written, WAL-dependent, covers a measured 7 of 40 sessions, and offers nothing any finding
class needs — per-request latency and nano-AIU only. The per-session `session.db` (todo rows) is a
different file with a different name; FR-1 classifies it but does not ingest it either, for a
different, unrelated reason (sparse and success-biased — `report_intent` supersedes it). Do not
fold the two into one exclusion rule; a future classifier for the per-session file belongs to
whichever story implements FR-1, not to `ExcludedSources`.

### System-prompt dedup needs no session-to-hash link table

`SystemPromptExtractor.Extract` is deterministic: the same `system.message` payload always hashes
to the same content hash. A session resolves its own full prompt text by re-running extraction over
its own RAW event and joining the hash against `SystemPromptText` (`AecoPostMortem.Data`) — cheaper
to recompute a SHA-256 once per read than to maintain a second table that could drift from RAW.
`SystemPromptExtractor.DistinctTexts` is what a future ingest pipeline calls before
`SystemPromptTextBatch.Append`, so a batch of events collapses to its distinct texts before the
append rather than relying on the `ON CONFLICT` alone to do all the work.

### `rewind-snapshots/index.json` is one event per read, not one event per file

`RewindSnapshotSource.ReadAsEvent` always reports `ByteOffset = 0` — the whole file is the event,
the same way a `.meta.json`-style source would be read, because it is a single JSON object rather
than a JSONL stream. It is rewritten in place as a session grows, so two reads of the same path can
carry different content and therefore different content hashes; RAW's identity triple
`(source_file, byte_offset, content_hash)` is what keeps both versions rather than one silently
overwriting the other (FR-13). This method takes `sessionId`, `sequence`, `timestamp` and
`providerVersion` as parameters rather than reading them from the file, because the index carries no
envelope of its own to lift them from — a future ingest pipeline supplies them from the session
context it already has.

### `arguments` is parsed polymorphically, and a third shape is never coerced (FR-4)

`ToolArguments.Parse` classifies the value's own JSON text as `Object` (every tool but
`apply_patch`), `String` (`apply_patch`'s whole patch envelope — a projection that assumes an object
here silently drops the patch, PRD §3.9's first listed failure mode), or `Unparsed` for anything
else. `Unparsed` exists so a future tool arriving with a third argument shape is recorded rather than
guessed at — `Raw` still preserves its text either way. `TryGetProperty` and `AsText` each throw if
called against the wrong `Kind`, rather than returning a default that would look like a real absence.

`ToolArguments` is a standalone, self-contained parsing unit — it does not yet plug into the
`RawEvent`-to-`ToolCall` pipeline; a later story wires `ToolArguments.Parse` in where
`tool.execution_start.data.arguments` is read.

### The spawn resolution key is a `toolCallId`, not a separately allocated agent id

The data map's non-circular derivation (Appendix) measured that Copilot reuses one value for both
roles: the spawning `task` call's own `toolCallId` *is* the value `subagent.started.data.toolCallId`
later reports for the agent it produced. `ExecutionRecordBuilder` resolves a spawn by looking that
same id up in a map keyed by every `task` `tool.execution_start`'s own `toolCallId` — not by any
other correlation — and nesting falls out of the same lookup: the value stored is the `agentId` that
task call itself carried, `null` for a spawn from the main thread. A `subagent.started` whose
`toolCallId` never appears as a `task` call's `toolCallId` is excluded from the returned agents
(`Agent.SpawningToolCallId` is `required`, so an unresolved spawn cannot honestly populate it) and
counted instead in `SpawnResolutionCheck` — reported, never silently dropped (Scenario 3).

Scenario 3's own `Given` is the reference corpus, so it is also asserted there:
`test/AecoPostMortem.Ingestion.Tests/ExecutionRecordCorpusTests.cs` rebuilds every ingested session
off the shared `CorpusIngestFixture` and checks that the spawns examined equal
`fixtures/corpus-manifest.json`'s own `subagent.started` census (470 today, read from the manifest
rather than typed into the test) with zero unresolved. Both halves matter: a builder that resolved
every spawn it looked at but never looked at most of them would pass an unresolved-count check
alone. Hand-built events prove the resolution rule; only the real bytes prove the 470-of-470 claim —
the same argument the `ts`/`timestamp` entry below makes at length.

### `Agent.Outcome` reads `subagent.completed`'s four cost fields as one unit

If none of `totalTokens`, `totalToolCalls`, `durationMs` and `model` are present, the outcome is
`CompletedCostUnknown`, not `Completed` with zeroes — zero is a number a surface would print, and
the measured 247 of 462 completions that carry none of the four would otherwise be priced at
nothing. Any one of the four present is enough to call it `Completed`; the constraint that only
`Completed` may carry non-null cost columns (`ck_agent_cost`, `NORMALIZED_MODEL.md`) does not require
all four together, only that the other three outcomes carry none. `subagent.failed` takes priority
over a `subagent.completed` for the same agent if both are somehow present in one session — not
measured in the reference corpus, so this is a documented judgment call, not an observed rule.

### A turn's tool calls are found by "what was open when", not by a field on the call

Neither `tool.execution_start` nor `_complete` names a `turnId` — only `assistant.turn_start`/
`turn_end` do. `ExecutionRecordBuilder` tracks the currently open turn while walking a session's
events in `Sequence` order and records it per event; `ToolCall.TurnId` is that snapshot, and only
for a main-thread call — the data map measured zero `agentId` on every turn boundary event, so a
subagent's own tool calls have no turn to belong to and its `TurnId` is always `null`. An `abort`
event closes the currently open turn as `Aborted`: the measured 9-event gap between 2,384 turn
starts and 2,375 ends equals the measured 9 `abort` events one-for-one, which is why `abort` is read
as "this turn's real end", not as an unrelated event.

### `ResultSizeBytes` reads only `result.content`, not the `toolTelemetry` fallback

The data map names two sources for a tool call's result size: `result.content` length (measured 98%
coverage) and `toolTelemetry.metrics.resultLength` (measured 39%). `ExecutionRecordBuilder` reads
only the first — a deliberate scope cut for this story, not an oversight, since no acceptance
scenario needs the last few percent. A future story that needs `ResultSizeBytes` on a call missing
`result.content` should add the `toolTelemetry` fallback to `GetResultSizeBytes` rather than treating
its absence as a gap in this one.

### `ExecutionRecordBuilder` is a pure in-memory builder, not yet wired into the store

It takes `RawEvent`s and returns `Data.Execution` records plus a `CheckRegistryEntry` — nothing here
opens a `PostMortemContext` or writes a derived table. None of this story's acceptance scenarios ask
for persistence, and `DerivedSchema.Rebuild`/`EnsureCurrent` (`AecoPostMortem.Data/CLAUDE.md`)
already leave the derived tables empty on purpose, pending a reader. Wiring this builder's output
into those tables — the actual `rebuild`-populates-rows step — is left for whichever later story
does that wiring; keeping the two concerns separate mirrors `SessionIngestor` staying RAW-only.

### `SessionRuleExtractor` never opens a file — its only input is `RawEvent`

Scenario 3 (issue #32) requires rule extraction to read only the ingested store, never a markdown
file on disk. `SessionRuleExtractor.Extract` takes `(string sessionId, IEnumerable<RawEvent>
sessionEvents)` — there is no path parameter for it to read a file from even if it wanted to. It
delegates prompt-text resolution to the already-existing `SystemPromptExtractor.Extract` (one RAW
event in, text or nothing out) and hands that text to `Rules.RuleStatementExtractor.ExtractBlocks`,
which is itself proven never to touch disk by
`test/AecoPostMortem.Containment.Tests/RuleExtractionNeverReadsDiskTests.cs` (a textual scan of
`AecoPostMortem.Rules`, the same technique `DeterminismInvariantTests` uses for the clock/chance/model
invariant). `SessionRuleExtractorTests` proves the behavioural half: extraction succeeds against
`RawEvent`s built entirely in memory, with no session-state directory ever created by the test.

### A session's blocks are unioned across its own `system.message` events, not deduplicated within it

A session can carry 1–3 distinct system-prompt texts (data map Part 6) — a mid-session context reset
can change the surrounding prompt while repeating the same injected files. `SessionRuleExtractor.Extract`
therefore calls `RuleStatementExtractor.ExtractBlocks` once per `system.message` event and
concatenates every block it returns, rather than picking "the" prompt text for the session. A
statement repeated across two of a session's own events still counts as one occurrence for that
session — that collapse is `RuleStatementDeduplication.Deduplicate`'s job (`AecoPostMortem.Rules`),
which tracks session ids in a set, not this method's.

### FR-7's exclusion is a plain cwd-prefix match, deliberately, and it compensates with visibility rather than precision

A path match alone cannot distinguish an analysis session run from a repository from ordinary
feature work also run from it — both share the same `session.start.data.context.cwd` (FR-7's own
stated risk). `SessionExclusion.Evaluate` does not try to solve that with anything cleverer; it
matches cwd against an operator-configured list of excluded roots, boundary-checked so a sibling
directory sharing a name prefix (`/repo` vs `/repository`) never matches, and separator-normalised
(`\` and `/` compare the same) so a cwd recorded on one platform still compares correctly against a
root configured on another. The list is configurable (`ExclusionListSource`, Scenario 2) and every
excluded session is named with its reason (`ExcludedSession.Reason`, FR-14) precisely because the
match itself cannot be made more precise — the operator corrects over- or under-exclusion by editing
the list, not by this code guessing better. An unknown cwd (no `session.start` first event, or one
missing `context.cwd`) is never excluded, against any list: this product cannot exclude a session it
cannot place.

### `ExclusionListSource` re-reads the config file every call; there is no cache

Scenario 2 ("a path added to the list is honoured without rebuilding the product") is true
structurally rather than by a mechanism that has to stay correct, because `Load` never caches what
it read. The file sits beside the store (`StoreLocation.DefaultFolder`, `exclusions.json`) rather
than under the Copilot source tree, since it is this product's own operator configuration, not
something Copilot writes. When no file exists, the default is this product's own repository root
(FR-7: "defaulting to this product's own repository root"), found by walking upward from
`AppContext.BaseDirectory` for `AecoPostMortem.sln` — the same bounded, `null`-on-miss walk
`AecoPostMortem.Cli.ServeWebRoot.Resolve` already uses to find `web/dist`. On a machine with no
checkout to find (an installed build), the default is an empty list, not a guess. Malformed JSON in
an existing file reads as no exclusions rather than throwing or falling back to the default — an
ingest run should not fail outright, or silently reassert a root the operator may be mid-edit to
remove, over a config file that does not parse.

### Exclusion is checked before the rewrite check and the append, and it purges retroactively

`SessionIngestor.Ingest` evaluates `SessionExclusion` immediately after reading the file — ahead of
`RawEventBatch.DetectRewrites` and `RawEventBatch.Append` both — so "at ingest, not as a later
filter" (FR-7's own phrasing) is true because no code path appends an excluded session's events and
filters them out afterwards; `SessionIngestResult.EventsInserted` is always `0` when
`Exclusion.Excluded` is `true`. Issue #6's edge case is retroactive, not only prospective: a session
that was already ingested before its cwd was added to the exclusion list is not merely refused on
the next read — `RawEventBatch.DeleteBySession` removes what RAW already holds for it, and
`SessionIngestResult.PurgedEventCount` reports how much. The excluded-vs-rewrite-refused distinction
matters for `IngestionRun`'s coverage counts: a rewrite-refused session is not FR-7's concern (it is
FR-5's), so it is not added to `CoverageReport.SessionsExcluded` — only a genuine exclusion is.

### `IngestionRun` is a thin walk over `SessionDiscovery` and `SessionIngestor`; it owns no store access of its own

`IngestionRun.Run` never opens a connection or issues SQL directly — every row it causes to be
written or removed goes through `SessionIngestor.Ingest`, the same per-session door a single-session
caller uses, so the `CoverageReport` it returns can never disagree with what was actually persisted.
"Sessions found" is `SessionDiscovery`'s full classified count, including a directory with no
`events.jsonl` (FR-1's "classified and skipped" case) — that session is neither ingested nor
excluded, the same way `SessionDiscovery` already reports it as seen but not read.
`CoverageReport.EventsByType` counts only what a non-excluded session's read produced, including a
duplicate re-ingest's already-stored events (consistent with `LinesParsed` also counting those) —
an excluded session's events are never counted, because they were never persisted (Scenario 3: no
query-time filter is needed precisely because there is nothing in the store to filter).

### The corpus round-trip is a build gate, not a unit test

`test/AecoPostMortem.Ingestion.Tests/ApplyPatchCorpusRoundTripTests.cs` parses and re-serialises
every `apply_patch` call in the *live* reference corpus (a measured 381 calls) through
`ToolArguments`, because `ToolArgumentsTests`'s hand-picked strings prove the algorithm but not that
the real corpus never trips it. The corpus bytes are not checked in (`fixtures/README.md`), so the
test resolves the live source directory through `ReferenceCorpus` (`fixtures/corpus-manifest.json`'s
own `source` field, overridable by `AECOPOSTMORTEM_CORPUS_SOURCE`) and **skips**, not fails, on a
machine that doesn't have it — the gate only bites where the corpus actually is.
`scripts/check-apply-patch-roundtrip.py` is the CI entry point: it runs that one test in isolation
and forwards its exit code, the same shape as `freeze-corpus-manifest.py --check`.
`test/AecoPostMortem.Ingestion.Tests/ReferenceCorpus.cs` is the corpus-source resolution both this
test and `CorpusVerificationTests` share.

### `EventEnvelopeParserV1` reads `timestamp`, not `ts` — full-corpus verification is why

`test/AecoPostMortem.Ingestion.Tests/CorpusVerificationTests.cs` drives a real full ingest of the
live reference corpus and checks the resulting RAW census against `fixtures/corpus-manifest.json`.
That check demonstrated `EventEnvelopeParserV1.TryParse` required a `"ts"` property that does not
exist anywhere in the corpus — every real event carries `"timestamp"` instead, confirmed against
multiple session files (zero `"ts":` matches, one match per line for `"timestamp":`). Every
hand-crafted fixture in `SessionIngestorTests`, `SessionEventReaderTests` and
`EventEnvelopeReaderTests` had independently guessed the same wrong field name, so no unit test
caught it, and `ApplyPatchCorpusRoundTripTests` doesn't either — it parses envelopes itself rather
than going through `EventEnvelopeParsers`. The parser reading a field that never occurs in the
corpus means a real ingest skips every line as malformed: `SessionEventReader`'s "a line that fails
to parse is skipped and counted, never fatal" behaviour (FR-6) makes total data loss look like a
clean, low-noise run rather than a hard failure. All three fixture files carry `"timestamp"` now,
matching the real event shape. This is the argument for running a fixture gate against real corpus
bytes rather than only against hand-picked JSON: a bug every synthetic fixture agrees on by
construction is invisible to a suite that never reads real data.

## Status

`EventEnvelopeParsers`), RAW persistence and idempotent, rewrite-safe re-ingest (`SessionIngestor`,
FR-5, issue #5), the malformed-line check (`MalformedLineCheck`), volume control (`ExcludedSources`,
`SystemPromptExtractor`, `RewindSnapshotSource`), the polymorphic `arguments` parser
(`ToolArguments`), execution-record reconstruction (`ExecutionRecordBuilder`, `EventEnvelope`,
`SpawnResolutionCheck`), rule-statement resolution (`SessionRuleExtractor`, S-19, issue #32) and
self-exclusion plus the coverage report (`SessionStartContext`, `SessionExclusion`,
`ExclusionListSource`, `CoverageReport`, `IngestionRun`, FR-7/FR-14, S-05, issue #6) exist as
composable building blocks (FR-1, FR-3, FR-4, FR-5, FR-6, FR-7, FR-8, FR-9, FR-10, FR-12, FR-13,
FR-14, FR-26). `ExecutionRecordBuilder` is the first caller of `ToolArguments` — it uses it to pull
`path` out of an object-shaped `arguments` value. The `ingest` CLI command still reports "not
implemented" (`AecoPostMortem.Cli`) — wiring `IngestionRun.Run` and `ExecutionRecordBuilder` into
the actual command (reading `CoverageReport` to stdout per FR-58, and populating the derived tables)
is a later story; this project's job is the composable pieces the CLI will call. `SessionRuleExtractor`
likewise resolves one session's own `RawEvent`s, already in hand — nothing yet walks the whole store
calling it per session and feeding the results into `Rules.RuleStatementDeduplication.Deduplicate`;
that corpus-wide wiring, and rule-set versioning by content hash (FR-27), are S-20's job.

Phase A's exit criterion is verified against the frozen fixture corpus
(`test/AecoPostMortem.Ingestion.Tests/CorpusVerificationTests.cs`,
`scripts/check-corpus-verification.py`): the RAW event census matches
`fixtures/corpus-manifest.json`'s per-type counts exactly, every RAW row re-serialises
byte-identically to its own source line, a full ingest of the 35-session corpus completes in a
measured ~14s (PRD §3.7's target: under 3 minutes), and an incremental re-ingest with no new events
completes in a measured ~6s (target: under 15 seconds) — both comfortably inside target on this
machine, though PRD §3.7 states these as targets rather than measurements, so a corpus that grows
toward the 500-session/1M-event design target is not guaranteed the same margin. Driving a "full
ingest" needs no dedicated production code: `CorpusIngestFixture` walks
`SessionDiscovery.Discover` and calls `SessionIngestor.Ingest` per session directly, the same
composable building blocks a future `ingest` CLI command wraps — this verification does not itself
wire that command.
