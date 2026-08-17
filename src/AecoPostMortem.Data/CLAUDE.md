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
| `RawEventBatch.cs` | the batched raw-SQL append |
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
| `Execution/DerivedSchema.cs` | hand-generated DDL for the eight derived tables, a SHA-256 version over that DDL, and `EnsureCurrent` — create/drop/version, called by `LocalStore.Open` |
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

### `store_metadata` is migrated deliberately, not by oversight

`StoreMetadata` does not implement `IDerivedEntity` and lives outside `Execution/` and its
namespace, so `ExcludeDerivedTypesFromMigrations` never touches it — it is the one non-RAW table
the migrations create (`SchemaTests.The_migrations_create_only_RAW_and_the_stores_own_metadata`
names both tables literally). It records facts about the store itself, including
`DerivedSchemaVersionKey`, the derived schema's version: a value dropped alongside the tables it
describes could not be compared against them, so it cannot be re-derived the way NORMALIZED and
FINDINGS are. `MapStoreMetadata` runs first in `OnModelCreating`, immediately after the `rawEvent`
block and before `MapSession`, so its placement can't drift as later mappings are added.

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
4. No migration — the table is created from the model (see the two decisions above).

A derived layer never reaches step 2: it is dropped and re-derived by `rebuild`.
