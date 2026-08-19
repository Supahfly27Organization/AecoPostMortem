# The Derived Layer

> **Scope:** how the eight NORMALIZED tables under `Execution/` are excluded from migrations,
> generated as DDL, versioned, and how ownership and the agent cost gate are enforced.
> **Read when:** mapping a new derived entity, changing an existing one, or debugging why a
> check constraint fired.

## The derived layer is created from the model, not from a migration

Any entity implementing `IDerivedEntity` is swept by `ExcludeDerivedTypesFromMigrations` at the end
of `OnModelCreating` and marked `ToTable(table => table.ExcludeFromMigrations())`; a new derived
entity that forgets the marker fails
`DerivedModelTests.Every_derived_entity_type_is_excluded_from_migrations` instead of quietly
entering the next `migrations add`. The same flag hides these tables from EF's own script
generation, including `GenerateCreateScript()`, which is why `DerivedSchema.CreateStatements` builds
each `CREATE TABLE` / `CREATE INDEX` by hand from the model's own metadata instead.

Every such read — here and in `DerivedSchemaTests` — goes through
`context.GetService<IDesignTimeModel>().Model`, never `context.Model`: the latter is EF Core's
read-optimized runtime model, which strips migrations-era annotations, check constraints included,
so building DDL from it would silently emit tables with no `CHECK` clause and nothing to say so.

## The derived schema's version is a hash of its own DDL

`DerivedSchema.Version` is a SHA-256 over `CreateStatements`' exact output — computed rather than
hand-maintained, so it cannot go stale the way an incremented integer could. `LocalStore.Open`
calls `EnsureCurrent` between `Migrate()` and the owner-only file lock: a version that differs
from the one recorded in `store_metadata` means the tables predate the model, so they are dropped
and recreated — the rows go with them, which is correct, because they are re-derivable from RAW
(PRD §3.8).

## Ownership is a database constraint, not a convention

`IOwned` (`OwnerKind` + `AgentId`) is carried by every derived entity a subagent can own; `Turn` is
the first. `PostMortemContext.MapOwnership<TEntity>` is the shared helper every such entity's
mapping calls — it adds a `ck_<table>_owner` check constraint pairing `owner_kind = 'main'` with a
null `agent_id` exactly, so a row can't claim the main thread while carrying an agent id (or claim
an agent without one) no matter which code path writes it. The data map measured 115 of 115 agent
ids resolving to a known subagent handle, so absence of an agent id means main thread, not "unknown"
— there is deliberately no nullable third state.

## `ToolCall`'s indexes ship with the contract, not with the story that queries through them

`ix_tc_session`, `ix_tc_name`, `ix_tc_session_path`, `ix_tc_name_success` and `ix_tc_session_name`
exist because their absence was measured at 776.06 ms against 56.15 ms on Postgres for the per-tool
aggregate — a measured 13.8×, falling to 64.34 ms once present
(`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md` Part 3). A
measured 16,085 `tool.execution_start` events against 16,076 `execution_complete` means an
unfinished call is a real state, so `CompletedAt`, `Success`, `ResultSizeBytes` and the MCP fields
are nullable rather than zero-filled. `DerivedModelTests.The_measured_tool_call_index_exists` names
each index literally so a rename can't pass unnoticed.

## `Agent` is the owner, not `IOwned`, and its cost columns are gated by a check constraint

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

## `Skill`, `Hook`, `Permission` and `WriteUnit` key off the envelope's `id`

Copilot writes no natural id for a skill invocation, a hook pair, a permission request or a write.
`EventId` is the event envelope's own `id` instead — measured present on 100% of events — so each
is keyed `(SessionId, EventId)` rather than the tool-call/turn/agent pattern of keying off an id
Copilot issued. All four are `IOwned` and mapped through `MapEventScopedEntities`, which — like
every other derived mapping — ends each entity's block with `MapOwnership`, since that call is what
names the table. `WriteUnit` is published but never populated in v1: FR-36 is Phase E, gated out by
PRD §3.4.3; the shape exists so later stories have something to compile against.

Per-column detail — types, DB column names and the measured coverage behind every nullable one —
lives in `docs/claude/NORMALIZED_MODEL.md`, not here; this file is the mechanics, that one is the
schema.
