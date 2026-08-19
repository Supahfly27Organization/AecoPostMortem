# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion.

## Structure

| File | What it holds |
|---|---|
| `SourceFiles.cs` | the only door onto `~/.copilot/`: read-only, non-exclusive |
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

### The assembly references no outbound transport, and a test says so

`NoOutboundTransportTests` reads assembly metadata rather than loading and reflecting, so a
networking type referenced but never reached is still a failure. §3.8 asks for no socket, not for no
telemetry, and the store holds the operator's prompts and source code.

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

The read-only source opener and the polymorphic `arguments` parser exist. Path discovery, the
event-line reader and session reconstruction land next.
