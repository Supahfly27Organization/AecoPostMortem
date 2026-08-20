# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion,
volume control (FR-10, FR-12, FR-13).

## Structure

| File | What it holds |
|---|---|
| `SourceFiles.cs` | the only door onto `~/.copilot/`; refuses `ExcludedSources`-excluded paths before any OS-level open |
| `SessionDiscovery.cs` | FR-1: finds every session directory under the session-state root and classifies its files (`events.jsonl`, `session.db`, `rewind-snapshots/index.json`, `workspace.yaml`) without reading any of them; a missing root is reported, not thrown |
| `SessionEventReader.cs` | FR-3/FR-6: reads one `events.jsonl` line by line into `RawEvent`s — provider version and event-schema version from line 1 only, malformed lines skipped and counted, a trailing unterminated line stops the read and reports the high-water offset |
| `EventEnvelopeParsers.cs` | `IEventEnvelopeParser`, the envelope-field (`type`/`ts`) reader, and the version-keyed registry `SessionEventReader` selects from — falls back to the one shape measured today for a schema version it has not seen |
| `SessionIngestor.cs` | reads a session file and lands what parsed in RAW via `RawEventBatch.Append`; FR-5 (issue #5): runs `RawEventBatch.DetectRewrites` first and refuses the append — reporting the mismatch on `SessionIngestResult.RewriteMismatches`/`RewriteDetected` instead — the moment a rewrite is found |
| `MalformedLineCheck.cs` | turns one or more `SessionReadResult`s into the malformed-line `CheckRegistryEntry` (issue #23) |
| `ExcludedSources.cs` | FR-10: recognises Copilot's global `session-store.db` by name, and states why it is skipped |
| `SystemPromptExtractor.cs` | FR-12: pulls `system.message.data.content` out of a RAW event, hashes it, and dedupes a batch down to its distinct texts |
| `RewindSnapshotSource.cs` | FR-13: reads `rewind-snapshots/index.json` as one whole-file RAW event at byte offset zero |
| `ToolArguments.cs` | FR-4's polymorphic `tool.execution_start.data.arguments` parser: `Object` / `String` / `Unparsed`, never coerced |
| `EventEnvelope.cs` | FR-8/FR-9 (issue #7): reads `id`/`parentId`/`agentId`/`data` out of a stored `RawEvent`'s own payload — separate from `EventEnvelopeParsers`, which only reads `type`/`ts` at RAW ingest time |
| `ExecutionRecordBuilder.cs` | FR-8/FR-9: rebuilds one session's `RawEvent`s into `Data.Execution.Turn`/`ToolCall`/`Agent` rows plus the causality map, pure and deterministic over `RawEvent.Sequence` order |
| `SpawnResolutionCheck.cs` | turns an `ExecutionRecordBuilder` agent-reconstruction pass into the `unresolvable-spawn` `CheckRegistryEntry` (issue #23, PRD §3.9's name for it) |

## References

`Data` — it writes through the RAW store `Data` owns, and `ExecutionRecordBuilder` returns
`Data.Execution.Turn`/`ToolCall`/`Agent` records built from it.

`Findings` — for two purposes: `MalformedLineCheck` builds a `Findings.CheckRegistryEntry` so the
malformed-line check registers itself (S-02's own acceptance scenario), and `SpawnResolutionCheck`
does the same for FR-9's spawn-resolution check (this story's Scenario 4). Ingestion does not call
into `Rules` and does not read any other check or finding shape.

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
this code path.

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

### The corpus round-trip is a build gate, not a unit test

`test/AecoPostMortem.Ingestion.Tests/ApplyPatchCorpusRoundTripTests.cs` parses and re-serialises
every `apply_patch` call in the *live* reference corpus (a measured 381 calls) through
`ToolArguments`, because `ToolArgumentsTests`'s hand-picked strings prove the algorithm but not that
the real corpus never trips it. The corpus bytes are not checked in (`fixtures/README.md`), so the
test reads the live source directory recorded in `fixtures/corpus-manifest.json`'s own `source`
field (overridable by `AECOPOSTMORTEM_CORPUS_SOURCE`) and **skips**, not fails, on a machine that
doesn't have it — the gate only bites where the corpus actually is.
`scripts/check-apply-patch-roundtrip.py` is the CI entry point: it runs that one test in isolation
and forwards its exit code, the same shape as `freeze-corpus-manifest.py --check`.

## Status

Path discovery (`SessionDiscovery`), the event-line reader (`SessionEventReader`,
`EventEnvelopeParsers`), RAW persistence and idempotent, rewrite-safe re-ingest (`SessionIngestor`,
FR-5, issue #5), the malformed-line check (`MalformedLineCheck`), volume control (`ExcludedSources`,
`SystemPromptExtractor`, `RewindSnapshotSource`), the polymorphic `arguments` parser
(`ToolArguments`) and execution-record reconstruction (`ExecutionRecordBuilder`, `EventEnvelope`,
`SpawnResolutionCheck`) exist as composable building blocks (FR-1, FR-3, FR-4, FR-5, FR-6, FR-8,
FR-9, FR-10, FR-12, FR-13). `ExecutionRecordBuilder` is the first caller of `ToolArguments` — it uses
it to pull `path` out of an object-shaped `arguments` value. The `ingest` CLI command still reports
"not implemented" (`AecoPostMortem.Cli`) — wiring path discovery, the event reader,
`SessionIngestor` and `ExecutionRecordBuilder` into an actual directory walk that also populates the
derived tables is a later story. The coverage report and self-exclusion (FR-7/FR-14) land with the
story that follows.
