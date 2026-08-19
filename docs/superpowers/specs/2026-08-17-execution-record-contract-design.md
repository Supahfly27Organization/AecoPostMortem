# Execution-record entity contract — design

**Story:** S-49 ([issue #12](https://github.com/Supahfly27Organization/AecoPostMortem/issues/12)) ·
**Epic:** E1 — Durable ingestion ([issue #1](https://github.com/Supahfly27Organization/AecoPostMortem/issues/1))
**Implements:** PRD §3.2, the NORMALIZED layer
**Blocks:** S-06, S-08, S-09, S-11, S-13, S-14, S-15, S-16, S-17, S-18, S-19, S-21, S-27, S-43, S-46
**Date:** 2026-08-17

## 1. What this story is for

Fifteen stories need a shape to build against. Without one they all queue behind S-06, which is the
largest story in E1 — so this publishes the shapes before anything populates them, and that is the
whole of it. **This story writes no row.** S-06 reconstructs the execution record; this story decides
what a reconstructed record looks like and makes the store able to hold one.

Three things in the acceptance criteria are stated as properties of the *shape* rather than of the
code that fills it, and each becomes structure here rather than a convention:

- every entity is scoped by its session,
- main-thread ownership is representable, not a null to be guessed at,
- an agent that completed without reporting its cost is distinguishable from one that did not
  complete.

## 2. Where the derived layer lives, and why it has no migration

PRD §3.8 splits the store: RAW moves forward under EF Core migrations because its rows are
irreplaceable once Copilot's window rotates; NORMALIZED and FINDINGS are dropped and re-derived,
because a migration there would preserve a derived row the product is required to be able to
reproduce, and hide a rebuild bug behind it.

The mechanical problem is that the derived tables still have to exist before anything can write to
them, and a migration is the ordinary way a table comes into being.

**Decision: one `PostMortemContext`, with the derived entity types excluded from migrations by
convention.**

```
src/AecoPostMortem.Data/
  PostMortemContext.cs        ← one model, one connection
  RawEvent.cs                 ← migrated
  StoreMetadata.cs            ← migrated
  Execution/
    IDerivedEntity.cs         ← the marker
    Session.cs  Turn.cs  ToolCall.cs  Agent.cs
    Skill.cs    Hook.cs  Permission.cs  WriteUnit.cs
    Ownership.cs              ← the invariant-2 value
    DerivedSchema.cs          ← create, drop, and the version
```

`OnModelCreating` enumerates every entity type whose CLR type implements `IDerivedEntity` and calls
`ExcludeFromMigrations()` on it in a loop. It is not a call to remember per entity — a type added in
a year is caught by the same loop, and a type that omits the marker fails the schema test rather
than silently entering the migration set.

**The alternatives, and why not.** A second `DbContext` for the derived layer is a marginally
stronger guarantee — with no migrations assembly, a derived migration has nowhere to land — bought
at the price of every cross-layer query becoming two queries. That is a permanent tax on fifteen
stories to prevent a mistake a convention plus two tests already catch. Putting the derived tables
in the migration set is the option to argue against hardest: EF Core's model snapshot means the
*next* `migrations add` after any derived-shape change generates the `ALTER TABLE` §3.8 calls a
defect, by default, while everyone follows the process correctly.

SQLite has no `ALTER TABLE DROP COLUMN` worth depending on, so drop-and-recreate is the engine's
natural move as well as the PRD's.

## 3. The three invariants, as structure

### 3.1 Session-scoped natural keys

Every derived entity's primary key is the composite `(SessionId, LocalId)`. No surrogate key sits
beside it.

`LocalId` is the identifier Copilot already wrote — `turnId`, `toolCallId`, the subagent handle —
and where Copilot writes none, the envelope `id`, which the data map measured present on 100% of
events.

Two things follow. The acceptance criterion "the session is part of that key" becomes unfalsifiable
rather than documented. And S-46's determinism comparison loses its carve-out: that story currently
has to allow "a rebuild identical except for row ids", and with natural keys there are no row ids to
differ, so the comparison is plain equality.

### 3.2 Ownership is a value, never a null

```
owner_kind  TEXT NOT NULL   CHECK (owner_kind IN ('main', 'agent'))
agent_id    TEXT NULL       CHECK ((owner_kind = 'main') = (agent_id IS NULL))
```

surfaced in C# as a small value with `Ownership.MainThread` and `Ownership.By(agentId)`.

There is no nullable field a caller can misread as "attribution unknown", because `owner_kind` is
NOT NULL and the check constraint binds the pair. The data map measured this as exact rather than
probable. The data map measured 115 distinct `agentId` values on tool events in the largest session,
and a measured 115 of 115 resolve to a `subagent.started.toolCallId` in the same file. Absence of
`agentId` therefore *means* main thread, exactly.

`Session` carries no ownership: it is the scope. `Agent` carries none: it is the owner.

### 3.3 The agent completion tri-state

`subagent.completed` carries tokens and duration on a measured 215 of 462 completions. So `Agent`
carries a discriminator:

| `outcome` | Meaning | Metric columns |
|---|---|---|
| `running` | no completion or failure event seen | null |
| `completed` | completed, with its cost reported | present |
| `completed_cost_unknown` | completed, cost not reported | null |
| `failed` | `subagent.failed` seen | null; `error` present |

with a check constraint that the metric columns are non-null only under `completed`. S-06's edge
case named this problem and had nowhere structural to put it; here "completed, cost unknown" cannot
be read as zero tokens, because the columns are absent rather than zero.

## 4. The eight shapes

Coverage figures are the data map's, and drive what is nullable. A field measured below full
coverage is present-or-absent — never zero-filled, because a zero is a number a surface would print.

| Entity | Key | Fields |
|---|---|---|
| `Session` | `SessionId` | `StartedAt`; `EndedAt?`; `CopilotVersion`, `EventSchemaVersion`; the `session.start.data.context` block — `Cwd`, `GitRoot`, `Branch`, `HeadCommit`, `Repository`, `HostType`, `BaseCommit`, measured 35/35; `SourceFile`; `InputTokens?`, `OutputTokens?`, `CacheReadTokens?`, `CacheWriteTokens?`, `ReasoningTokens?`, `ModelCount?` from `session.shutdown.data.modelMetrics`, measured 31/35 |
| `Turn` | `(SessionId, TurnId)` | `StartedAt`, `EndedAt?`; `Outcome` — completed, aborted with `AbortReason`, or unfinished; `OutputTokens?`; `Ownership` |
| `ToolCall` | `(SessionId, ToolCallId)` | `ToolName`, measured 100%; `StartedAt`, `CompletedAt?`; `Success?`; `Path?`, measured 5,201 of 5,201 `view` calls; `ResultSizeBytes?`; `McpServerName?`, `McpToolName?`, measured 251; `TurnId?`; `Ownership` |
| `Agent` | `(SessionId, AgentId)` | `SpawningToolCallId`, measured 470 of 470 resolving; `ParentAgentId?`, measured 178 of 470 nested; `Name`, `DisplayName`, `Description`; `StartedAt`; §3.3's outcome and its metrics |
| `Skill` | `(SessionId, EventId)` | `Name`, `Path`, `Description`, `PluginName`, `PluginVersion`, from a measured 794 `skill.invoked` events; `InvokedAt`; `Ownership` |
| `Hook` | `(SessionId, EventId)` | `Name`; `StartedAt`, `EndedAt?`; `Success?`, measured 35 failures across 3,027 pairs; `Ownership` |
| `Permission` | `(SessionId, EventId)` | `RequestedAt`, `CompletedAt?`; `ResultKind?` — an enum on Copilot rather than a string match, measured 1,033 requested against 1,031 completed; `ToolCallId?`; `Ownership` |
| `WriteUnit` | `(SessionId, EventId)` | `ToolCallId`, `Path`, `AddedContent`; `Ownership` |

### What is deliberately absent

**Messages.** The eight carry no message entity, and that is consistent rather than an oversight:
the latency research measured the Flight Recorder's tape (Q4, Q5) against `raw_event` directly.
NORMALIZED holds the execution skeleton; message text is read from RAW.

**`FileChange`, `RuleStatement`, `RuleSetVersion`.** PRD §3.2 lists all three in NORMALIZED, and the
acceptance criteria name none of them. `FileChange` arrives with S-07, whose dependencies are S-01
alone, so it never waits on this contract — and per FR-13 its source may be deferred entirely if
nothing in Phase A reads it. `RuleStatement` and `RuleSetVersion` belong to S-19 and S-20, both of
which list *this* contract as a dependency; fixing their shape here would design two later stories
blind, and FR-26 has real open questions in it about what counts as a rule statement at all.

**`WriteUnit` is published and never populated.** FR-36 is Phase E, gated out of v1 (PRD §3.4.3).
The acceptance criteria name it, and an unpopulated shape costs nothing — which is the point of a
contract story.

**A per-model token breakdown.** `session.shutdown.data.modelMetrics` is keyed by model, so a session
that changed model mid-run has several entries, and the data map measured that happening: 36
`session.model_change` events across a measured 25 sessions. Two readings were possible and one is
chosen — **`Session` carries the totals summed across models, plus `ModelCount`.** The per-model
split would need a ninth shape, and
the acceptance criteria name eight. Recorded as a known gap rather than hidden: if S-11 needs the
breakdown to state a figure honestly, it reopens this contract, and that is a cheaper mistake to fix
than a ninth entity nobody asked for.

**Ownership on `Turn`.** Turns carry it like everything else, because the envelope carries `agentId`
on subagent-scoped events and nothing measured rules out a subagent-owned turn. If S-06 finds every
turn is main-thread, the column is a constant that costs one discriminator per row — the cheaper
error than discovering the opposite after fifteen stories have built on a turn that cannot say who
ran it.

## 5. Indexes

The covering indexes ship with the contract rather than with the stories that query through them.
The latency research measured their absence at 776.06 ms against 56.15 ms on Postgres for the
per-tool aggregate — a measured 13.8× — falling to a measured 64.34 ms once present. Leaving them to
S-13 and S-14 means rediscovering that regression.

| Index | Columns | Serves |
|---|---|---|
| `ix_tc_session` | `tool_call(session_id)` | a session's calls |
| `ix_tc_name` | `tool_call(tool_name)` | the tool vocabulary (FR-29) |
| `ix_tc_session_path` | `tool_call(session_id, path)` | repeated file reads (FR-15) |
| `ix_tc_name_success` | `tool_call(tool_name, success)` | per-tool failure rates (FR-16) |
| `ix_tc_session_name` | `tool_call(session_id, tool_name)` | adherence scoped to a session (FR-31) |
| `ix_turn_session` | `turn(session_id)` | the tape |
| `ix_agent_session` | `agent(session_id)` | subagent lanes (FR-22) |
| `ix_agent_parent` | `agent(session_id, parent_agent_id)` | nesting |

## 6. The derived schema version

PRD §3.8: the store carries a schema version, and a version change triggers re-derivation rather
than migration.

The version is **computed, not maintained**: SHA-256 over the generated `CREATE TABLE` and
`CREATE INDEX` statements for the derived tables, lower-case hex. A hand-maintained integer is a
thing to forget when a column changes; a hash over the DDL cannot be out of step with the DDL.

It is stored in `store_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL)`.

`store_metadata` is **migrated**, not derived — it records the store's own state, and a value that
is dropped with the tables it describes cannot be compared against them.

**This changes a test shipped with S-01.** `SchemaTests.RAW_is_the_only_table_the_migrations_create`
asserts the migration set is exactly `raw_event`; it becomes `raw_event` and `store_metadata`, with
the reason stated in the test. Widening it silently would be the drift the test exists to catch.

## 7. What this story ships, and what it does not

**Ships:** the eight entity types and their mapping; `Ownership`; the derived-schema create, drop and
version primitives; the `store_metadata` migration; the indexes; the tests below; and the
`docs/claude/DOMAIN_MODEL.md` entry for each entity.

**Does not ship:** any population of any table (S-06); the `rebuild` command that calls drop and
create (S-46); the finding record shape (S-44); `FileChange`, `RuleStatement`, `RuleSetVersion`.

## 8. Tests

Each is tied to a criterion or an invariant, not to a method.

| Test | What it holds |
|---|---|
| The eight shapes are published | acceptance criterion 1, by name |
| Every derived key contains the session | acceptance criterion 2, read from the model's key metadata rather than restated |
| Every derived entity type is excluded from migrations | the convention in §2, over the model's entity types |
| The migrations create only `raw_event` and `store_metadata` | Repo Rule 4, widened once and deliberately |
| `owner_kind` is NOT NULL, and its check rejects a mismatched pair | acceptance criterion 3 — a main-thread row carrying an agent id fails at the database |
| Agent metrics are rejected unless the outcome is `completed` | §3.3, at the database |
| Drop then create leaves exactly the migrated tables, and create is idempotent | the derived layer is disposable |
| The measured `tool_call` indexes exist | §5, named literally so a rename cannot pass |
| The schema version is stable across runs and moves when the model does | §6, and §3.8's determinism |

## 9. Implementation order

1. `IDerivedEntity`, `Ownership`, and the `OnModelCreating` loop — the convention before anything
   relies on it.
2. The eight entity types and their mapping, with check constraints.
3. `store_metadata` and its migration; update the S-01 schema test in the same commit that widens
   what it allows.
4. `DerivedSchema` — create, drop, version.
5. The indexes.
6. `docs/claude/DOMAIN_MODEL.md`, and the `Data` router's playbook entry for adding a derived
   entity.

## 10. Out of scope

Populating anything. The `rebuild` command. The findings contract. Any entity not among the eight.
Any query helper — the fifteen downstream stories write their own reads against the shapes, which is
what a contract is for.
