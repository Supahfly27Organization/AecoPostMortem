# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion,
volume control (FR-10, FR-12, FR-13), rule-statement resolution from the store (FR-26).

## Structure

| File | What it holds |
|---|---|
| `SourceFiles.cs` | the only door onto `~/.copilot/`; refuses `ExcludedSources`-excluded paths before any OS-level open |
| `SessionDiscovery.cs` | FR-1: finds every session directory under the session-state root and classifies its files (`events.jsonl`, `session.db`, `rewind-snapshots/index.json`, `workspace.yaml`) without reading any of them; a missing root is reported, not thrown |
| `SessionEventReader.cs` | FR-3/FR-6: reads one `events.jsonl` line by line into `RawEvent`s — provider version and event-schema version from line 1 only, malformed lines skipped and counted, a trailing unterminated line stops the read and reports the high-water offset |
| `EventEnvelopeParsers.cs` | `IEventEnvelopeParser`, the envelope-field (`type`/`ts`) reader, and the version-keyed registry `SessionEventReader` selects from — falls back to the one shape measured today for a schema version it has not seen |
| `SessionIngestor.cs` | reads a session file and lands what parsed in RAW via `RawEventBatch.Append` |
| `MalformedLineCheck.cs` | turns one or more `SessionReadResult`s into the malformed-line `CheckRegistryEntry` (issue #23) |
| `ExcludedSources.cs` | FR-10: recognises Copilot's global `session-store.db` by name, and states why it is skipped |
| `SystemPromptExtractor.cs` | FR-12: pulls `system.message.data.content` out of a RAW event, hashes it, and dedupes a batch down to its distinct texts |
| `RewindSnapshotSource.cs` | FR-13: reads `rewind-snapshots/index.json` as one whole-file RAW event at byte offset zero |
| `ToolArguments.cs` | FR-4's polymorphic `tool.execution_start.data.arguments` parser: `Object` / `String` / `Unparsed`, never coerced |
| `SessionRuleExtractor.cs` | FR-26 (S-19, issue #32): resolves a session's `<custom_instruction>` blocks from its own `system.message` `RawEvent`s — the only caller that supplies `Rules.RuleStatementExtractor` its prompt text from the ingested store rather than from a file |

## References

`Data` — it writes through the RAW store `Data` owns.

`Findings` — for exactly one purpose: `MalformedLineCheck` builds a `Findings.CheckRegistryEntry`
so the malformed-line check registers itself (this story's own acceptance scenario). Ingestion does
not read any other check or finding shape; reconstructing sessions from raw events still needs
nothing else from it.

`Rules` — since S-19 (issue #32), for `SessionRuleExtractor` alone: it calls
`RuleStatementExtractor.ExtractBlocks` with the text `SystemPromptExtractor.Extract` already
resolved from a `RawEvent`, so `<custom_instruction>` parsing itself stays in `Rules` (this
project's own CLAUDE.md, "Rule-set extraction and the check-shape catalogue") while the RAW-reading
half of the job — which events to feed it, and unioning a session's own blocks across all of
them — stays here, the same split `Findings` uses for every other check shape.

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
`EventEnvelopeParsers`), RAW persistence (`SessionIngestor`), the malformed-line check
(`MalformedLineCheck`), volume control (`ExcludedSources`, `SystemPromptExtractor`,
`RewindSnapshotSource`), the polymorphic `arguments` parser (`ToolArguments`) and rule-statement
resolution (`SessionRuleExtractor`, S-19, issue #32) exist as composable building blocks (FR-1, FR-3,
FR-4, FR-6, FR-10, FR-12, FR-13, FR-26). `ToolArguments` is not yet wired into the event-line
reader's `RawEvent`-to-`ToolCall` reconstruction. The `ingest` CLI command still reports "not
implemented" (`AecoPostMortem.Cli`) — wiring these into an actual directory walk is a later story.
`SessionRuleExtractor` likewise resolves one session's own `RawEvent`s, already in hand — nothing
yet walks the whole store calling it per session and feeding the results into
`Rules.RuleStatementDeduplication.Deduplicate`; that corpus-wide wiring, and rule-set versioning by
content hash (FR-27), are S-20's job. The coverage report and self-exclusion (FR-7/FR-14) and
execution-record reconstruction (FR-8/FR-9) land with the stories that follow.
