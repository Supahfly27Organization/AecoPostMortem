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
| `Execution/Agent.cs` | one subagent, keyed by `(SessionId, AgentId)`; carries `AgentOutcome`'s four-state completion tri-state instead of `IOwned` — it is the owner |
| `Execution/EventScopedEntities.cs` | `Skill`, `Hook`, `Permission`, `WriteUnit` — the four shapes Copilot writes no natural id for, each keyed `(SessionId, EventId)` off the envelope's own `id`; completes the contract's eight shapes |
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

### The derived layer is excluded from migrations by a loop, not by memory

Any entity implementing `AecoPostMortem.Data.Execution.IDerivedEntity` is swept by
`PostMortemContext.ExcludeDerivedTypesFromMigrations` at the end of `OnModelCreating` and marked
`ToTable(table => table.ExcludeFromMigrations())`. A new derived entity that forgets the marker
fails `DerivedModelTests.Every_derived_entity_type_is_excluded_from_migrations` instead of quietly
entering the next `migrations add` — Repo Rule 4 as a loop rather than a call each entity's mapping
code has to remember.

`DbContext.Model` (the property tests normally read) returns EF Core 10's read-optimized runtime
model, which strips migrations-only annotations — calling `IsTableExcludedFromMigrations()` on it
throws rather than answering. Tests that assert on that annotation must read
`context.GetService<IDesignTimeModel>().Model` instead (`Microsoft.EntityFrameworkCore.Metadata`
namespace).

### Ownership is a database constraint, not a convention

`IOwned` (`OwnerKind` + `AgentId`) is carried by every derived entity a subagent can own; `Turn` is
the first. `PostMortemContext.MapOwnership<TEntity>` is the shared helper every such entity's
mapping calls — it adds a `ck_<table>_owner` check constraint pairing `owner_kind = 'main'` with a
null `agent_id` exactly, so a row can't claim the main thread while carrying an agent id (or claim
an agent without one) no matter which code path writes it. The data map measured 115 of 115 agent
ids resolving to a known subagent handle, so absence of an agent id means main thread, not "unknown"
— there is deliberately no nullable third state.

### `ToolCall`'s indexes ship with the contract, not with the story that queries through them

`ix_tc_session`, `ix_tc_name`, `ix_tc_session_path`, `ix_tc_name_success` and `ix_tc_session_name`
exist because their absence was measured at 776.06 ms against 56.15 ms on Postgres for the per-tool
aggregate — a measured 13.8×, falling to 64.34 ms once present
(`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md` Part 3). A
measured 16,085 `tool.execution_start` events against 16,076 `execution_complete` means an
unfinished call is a real state, so `CompletedAt`, `Success`, `ResultSizeBytes` and the MCP fields
are nullable rather than zero-filled. `DerivedModelTests.The_measured_tool_call_index_exists` names
each index literally so a rename can't pass unnoticed.

### `Pooling=False` on the connection

A pooled handle outlives the context that opened it, and `purge` has to be able to delete the file
immediately afterwards. One local file, one process — the pool buys nothing to weigh against that.

### `Agent` is the owner, not `IOwned`, and its cost columns are gated by a check constraint

`Agent` implements only `IDerivedEntity`. It never calls `MapOwnership` — that helper exists for
entities a subagent can own, and `Agent` is the thing being owned's owner-of-record instead, with
its own key column already `agent_id`. `MapAgent` therefore names its own table via `ToTable`
directly, and it must stay the last statement in the method (the `HasCheckConstraint` call has to
see every prior `Property` mapping).

`subagent.completed` carries tokens and duration on only a measured 215 of 462 completions, so
`AgentOutcome` has four states, not two: `CompletedCostUnknown` distinguishes "completed, cost
unknown" from `Running`/`Failed` (did not complete), and the metric columns (`TotalTokens`,
`TotalToolCalls`, `DurationMs`, `Model`) stay nullable rather than zero-filled so absence is never
readable as zero. `ck_agent_cost` enforces that pairing in the database — metrics may accompany
only `Outcome = 'Completed'` — and it compares against the capitalized C# member name because
`Outcome` is persisted via `HasConversion<string>()`, not a lowercased form.

### `Skill`, `Hook`, `Permission` and `WriteUnit` key off the envelope's `id`, not a Copilot-issued one

Copilot writes no natural id for a skill invocation, a hook pair, a permission request or a write.
`EventId` is the event envelope's own `id` instead — measured present on 100% of events — so each
is keyed `(SessionId, EventId)` rather than the tool-call/turn/agent pattern of keying off an id
Copilot issued. All four are `IOwned` and mapped through `MapEventScopedEntities`, which — like
every other derived mapping — ends each entity's block with `MapOwnership`, since that call is what
names the table. `WriteUnit` is published but never populated in v1: FR-36 is Phase E, gated out by
PRD §3.4.3; the shape exists so later stories have something to compile against.

## Playbook — changing the RAW schema

1. Edit `RawEvent`, `RawEventSchema` and the mapping in `PostMortemContext`.
2. `dotnet ef migrations add <Name> --project src/AecoPostMortem.Data --output-dir Migrations`
3. Add any new index to `SchemaTests.RequiredIndexes`, which names them literally so a rename
   cannot pass unnoticed.
4. If a column joins the append, add it to `RawEventSchema.WrittenColumns` **and** to
   `RawEventBatch.ColumnTypes` in the same order, then bind it in `RawEventBatch.Execute`.
5. Record the change in `docs/claude/DOMAIN_MODEL.md`.

A derived layer never reaches step 2: it is dropped and re-derived by `rebuild`.
