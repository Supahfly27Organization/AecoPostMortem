# AecoPostMortem.Data

The `DbContext`, the entity model and the EF Core migrations; the only project that owns the schema.

## Structure

| File | What it holds |
|---|---|
| `RawEvent.cs` | the RAW row: the verbatim line plus its provenance |
| `RawEventSchema.cs` | the physical table, column and index names, stated once |
| `RawPayload.cs` | strict UTF-8 decode, and FR-2's content hash |
| `PostMortemContext.cs` | the model, its column mapping and its indexes |
| `StoreMetadata.cs` | the store's own key/value state — migrated, not derived; holds `DerivedSchemaVersionKey` |
| `RawEventBatch.cs` | the batched raw-SQL append, plus `DetectRewrites` (FR-5, issue #5): read-only rewrite detection — a different content hash at an already-stored `(source_file, byte_offset)` — and `RawRewriteMismatch`, its result row |
| `SystemPromptText.cs` | FR-12's dedup row: system-prompt text keyed by its own content hash — migrated, not derived, for the same reason as `StoreMetadata` |
| `SystemPromptTextSchema.cs` | the physical table and column names for the dedup table, stated once |
| `SystemPromptTextBatch.cs` | the batched raw-SQL append for dedup rows (Repo Rule 5) |
| `LocalStore.cs` | the store as a file: path, creation, size, purge |
| `StoreLocation.cs` | FR-11's documented per-user path |
| `OwnerOnlyAccess.cs` | owner-only permissions, per platform |
| `Execution/IDerivedEntity.cs` | empty marker interface for the NORMALIZED/FINDINGS layers |
| `Execution/Session.cs` | the first derived entity: one Copilot session, keyed by `SessionId` |
| `Execution/IOwned.cs` | `OwnerKind` (`Main`/`Agent`) and the `IOwned` contract every subagent-ownable derived entity carries |
| `Execution/Turn.cs` | one assistant turn, bounded by `assistant.turn_start`/`turn_end`, keyed by `(SessionId, TurnId)` |
| `Execution/ToolCall.cs` | one tool invocation, bounded by `tool.execution_start`/`execution_complete`, keyed by `(SessionId, ToolCallId)`; carries its five measured indexes |
| `Execution/Agent.cs` | one subagent, keyed by `(SessionId, AgentId)`; carries `AgentOutcome`'s four completion states instead of `IOwned` — it is the owner |
| `Execution/EventScopedEntities.cs` | `Skill`, `Hook`, `Permission`, `WriteUnit` — the four shapes Copilot writes no natural id for, each keyed `(SessionId, EventId)` off the envelope's own `id`; completes the contract's eight shapes |
| `Execution/DerivedSchema.cs` | hand-generated DDL for the eight derived tables, a SHA-256 version over that DDL, `EnsureCurrent` (create/drop/version on open) and `Rebuild` (unconditional drop-and-recreate, what `aecopostmortem rebuild` calls — S-46, issue #24) |
| `Migrations/` | generated; do not read unless the task is about migrations (Repo Rule 1) |

## References

`Microsoft.EntityFrameworkCore.Sqlite` and nothing of this repository's own. It is the base of the
dependency graph — `Ingestion`, `Findings`, `Api` and `Cli` reach it, so it cannot depend back on
any of them without creating a cycle.

`SQLitePCLRaw.bundle_e_sqlite3` is pinned above what the provider drags in, because the inherited
version carries a published advisory and `NU1903` is an error here.

## Non-obvious decisions

### RAW appends bypass EF Core change tracking

`RawEventBatch` issues multi-row `INSERT` statements straight at the connection. A measured 56,138
rows arrive in one full ingest (PRD §3.1), and per-entity tracking is the wrong shape for that. The
same schema, a different door — everything else goes through the context.

### Idempotency is a property of the index, not of the writer

The append is `ON CONFLICT (source_file, byte_offset, content_hash) DO NOTHING`. Re-running
ingestion over the same logs therefore adds nothing (FR-5) without the caller remembering where it
stopped. Naming the conflict target rather than using `INSERT OR IGNORE` keeps a genuine constraint
failure loud.

### The identity index alone cannot catch a rewritten file, so `DetectRewrites` is the second check

`ON CONFLICT` only fires on an exact `(source_file, byte_offset, content_hash)` match. A file that
was rewritten rather than grown — its bytes at an offset already stored now hash differently —
produces a *different* content hash at that offset, which is not a conflict: `Append` would insert
it as a second, unrelated row rather than refuse it, silently merging two different byte streams
under one source file. `RawEventBatch.DetectRewrites` is the read-only check that catches this
before `Append` runs: for each candidate event, it looks up whatever content hash RAW already
holds at that event's `(source_file, byte_offset)` and reports a `RawRewriteMismatch` when the two
disagree. A matching hash at an already-stored offset is not a mismatch — that is the ordinary
re-ingest case `Append`'s own conflict target already treats as a no-op. `AecoPostMortem.Ingestion.
SessionIngestor.Ingest` is the caller: it runs `DetectRewrites` before `Append` and skips the append
entirely — reporting the mismatch on its own result instead — the moment any mismatch turns up
(issue #5, FR-5's edge case: byte offsets are safe identity only because growth is append-only).

### Only RAW carries a migration

NORMALIZED and FINDINGS are re-derived from RAW, never migrated — a migration against them is a
defect (Repo Rule 4, PRD §3.8), because it would preserve a derived row the product is required to
be able to reproduce from scratch.

### The session, sequence, type and timestamp columns are not a second copy of the truth

The payload stays authoritative and byte-exact. Those four are lifted out of the envelope because
the measured read path indexes them, and an index cannot be built over a value that exists only
inside a JSON string.

### The payload column is TEXT, so the decode is strict

`RawPayload.FromUtf8` throws on bytes it cannot decode rather than substituting U+FFFD. TEXT is
stored as UTF-8, so a lossy decode would make the round trip silently non-verbatim; failing instead
routes the line to FR-6's per-line tolerance, where it is counted and retried.

### `system_prompt_text` is migrated deliberately, not by oversight

FR-12 dedupes system-prompt text by content hash — a measured 337 system messages at a measured
median 54,335 characters (data map Part 6) are mostly near-duplicates, so this is a design decision,
not a later optimisation. `SystemPromptText` does not implement `IDerivedEntity`: it is written
directly at ingest time from source bytes, the same act that writes RAW, not re-derived from
anything else already in the store the way NORMALIZED and FINDINGS are — Repo Rule 4's "never
migrated" governs those two layers, not this table. Its key is `content_hash`, not a session, which
is also why it sits outside the derived layer's "every key contains the session" invariant
(`DerivedModelTests.Every_derived_key_contains_the_session`) rather than being shoehorned into it.
`SchemaTests.The_migrations_create_only_RAW_and_the_stores_own_metadata` names it literally
alongside `raw_event` and `store_metadata`.

Extraction and resolution — pulling `data.content` out of a `system.message` RAW event, hashing it,
and deduplicating a batch of events down to their distinct texts — live in
`AecoPostMortem.Ingestion.SystemPromptExtractor`, not here: this project stores rows, it does not
parse envelopes. A session resolves its own full prompt text by re-running the same extraction over
its own RAW event and joining the resulting hash against this table — deterministic, so no separate
session-to-hash link table is needed.

### `store_metadata` is migrated deliberately, not by oversight

`StoreMetadata` does not implement `IDerivedEntity` and lives outside `Execution/` and its
namespace, so `ExcludeDerivedTypesFromMigrations` never touches it — it is the one non-RAW table
the migrations create (`SchemaTests.The_migrations_create_only_RAW_and_the_stores_own_metadata`
names both tables literally). It records facts about the store itself, including
`DerivedSchemaVersionKey`, the derived schema's version: a value dropped alongside the tables it
describes could not be compared against them, so it cannot be re-derived the way NORMALIZED and
FINDINGS are. `MapStoreMetadata` runs first in `OnModelCreating`, immediately after the `rawEvent`
block and before `MapSession`, so its placement can't drift as later mappings are added.

### `Rebuild` is unconditional; `EnsureCurrent` is conditional

Both drop and recreate the derived tables, but they answer different questions.
`EnsureCurrent` asks "has the model's version moved since this store was last opened" and only acts
when it has — that is what makes opening a current store cheap. `Rebuild` is the operator asking for
a rebuild regardless of version (PRD §3.2, §3.8, S-46): the CLI's `rebuild` command calls it
directly, never `EnsureCurrent`, because "nothing changed" is not a reason to refuse the operator's
own request. Neither method populates the recreated tables from RAW — that derivation logic
(session discovery, execution record reconstruction) lands with the E1 ingestion stories; today both
leave the tables empty, which is the honest answer for "re-derived from a RAW that has no reader
yet." `DerivedSchemaTests` covers `Rebuild`'s determinism: two rebuilds against the same model
produce the same tables, in the same order, with the same version hash, and RAW rows are untouched
by either method — see `Rebuild_leaves_RAW_unchanged` and
`Rebuilding_twice_produces_identical_schema_content_and_order`.

### The determinism contract is enforced by a source scan, not by a reference check

PRD §3.8 forbids a check that reads the clock, samples randomly, or calls a model. The surface that
would let one do any of those three is entirely inside the base class library —
`DateTime.Now`, `Random` and `HttpClient` need no `PackageReference` — so `AecoPostMortem.Rules`'
"references nothing" guarantee does not by itself rule any of them out.
`test/AecoPostMortem.Containment.Tests/DeterminismInvariantTests.cs` scans every `.cs` file under
`AecoPostMortem.Rules` and `AecoPostMortem.Findings` for a fixed list of forbidden substrings
(`DateTime.Now`, `new Random(`, `HttpClient`, and similar) and fails the moment one appears; a
second test proves the scanner itself is not vacuous. Both projects are still close to empty, so
this is the enforcement mechanism built ahead of what it enforces — extend the pattern list there,
not a new one elsewhere, when a check lands that could plausibly read time or chance.

### `Pooling=False` on the connection

A pooled handle outlives the context that opened it, and `purge` has to be able to delete the file
immediately afterwards. One local file, one process — the pool buys nothing to weigh against that.

### The derived layer's mechanics are documented in a sidecar

Migration exclusion, hand-generated DDL versioned by a SHA-256 hash, the `ck_<table>_owner`
ownership pairing, `ToolCall`'s five measured indexes, `Agent`'s `ck_agent_cost` gate and the
event-scoped shapes (`Skill`, `Hook`, `Permission`, `WriteUnit`) are documented in
`src/AecoPostMortem.Data/docs/DERIVED_LAYER.md`, alongside `docs/claude/NORMALIZED_MODEL.md`'s
per-column coverage. Read it before mapping or querying anything under `Execution/`.

## Playbook — changing the RAW schema

1. Edit `RawEvent`, `RawEventSchema` and the mapping in `PostMortemContext`.
2. `dotnet ef migrations add <Name> --project src/AecoPostMortem.Data --output-dir Migrations`
3. Add any new index to `SchemaTests.RequiredIndexes`, which names them literally so a rename
   cannot pass unnoticed.
4. If a column joins the append, add it to `RawEventSchema.WrittenColumns` **and** to
   `RawEventBatch.ColumnTypes` in the same order, then bind it in `RawEventBatch.Execute`.
5. Record the change in `docs/claude/DOMAIN_MODEL.md`.

## Playbook — adding a derived entity

1. Implement `IDerivedEntity` (and `IOwned`, if a subagent can own it) under `Execution/`.
2. Map it in `OnModelCreating` with snake_case columns; call `MapOwnership` if it is `IOwned`.
3. Add it to `DerivedModelTests.The_eight_shapes_are_published`.
4. No migration — the table is created from the model, not from a migration
   (`docs/DERIVED_LAYER.md`).

A derived layer never reaches step 2: it is dropped and re-derived by `rebuild`.

## Playbook — keeping a derived entity rebuild-safe

1. Never give it anything `DerivedSchema.CreateStatements` cannot emit deterministically: no default
   value, computed column, collation or foreign key (`DerivedSchemaTests.The_generator_covers_every_
   mapping_feature_the_derived_model_uses` catches these), and no server-assigned or time-of-write
   value — a rebuilt row must be identical in content to the row it replaced, ids aside (issue #24's
   edge case).
2. Derive it from RAW alone. Nothing under `Execution/` may read the wall clock, sample randomly, or
   reach a model to decide a value — `DeterminismInvariantTests` in `Containment.Tests` scans for
   exactly that, and a derived entity's mapping or any future population logic for it is squarely
   "the analysis code path" that scan covers.
3. Sort before you emit. `DerivedSchema.CreateStatements` orders tables, columns and indexes by name
   (`StringComparer.Ordinal`) precisely so two rebuilds against the same model produce the same DDL
   in the same order — carry that discipline into any derivation logic that populates rows, since a
   tie broken by insertion order would reorder the operator's priorities between runs.
4. Test it under `Rebuild`, not only under `EnsureCurrent`: add a case to `DerivedSchemaTests` (or
   the entity's own test file) proving `DerivedSchema.Rebuild` drops the entity's rows and that two
   consecutive rebuilds against unchanged RAW agree, content and order both.
