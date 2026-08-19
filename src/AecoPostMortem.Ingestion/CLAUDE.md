# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion.

## Structure

| File | What it holds |
|---|---|
| `SourceFiles.cs` | the only door onto `~/.copilot/` — read-only, non-exclusive `FileStream` |
| `SessionDiscovery.cs` | FR-1: finds every session directory under the session-state root and classifies its files (`events.jsonl`, `session.db`, `rewind-snapshots/index.json`, `workspace.yaml`) without reading any of them; a missing root is reported, not thrown |
| `SessionEventReader.cs` | FR-3/FR-6: reads one `events.jsonl` line by line into `RawEvent`s — provider version and event-schema version from line 1 only, malformed lines skipped and counted, a trailing unterminated line stops the read and reports the high-water offset |
| `EventEnvelopeParsers.cs` | `IEventEnvelopeParser`, the envelope-field (`type`/`ts`) reader, and the version-keyed registry `SessionEventReader` selects from — falls back to the one shape measured today for a schema version it has not seen |
| `SessionIngestor.cs` | reads a session file and lands what parsed in RAW via `RawEventBatch.Append` |
| `MalformedLineCheck.cs` | turns one or more `SessionReadResult`s into the malformed-line `CheckRegistryEntry` (issue #23) |

## References

`Data` — it writes through the RAW store `Data` owns.

`Findings` — for exactly one purpose: `MalformedLineCheck` builds a `Findings.CheckRegistryEntry`
so the malformed-line check registers itself (this story's own acceptance scenario). Ingestion does
not call into `Rules` and does not read any other check or finding shape; reconstructing sessions
from raw events still needs nothing from either.

## Non-obvious decisions

### `SourceFiles.OpenRead` is the only door onto `~/.copilot/`

One place, so "the product never writes to the source" (PRD §3.8) is a property of the code rather
than of everyone who reads a file remembering it. Its share mode is
`FileShare.ReadWrite | FileShare.Delete`: `events.jsonl` is written live by a session that may still
be running, and a reader holding an exclusive share would make the source fail to write. Read-only
has to mean the product neither writes to the source nor stops it being written to.

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

## Status

Path discovery (`SessionDiscovery`), the event-line reader (`SessionEventReader`,
`EventEnvelopeParsers`), RAW persistence (`SessionIngestor`) and the malformed-line check
(`MalformedLineCheck`) exist (FR-1, FR-3, FR-6 — issue #3 / S-02). Polymorphic tool-argument parsing
(FR-4), the coverage report and self-exclusion (FR-7/FR-14), and execution-record reconstruction
(FR-8/FR-9) land with the stories that follow.
