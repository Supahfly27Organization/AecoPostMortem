# Normalized Model

> **Scope:** the eight NORMALIZED entities — table name, key, columns and the invariants that bind
> them, plus the measured coverage that made a column nullable.
> **Read when:** writing a query against a derived table, or checking what a NORMALIZED column
> means before trusting it.

These eight are re-derived from `raw_event`, never migrated (Repo Rule 4, PRD §3.8): their tables
are created from the model, not from a migration, and `DerivedSchema` versions them by a SHA-256
hash of their own generated DDL. A version mismatch drops and recreates every derived table; the
rows go with them, because they are re-derivable from RAW.

## Session — table `session`

PK: `SessionId`. One Copilot session; the only one of the eight that carries no ownership, since it
is the scope every other entity is keyed within.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId` | `string` | `session_id TEXT NOT NULL` | primary key |
| `StartedAt` | `string` | `started_at TEXT NOT NULL` | |
| `EndedAt` | `string?` | `ended_at TEXT` | null when the session never wrote `session.shutdown` — measured 31 of 35 did |
| `CopilotVersion` | `string` | `copilot_version TEXT NOT NULL` | |
| `EventSchemaVersion` | `string` | `event_schema_version TEXT NOT NULL` | |
| `SourceFile` | `string` | `source_file TEXT NOT NULL` | |
| `Cwd` | `string` | `cwd TEXT NOT NULL` | `session.start.data.context`, measured present on 35 of 35 sessions |
| `GitRoot`, `Branch`, `HeadCommit`, `Repository`, `HostType`, `BaseCommit` | `string?` (each) | `git_root`, `branch`, `head_commit`, `repository`, `host_type`, `base_commit` (all `TEXT`) | `session.start.data.context` fields Copilot does not always populate |
| `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheWriteTokens`, `ReasoningTokens` | `long?` (each) | `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_write_tokens`, `reasoning_tokens` (all `INTEGER`) | from `session.shutdown.data.modelMetrics`, measured present on 31 of 35 sessions; summed across models, never zero-filled |
| `ModelCount` | `int?` | `model_count INTEGER` | how many models the token figures above were summed across |

## Turn — table `turn`

PK: (`SessionId`, `TurnId`). One assistant turn, bounded by `assistant.turn_start` /
`assistant.turn_end`.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId`, `TurnId` | `string` (each) | `session_id TEXT NOT NULL`, `turn_id TEXT NOT NULL` | key |
| `StartedAt` | `string` | `started_at TEXT NOT NULL` | |
| `EndedAt` | `string?` | `ended_at TEXT` | a measured 2,384 turn starts against 2,375 ends means unfinished is a real outcome state |
| `Outcome` | `TurnOutcome` | `outcome TEXT NOT NULL` | `Unfinished`/`Completed`/`Aborted`, stored via `HasConversion<string>()` |
| `AbortReason` | `string?` | `abort_reason TEXT` | set only when `Outcome` is `Aborted` |
| `OutputTokens` | `long?` | `output_tokens INTEGER` | |
| `OwnerKind`, `AgentId` | `OwnerKind`, `string?` | `owner_kind TEXT NOT NULL`, `agent_id TEXT` | ownership pair, see Invariants |

Index: `ix_turn_session` (`SessionId`).

## ToolCall — table `tool_call`

PK: (`SessionId`, `ToolCallId`). One tool invocation, bounded by `tool.execution_start` /
`tool.execution_complete`.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId`, `ToolCallId` | `string` (each) | `session_id TEXT NOT NULL`, `tool_call_id TEXT NOT NULL` | key |
| `ToolName` | `string` | `tool_name TEXT NOT NULL` | |
| `StartedAt` | `string` | `started_at TEXT NOT NULL` | |
| `CompletedAt` | `string?` | `completed_at TEXT` | a measured 16,085 `tool.execution_start` events against 16,076 `execution_complete` means an unfinished call is a real state |
| `Success` | `bool?` | `success INTEGER` | from `tool.execution_complete.data.success`, measured present on 16,076 of 16,076 completions — null means "not completed", never "completed, outcome unknown" |
| `Path` | `string?` | `path TEXT` | the path a read or write touched, measured present on 5,201 of 5,201 `view` calls; null for tools that name no path |
| `ResultSizeBytes` | `long?` | `result_size_bytes INTEGER` | |
| `McpServerName`, `McpToolName` | `string?` (each) | `mcp_server_name TEXT`, `mcp_tool_name TEXT` | |
| `TurnId` | `string?` | `turn_id TEXT` | |
| `OwnerKind`, `AgentId` | `OwnerKind`, `string?` | `owner_kind TEXT NOT NULL`, `agent_id TEXT` | ownership pair, see Invariants |

Indexes: `ix_tc_session` (`SessionId`), `ix_tc_name` (`ToolName`), `ix_tc_session_path`
(`SessionId, Path`), `ix_tc_name_success` (`ToolName, Success`), `ix_tc_session_name`
(`SessionId, ToolName`) — measured 776.06 ms against 56.15 ms on Postgres for the per-tool aggregate
without them, a measured 13.8×, falling to 64.34 ms once present
(`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md` Part 3).

## Agent — table `agent`

PK: (`SessionId`, `AgentId`). One subagent. Carries no `IOwned`: it is the owner-of-record, not
something owned, and its key column is already `agent_id`.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId`, `AgentId` | `string` (each) | `session_id TEXT NOT NULL`, `agent_id TEXT NOT NULL` | key; the handle is `subagent.started.data.toolCallId`, measured identical to the `agentId` on every event the subagent produced |
| `SpawningToolCallId` | `string` | `spawning_tool_call_id TEXT NOT NULL` | the `task` call that produced it — a measured 470 of 470 spawns resolve |
| `ParentAgentId` | `string?` | `parent_agent_id TEXT` | a measured 178 of 470 are nested; null means spawned from the main thread |
| `Name`, `DisplayName` | `string` (each) | `name TEXT NOT NULL`, `display_name TEXT NOT NULL` | |
| `Description` | `string?` | `description TEXT` | |
| `StartedAt` | `string` | `started_at TEXT NOT NULL` | |
| `Outcome` | `AgentOutcome` | `outcome TEXT NOT NULL` | `Running`/`Completed`/`CompletedCostUnknown`/`Failed`, stored via `HasConversion<string>()`; see Invariants |
| `TotalTokens`, `TotalToolCalls`, `DurationMs`, `Model` | `long?`, `int?`, `long?`, `string?` | `total_tokens INTEGER`, `total_tool_calls INTEGER`, `duration_ms INTEGER`, `model TEXT` | `subagent.completed` carries these on only a measured 215 of 462 completions, so they stay nullable rather than zero-filled; gated by `ck_agent_cost` |
| `Error` | `string?` | `error TEXT` | from `subagent.failed.data.error` — a measured 6 events across 2 sessions |

Indexes: `ix_agent_session` (`SessionId`), `ix_agent_parent` (`SessionId, ParentAgentId`).

## Skill — table `skill`

PK: (`SessionId`, `EventId`). One `skill.invoked` event — a measured 794 across 31 sessions.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId`, `EventId` | `string` (each) | `session_id TEXT NOT NULL`, `event_id TEXT NOT NULL` | key; `EventId` is the envelope's own `id`, measured present on 100% of events — Copilot writes no natural id for a skill invocation |
| `Name` | `string` | `name TEXT NOT NULL` | |
| `Path`, `Description`, `PluginName`, `PluginVersion` | `string?` (each) | `path`, `description`, `plugin_name`, `plugin_version` (all `TEXT`) | |
| `InvokedAt` | `string` | `invoked_at TEXT NOT NULL` | |
| `OwnerKind`, `AgentId` | `OwnerKind`, `string?` | `owner_kind TEXT NOT NULL`, `agent_id TEXT` | ownership pair, see Invariants |

Index: `ix_skill_session` (`SessionId`).

## Hook — table `hook`

PK: (`SessionId`, `EventId`). One `hook.start` / `hook.end` pair.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId`, `EventId` | `string` (each) | `session_id TEXT NOT NULL`, `event_id TEXT NOT NULL` | key; the envelope's own `id` |
| `Name` | `string` | `name TEXT NOT NULL` | |
| `StartedAt` | `string` | `started_at TEXT NOT NULL` | |
| `EndedAt` | `string?` | `ended_at TEXT` | |
| `Success` | `bool?` | `success INTEGER` | a field rather than a string match — a measured 35 failures across 3,027 pairs |
| `OwnerKind`, `AgentId` | `OwnerKind`, `string?` | `owner_kind TEXT NOT NULL`, `agent_id TEXT` | ownership pair, see Invariants |

Index: `ix_hook_session` (`SessionId`).

## Permission — table `permission`

PK: (`SessionId`, `EventId`). One permission request.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId`, `EventId` | `string` (each) | `session_id TEXT NOT NULL`, `event_id TEXT NOT NULL` | key |
| `RequestedAt` | `string` | `requested_at TEXT NOT NULL` | |
| `CompletedAt` | `string?` | `completed_at TEXT` | a measured 1,033 requested against 1,031 completed means an unanswered request is a real state |
| `ResultKind` | `string?` | `result_kind TEXT` | from `permission.completed.data.result.kind`, an enum on Copilot rather than a string match |
| `ToolCallId` | `string?` | `tool_call_id TEXT` | |
| `OwnerKind`, `AgentId` | `OwnerKind`, `string?` | `owner_kind TEXT NOT NULL`, `agent_id TEXT` | ownership pair, see Invariants |

Index: `ix_permission_session` (`SessionId`).

## WriteUnit — table `write_unit`

PK: (`SessionId`, `EventId`). Published and never populated in v1: FR-36 is Phase E, gated out by
PRD §3.4.3 — the shape exists so later stories have something to compile against.

| Property | Type | DB | Notes |
|---|---|---|---|
| `SessionId`, `EventId` | `string` (each) | `session_id TEXT NOT NULL`, `event_id TEXT NOT NULL` | key |
| `ToolCallId` | `string` | `tool_call_id TEXT NOT NULL` | |
| `Path` | `string` | `path TEXT NOT NULL` | |
| `AddedContent` | `string` | `added_content TEXT NOT NULL` | |
| `OwnerKind`, `AgentId` | `OwnerKind`, `string?` | `owner_kind TEXT NOT NULL`, `agent_id TEXT` | ownership pair, see Invariants |

Index: `ix_write_unit_session` (`SessionId`).

### Invariants

- **Session-scoped natural keys.** Every derived entity keys off `SessionId` first — alone for
  `Session`, paired with a Copilot-issued id (`Turn`, `ToolCall`, `Agent`) or the event envelope's
  own `id` (`Skill`, `Hook`, `Permission`, `WriteUnit`) for the rest. Nothing is keyed globally.
- **Ownership is a value, not a convention.** `Turn`, `ToolCall`, `Skill`, `Hook`, `Permission` and
  `WriteUnit` carry `owner_kind TEXT NOT NULL` and `agent_id TEXT`; each table's `ck_<table>_owner`
  constraint binds them exactly: `(owner_kind = 'main') = (agent_id IS NULL)`. `Agent` carries
  neither — it is the owner-of-record, keyed by `agent_id` directly, so there is deliberately no
  nullable third ownership state.
- **The agent completion states are gated by `ck_agent_cost`.** `Agent.Outcome` is one of `Running`,
  `Completed`, `CompletedCostUnknown` or `Failed` — four states, not two, because cost data does not
  always accompany a completion. `agent.ck_agent_cost` requires:
  ```sql
  outcome = 'Completed' OR (total_tokens IS NULL AND total_tool_calls IS NULL AND duration_ms IS NULL AND model IS NULL)
  ```
  So cost metrics can accompany only a `Completed` outcome.

Message text is not in this layer — it is read from `raw_event`, keyed by the same `session_id`
these tables carry. `FileChange`, `RuleStatement` and `RuleSetVersion` are deliberately not among
the eight shapes documented here.
