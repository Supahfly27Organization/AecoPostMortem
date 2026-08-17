# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion.

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

## Status

The read-only source opener exists. Path discovery, the event-line reader and session
reconstruction land next.
