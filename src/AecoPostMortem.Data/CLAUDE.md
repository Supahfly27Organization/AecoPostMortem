# AecoPostMortem.Data

The `DbContext`, the entity model and the EF Core migrations; the only project that owns the schema.

## Structure

| File | What it holds |
|---|---|
| `RawEvent.cs` | the RAW row: the verbatim line plus its provenance |
| `RawEventSchema.cs` | the physical table, column and index names, stated once |
| `RawPayload.cs` | strict UTF-8 decode, and FR-2's content hash |
| `PostMortemContext.cs` | the model, its column mapping and its indexes |
| `RawEventBatch.cs` | the batched raw-SQL append |
| `LocalStore.cs` | the store as a file: path, creation, size, purge |
| `StoreLocation.cs` | FR-11's documented per-user path |
| `OwnerOnlyAccess.cs` | owner-only permissions, per platform |
| `Execution/IDerivedEntity.cs` | empty marker interface for the NORMALIZED/FINDINGS layers |
| `Execution/Session.cs` | the first derived entity: one Copilot session, keyed by `SessionId` |
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

### `Pooling=False` on the connection

A pooled handle outlives the context that opened it, and `purge` has to be able to delete the file
immediately afterwards. One local file, one process — the pool buys nothing to weigh against that.

## Playbook — changing the RAW schema

1. Edit `RawEvent`, `RawEventSchema` and the mapping in `PostMortemContext`.
2. `dotnet ef migrations add <Name> --project src/AecoPostMortem.Data --output-dir Migrations`
3. Add any new index to `SchemaTests.RequiredIndexes`, which names them literally so a rename
   cannot pass unnoticed.
4. If a column joins the append, add it to `RawEventSchema.WrittenColumns` **and** to
   `RawEventBatch.ColumnTypes` in the same order, then bind it in `RawEventBatch.Execute`.
5. Record the change in `docs/claude/DOMAIN_MODEL.md`.

A derived layer never reaches step 2: it is dropped and re-derived by `rebuild`.
