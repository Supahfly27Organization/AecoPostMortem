# Domain Model

> **Scope:** every entity the store holds — its key, its columns, its indexes and the invariants
> that are not visible from the C# alone.
> **Read when:** adding or changing an entity, writing a query, or checking what a column means
> before trusting it.

The three layers are RAW, NORMALIZED and FINDINGS (PRD §3.2). Only RAW exists today. Only RAW ever
carries a migration — the other two are re-derived from it, and a migration against them is a defect
(Repo Rule 4, PRD §3.8).

## RawEvent — table `raw_event`

PK: `Id` (`long`, `id INTEGER`). One Copilot event line, preserved exactly as it was read (FR-2).

| Property | Type | DB | Notes |
|---|---|---|---|
| `Id` | `long` | `id INTEGER PRIMARY KEY` | surrogate, assigned by SQLite; carries no meaning and nothing outside the store keys on it |
| `SessionId` | `string` | `session_id TEXT NOT NULL` | the session directory the line came from |
| `Sequence` | `long` | `seq INTEGER NOT NULL` | the line's position in its file, from zero |
| `EventType` | `string` | `event_type TEXT NOT NULL` | the envelope's `type`; a measured 31 distinct values in the frozen corpus |
| `Timestamp` | `string` | `ts TEXT NOT NULL` | the event's own timestamp, verbatim. All temporal ordering derives from it — §3.8 forbids a wall-clock dependency |
| `ProviderVersion` | `string` | `provider_version TEXT NOT NULL` | `session.start.data.copilotVersion`; a measured 14 distinct values across the corpus |
| `SourceFile` | `string` | `source_file TEXT NOT NULL` | identity, part 1 |
| `ByteOffset` | `long` | `byte_offset INTEGER NOT NULL` | identity, part 2. Measured stable: on all 8 events carrying `eventsFileSizeBytes` the declared value equals the offset, delta 0 |
| `ContentHash` | `string` | `content_hash TEXT NOT NULL` | identity, part 3. SHA-256 of the line's bytes, lower-case hex |
| `Payload` | `string` | `payload TEXT NOT NULL` | the whole line, not a projection of it. Unknown fields are preserved, never dropped |

### Indexes

| Index | Columns | Unique | Why it exists |
|---|---|---|---|
| `ux_raw_identity` | `source_file, byte_offset, content_hash` | yes | FR-2's identity triple. The append is `ON CONFLICT … DO NOTHING` against it, which is what makes re-ingesting the same log add nothing (FR-5) |
| `ix_raw_session_seq` | `session_id, seq` | no | the Flight Recorder's tape — a session's events in order |
| `ix_raw_type` | `event_type` | no | the event census, counted by type |

Measured, not tuned: without the covering indexes the per-tool aggregate ran 776.06 ms against
56.15 ms on Postgres — a measured 13.8× — and 64.34 ms with them
(`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md` Part 3). A
missing index fails `SchemaTests` rather than degrading a surface quietly.

### Invariants

- **The payload round-trips byte-identically**, unknown fields included. That is Phase A's exit
  criterion (PRD §3.5), so `payload` is written from a strict UTF-8 decode: a line whose bytes are
  not valid UTF-8 is refused, not stored with a substitution character.
- **Identity is the triple, not the payload.** The same bytes at a different offset are a different
  event; the corpus repeats identical lines and collapsing them would lose real events.
- **`session_id`, `seq`, `event_type` and `ts` are not a second copy of the truth.** The payload
  stays authoritative. Those four exist because the measured read path indexes them, and an index
  cannot be built over a value that exists only inside a JSON string.
- **RAW rows are never rewritten.** The append is the only writer.
