# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion,
volume control (FR-10, FR-12, FR-13).

## Structure

| File | What it holds |
|---|---|
| `SourceFiles.cs` | the only door onto `~/.copilot/`; refuses `ExcludedSources`-excluded paths before any OS-level open |
| `ExcludedSources.cs` | FR-10: recognises Copilot's global `session-store.db` by name, and states why it is skipped |
| `SystemPromptExtractor.cs` | FR-12: pulls `system.message.data.content` out of a RAW event, hashes it, and dedupes a batch down to its distinct texts |
| `RewindSnapshotSource.cs` | FR-13: reads `rewind-snapshots/index.json` as one whole-file RAW event at byte offset zero |
| `ToolArguments.cs` | FR-4's polymorphic `tool.execution_start.data.arguments` parser: `Object` / `String` / `Unparsed`, never coerced |

## References

`Data` — it writes through the RAW store `Data` owns, and nothing else: ingestion has no reason to
see rule checks or findings, only to land raw events and reconstruct sessions from them.

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

`ToolArguments` is a standalone, self-contained parsing unit — it does not yet plug into a
`RawEvent`-to-`ToolCall` pipeline, because that event-line parsing loop (S-02, story `issue-3`) had
not landed in this worktree when this story was implemented. A later merge wires
`ToolArguments.Parse` in where `tool.execution_start.data.arguments` is read.

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

`SourceFiles`, `ExcludedSources`, `SystemPromptExtractor`, `RewindSnapshotSource` and
`ToolArguments` exist as composable building blocks with no orchestrating pipeline yet — the
`ingest` CLI command still reports "not implemented" (`AecoPostMortem.Cli`); wiring these into an
actual directory walk and a `RawEvent`-to-`ToolCall` pipeline is a later story (S-02's discovery, or
whichever story lands the `ingest` command's body). Path discovery, the event-line reader and
session reconstruction land next.
