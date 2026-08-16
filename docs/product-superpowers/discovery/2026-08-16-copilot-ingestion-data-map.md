# Copilot CLI → Insights ingestion: the data map

**Date:** 2026-08-16 · **Author:** measurement run on the reference machine · **Status:** research
only, nothing built

> **What this is.** A field-level map of what GitHub Copilot CLI writes to disk, against what
> AecoLedger Insights needs, so that adding Copilot as source #2 can be planned from measurements
> rather than from the capability matrix's static reference row.
>
> **What this is not.** Not a plan, not stories, not an approved scope. Part 9 lists the components
> a plan would have to cover; it does not sequence or size them.
>
> **Why now.** `src/AecoLedger.Insights.Ingestion/CLAUDE.md` records Codex and Copilot ingestion as
> post-v1 scope, and the PRD (§3.9) names Copilot as source #2 blocked on an unresolved cost
> question (Part 8, Q1). Part 7 of this document resolves that question and Part 8 corrects five
> claims in the discovery doc that the current corpus contradicts.

---

## Part 1: Method and corpus

Everything below was measured on this machine on 2026-08-16 by reading `~/.copilot/` directly. No
figure here is carried over from the 2026-08-13 discovery document; where the two disagree, Part 8
records the disagreement rather than silently overwriting it. Reproduction scripts are in the
Appendix.

| Corpus property | Measured |
|---|---|
| `~/.copilot/session-state/` session directories | 48 |
| …of those, holding an `events.jsonl` | 35 |
| Total `events.jsonl` bytes | 176.7 MB |
| Total event lines parsed | 56 176 |
| Lines that failed `json.loads` | 0 |
| Distinct event `type` values | 31 |
| `events.jsonl` mtime span (2026-04-20 → 2026-08-09) | 111 days |
| Per-session `session.db` files | 34 |
| `rewind-snapshots/index.json` files | 30 |
| Global `session-store.db` | 5.1 MB, plus a 4.0 MB `-wal` |
| Distinct `copilotVersion` values across 35 session starts | 14 (measured: `1.0.24` … `1.0.78`) |
| `session.start.data.version` (event-schema version) | `1` on all 35, measured |

The measured 111-day span is consistent with Repo Rule 1's "Copilot 99–111 depending on layer" and is the
practical argument for ingesting at all: this is the widest on-disk window of the three tools, and
it is still a rotating window.

---

## Part 2: What Copilot writes to disk

| Path | Shape | Measured presence | Insights relevance |
|---|---|---|---|
| `session-state/<sid>/events.jsonl` | JSONL, append-only, `session.start` on line 1 (35/35) | 35 dirs | **The primary source.** Everything in Parts 3–6 comes from here |
| `session-state/<sid>/session.db` | SQLite: `todos`, `todo_deps`, `inbox_entries` | 34 files; `todos` in 33, 243 rows total; `todo_deps` 38 rows; `inbox_entries` 0 rows | Weak. Sparse and success-biased (discovery §4.4) |
| `session-state/<sid>/rewind-snapshots/index.json` | `{version, snapshots[], filePathMap{}}` | 30 files | **Strong.** The `FileChange` source — see Part 5 |
| `session-state/<sid>/checkpoints/` | `index.md` | 46 dirs, all non-empty | Narrative only; no structured field |
| `session-state/<sid>/files/` | scratch | 46 dirs, 4 non-empty | None |
| `session-state/<sid>/research/` | — | 46 dirs, all empty | None |
| `session-state/<sid>/workspace.yaml` | workspace config | present per session dir | Possible cwd cross-check |
| `session-store.db` (global) | SQLite: `assistant_usage_events`, `sessions`, `turns`, `checkpoints`, 12 more | 1 file | **Secondary.** Best per-request fidelity, worst coverage — Part 5 |
| `logs/` | `process-<epoch>-<pid>.log` | 3 files, largest 7.6 KB | None. Plain text process logs |
| `logs/otel/` | — | **Does not exist** | Confirms discovery §4.4 and PRD Part 8 Q1 |

### The `session-store.db` WAL is not optional reading

Measured today, opening the same file two ways:

| Open mode | Measured `assistant_usage_events` rows | Measured `sessions` rows |
|---|---|---|
| `file:…?mode=ro&immutable=1` | 1 135 | 37 |
| `file:…?mode=ro` | 1 255 | 40 |

As measured, 120 usage rows and 3 whole sessions live only in the 4.0 MB write-ahead log. `immutable=1` — the
obvious choice for reading a foreign database safely — hides them silently and without error. Any
Copilot ingestor that touches this file must open read-only *without* `immutable`, and must treat
the result as a moving target rather than a snapshot.

---

## Part 3: The event census

Every event type measured, with its count, how many sessions carry it, and whether the event carries
the top-level `agentId`. Measured 2026-08-16 across the corpus in Part 1.

| Event type | Measured count | Measured sessions | Measured `agentId` |
|---|---|---|---|
| `tool.execution_start` | 16 085 | 33 | 11 702 |
| `tool.execution_complete` | 16 076 | 33 | 11 698 |
| `assistant.message` | 8 261 | 34 | 5 875 |
| `hook.start` / `hook.end` | 3 027 each | 34 | 0 |
| `assistant.turn_start` | 2 384 | 35 | 0 |
| `assistant.turn_end` | 2 375 | 34 | 0 |
| `permission.requested` | 1 033 | 25 | 0 |
| `permission.completed` | 1 031 | 25 | 0 |
| `skill.invoked` | 794 | 31 | 445 |
| `subagent.started` | 470 | 15 | 470 |
| `subagent.completed` | 462 | 15 | 462 |
| `user.message` | 413 | 35 | 0 |
| `system.message` | 337 | 34 | 0 |
| `system.notification` | 167 | 12 | 0 |
| `session.model_change` | 36 | 25 | 0 |
| `session.start` | 35 | 35 | 0 |
| `session.shutdown` | 31 | 29 | 0 |
| `session.workspace_file_changed` | 17 | 3 | 0 |
| `session.context_changed` | 11 | 9 | 0 |
| `session.usage_checkpoint` | 10 | 5 | 0 |
| `abort` | 9 | 8 | 0 |
| `session.info` | 8 | 7 | 0 |
| `session.compaction_start` / `_complete` | 7 each | 6 | 0 |
| `session.plan_changed` | 7 | 4 | 0 |
| `subagent.failed` | 6 | 2 | 6 |
| `session.error` | 6 | 5 | 0 |
| `session.warning` | 3 | 1 | 0 |
| `session.resume` | 2 | 2 | 0 |
| `session.remote_steerable_changed` | 1 | 1 | 0 |

### The envelope

Every line is `{type, data, id, timestamp, parentId}`, plus `agentId` on subagent-scoped events
only. Measured: 8 535 of the largest session's events carry `agentId`, 2 371 do not.

`id` / `parentId` form a causal chain across the whole file. This is the direct analogue of Claude's
`uuid` / `parentUuid`, and it is measured present on **100% of events** rather than on message lines
only.

### `agentId` is Observed subagent attribution, not a heuristic

Measured on the largest session: 115 distinct `agentId` values on tool events, and **all 115** are
`subagent.started.toolCallId` values from the same file. Zero `agentId` values that are not a known
subagent handle. Absence of `agentId` therefore means "main thread", exactly.

This is materially better than Claude, where subagent attribution needs a `.meta.json` sidecar and a
`toolUseId` correlation (FR-13). On Copilot the attribution is a field on every event.

---

## Part 4: What Insights needs

The consumer side, from `src/AecoLedger.Insights.Domain/docs/graph-contract.md` and the two detector
documents. This is the list any second source has to satisfy.

**Seven NORMALIZED entities:** `Session`, `Turn`, `ToolCall`, `Agent`, `FileChange`, `ModelUsage`,
`WorkflowPhase`.

**Six waste detectors:** FR-21 duplicate subagent exploration, FR-22 repeated file reads, FR-23
tokens before first verification, FR-24 failed tool calls, FR-25 low-value tool results, FR-26
rework loop.

**Plus** FR-15 skill token attribution, FR-16 resume gaps, FR-18…FR-20 verification extraction, and
FR-50's main/subagent/advisor token split on the session header.

**RAW's own requirements** (`docs/raw-contract.md`): a byte-addressable append-only line source, a
per-event provider version, and a resumable `(source_file, byte_offset, content_hash)` identity.

---

## Part 5: The mapping — Copilot field → Insights entity

Provenance uses discovery §5's ladder: **Observed** (read from a field the tool wrote), **Derived**
(computed deterministically), **Inferred** (needs a heuristic).

### RAW layer

| Requirement | Copilot source | Level | Measured coverage |
|---|---|---|---|
| Byte-addressable append-only lines | `events.jsonl`, one JSON object per line | Observed | 56 176 lines, 0 malformed |
| `RawEvent.ProviderVersion` | `session.start.data.copilotVersion`, always line 1 | Observed | 35/35 |
| Parser version range (FR-4) | `session.start.data.version` — an explicit event-schema version | Observed | `1` on 35/35 |
| Resume identity | file grows by append; `session.resume.eventsFileSizeBytes` records the size at resume | Observed | 2 resume events measured |

`TranscriptLineReader`, `RawEvent`, `RawEventHash`, `SqliteRawEventStore` and `InsightsStore` are all
provider-agnostic already — RAW needs a new discovery + parser pair, not a new store.

**One parser hazard:** `tool.execution_start.data.arguments` is polymorphic. It is a JSON object for
every tool measured except `apply_patch`, where it is a **string** carrying a patch envelope — 381 of
381 `apply_patch` calls measured. A projection that assumes an object silently drops every patch.

### NORMALIZED layer

| Entity | Field | Copilot source | Level | Measured coverage |
|---|---|---|---|---|
| `Session` | `SessionId` | `session.start.data.sessionId` | Observed | 35/35 |
| | cwd / repo / branch / commit | `session.start.data.context{cwd, gitRoot, branch, headCommit, repository, hostType, baseCommit}` | Observed | 35/35 |
| | mid-session change | `session.context_changed`, same shape | Observed | 11 events, 9 sessions |
| `Turn` | `Uuid` / `ParentUuid` | event envelope `id` / `parentId` | Observed | 100% of events |
| | turn boundaries | `assistant.turn_start` / `turn_end` `.data.turnId` | Observed | 2 384 / 2 375 |
| | `Role = User` | `user.message` | Observed | 413 |
| | `Role = Assistant` | `assistant.message` | Observed | 8 261 |
| | `Role = System` | `system.message`, `system.notification` | Observed | 337 / 167 |
| | `Role = Advisor` | **no equivalent** — see Part 7 | — | — |
| | `AgentId` | envelope `agentId` | Observed | subagent events only, by design |
| `ToolCall` | `ToolUseId` | `tool.execution_start.data.toolCallId` | Observed | 16 085 (100%) |
| | tool name | `.data.toolName` | Observed | 100% |
| | `StartedAt` / `CompletedAt` | envelope `timestamp` on the start/complete pair | Observed | 16 076 pairs |
| | `IsError` | `tool.execution_complete.data.success` | Observed | 100%; `error` object on 373 (2.3%) |
| | `ResultSizeBytes` | `.data.result.content` length, or `toolTelemetry.metrics.resultLength` | Derived / Observed | `result` 98%; `toolTelemetry` 39% |
| | `Command` | `.data.arguments.command` for `powershell`, plus `shellToolInfo{possiblePaths, hasWriteFileRedirection}` | Observed | `shellToolInfo` on 554 |
| | MCP provenance | `.data.mcpServerName` / `.mcpToolName` | Observed | 251 |
| `Agent` | `AgentId` | `subagent.started.data.toolCallId` (== envelope `agentId`) | Observed | 470 |
| | `SpawningToolCallId` | the same id, resolved against the `task` `tool.execution_start` | Observed | **470/470 resolve** |
| | `ParentAgentId` (nesting) | `agentId` on the spawning `task` call | Derived | 178/470 nested (37.9%) |
| | agent type / display name | `.data.agentName`, `.agentDisplayName`, `.agentDescription` | Observed | 100% of spawns |
| | end time, tokens, duration | `subagent.completed.data{totalTokens, totalToolCalls, durationMs, model}` | Observed | **215/462 (47%)** |
| | failure | `subagent.failed.data.error` | Observed | 6 events, 2 sessions |
| `FileChange` | `RealPath`, version, backup | `rewind-snapshots/index.json` → `snapshots[].files{}` with `{gitStatus, contentHash, backupFile, size, mtime}`, plus `filePathMap` | Observed | 30 of 35 sessions |
| | turn linkage | `snapshots[].eventId` → the envelope `id` of the originating event | Observed | present on every snapshot measured |
| | git anchor | `snapshots[].{gitCommit, gitBranch, userMessage, fileCount}` | Observed | present on every snapshot measured |
| | session-level rollup | `session.shutdown.data.codeChanges{linesAdded, linesRemoved, filesModified[]}` | Observed | 31/35 |
| | live file watch | `session.workspace_file_changed{path, operation}` | Observed | 17 events, **3 sessions only** |
| `ModelUsage` | per-message output tokens | `assistant.message.data.outputTokens` | Observed | 8 232/8 261 (99.6%) |
| | per-session full `TokenUsage` | `session.shutdown.data.modelMetrics{<model>: {requests{count,cost}, usage{inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens, reasoningTokens}}}` | Observed | **31/35 (89%)** |
| | per-request full usage | `session-store.db.assistant_usage_events` (23 columns incl. `total_nano_aiu`, `duration_ms`, `time_to_first_token_ms`, `token_details_json`) | Observed | **7 of 40 sessions (17.5%)** |
| | context size at a point | `session.shutdown.data{currentTokens, systemTokens, conversationTokens, toolDefinitionsTokens}` | Observed | 31/35 |
| | model identity | `tool.execution_complete.data.model` | Observed | 16 076 (100%) |
| `WorkflowPhase` | resume gaps (FR-16) | `session.resume.data{resumeTime, eventCount, context}` | Observed | 2 events |
| | phase labels | `report_intent` tool calls, `.data.arguments.intent` | Observed | **2 167 calls** |
| | compaction span | `session.compaction_start{systemTokens, conversationTokens, toolDefinitionsTokens}` → `session.compaction_complete{preCompactionTokens, preCompactionMessagesLength, compactionTokensUsed, summaryContent, checkpointPath, success}` | Observed | 7 pairs, 6 sessions |
| | abort | `abort.data.reason` | Observed | 9 events, 8 sessions |

### ANALYTICS layer — the six detectors

| Detector | What it needs | Copilot answer | Verdict |
|---|---|---|---|
| **FR-21** duplicate subagent exploration | agents, per-agent read sets, per-agent tokens | Agents 100%-linked; read sets from `view.arguments.path` scoped by `agentId`; tokens from `subagent.completed.totalTokens` | **Partial** — read sets and overlap are exact; the token price is 47%-covered, so impact must fall back to counted for the rest |
| **FR-22** repeated file reads | tool calls + a path per read | `view.arguments.path` on 5 201 of 5 201 `view` calls | **Full**, and cleaner than Claude — see Part 7 |
| **FR-23** tokens before first verification | first verification tool call + turn tokens | Verification detectable from `powershell.arguments.command`; turn tokens only as `outputTokens` | **Partial** — ordering is exact, the token denominator is output-only |
| **FR-24** failed tool calls | `IsError` per call | `success` on 16 076 of 16 076 | **Full**, Observed |
| **FR-25** low-value tool results | result size per call | `result.content` length on 98%; `toolTelemetry.metrics.resultLength` on 39% | **Full**, Derived from content length |
| **FR-26** rework loop | file changes grouped by path + turn tokens | `rewind-snapshots` gives per-path version history with a git hash per version | **Partial** — grouping is exact, the token price is output-only |

### Signals Copilot has that Claude does not

These are not required by any current FR, and are recorded because they are the reason the PRD calls
Copilot's data richer:

- **`permission.completed.data.result.kind`** — an enum, so permission denial is **Observed** on
  Copilot where discovery §5 records it as Inferred on Claude (string matching) and Absent on Codex.
  1 031 events measured across 25 sessions.
- **`hook.end.data.success`** — hook failure as a field.
  Measured: 35 failures across 3 027 hook pairs, 1.2%.
- **`report_intent`** — 2 167 self-declared phase labels. Discovery §4.4 calls a non-monotonic
  intent sequence the best wandering proxy found on any tool.
- **`skill.invoked`** — 794 events measured, carrying `{name, path, description, pluginName, pluginVersion}`,
  a structured skill boundary rather than a token-attributed inference (FR-15).
- **`session.error.data{errorType, statusCode, providerCallId}`** — structured API failure, 6 events.

---

## Part 6: What has no Copilot equivalent

| Insights concept | Status on Copilot | Consequence |
|---|---|---|
| `Turn.Role = Advisor` | No `iterations[]`-equivalent structure found | FR-50's three-way split degrades to main/subagent. The wire contract (`TokenSplitDto`) already names its rule, so the honest move is a two-way split with the rule named, not a fabricated third bucket |
| Per-turn **input** and **cache** tokens | Only `outputTokens` per message (99.6%); full usage exists per-session (89%) or per-request at 17.5% coverage | Every **priced** signal (FR-21, FR-23, FR-26) is either output-token-priced or session-apportioned. Both are Derived, not Observed — and must say so |
| Per-tool-call tokens | Absent, as on Claude | Confirms FR-51 was the right rule, not a Claude quirk |
| Post-compaction context size | `preCompactionTokens` exists; no `postCompactionTokens` | Compaction delta remains unmeasurable on all three tools |
| A dollar cost | Copilot prices in **premium requests** and **nano-AIU**, not currency | See Part 7 |
| Outcome truth (merged / reverted / reviewed) | Absent, as on all three tools (discovery §4.5) | Unchanged; L4 still needs GitHub |

---

## Part 7: The two findings that change existing decisions

### 1. PRD Part 8 Q1 — the Copilot cost blocker — is resolved, at session granularity

The PRD records Copilot as blocked because `logs/otel/` does not exist and `assistant_usage_events`
covers a small minority of sessions. Both halves still hold: `logs/otel/` is absent today, and the
usage table is measured at 7 of 40 sessions, 17.5%.

But there **is** a third token source, and it is in `events.jsonl`:

```
session.shutdown.data.modelMetrics = {
  "gpt-5.4":          { requests: {count: 284, cost: 15},
                        usage: {inputTokens, outputTokens, cacheReadTokens,
                                cacheWriteTokens, reasoningTokens} },
  "claude-sonnet-4.5": { ... }, ...
}
```

Measured present on 31 of 35 sessions, 89%. It carries the complete five-field usage breakdown per
model — the exact shape of `AecoLedger.Core.Domain.TokenUsage` — plus a request count and a `cost`
in premium requests.
Also measured: `session.usage_checkpoint.data.totalNanoAiu` on 10 events across 5 sessions.
Also measured: `session.shutdown.data.totalNanoAiu` on 8 of 31, the same figure in nano-AIU.

Two things this does and does not settle:

- **It settles session-level token totals** at the measured 89% coverage, Observed, without OTEL and without the
  live database. That is enough for the session header (FR-50) and for any session-scoped signal.
- **It does not settle per-turn attribution.** Distributing a session total across turns is
  apportionment — Inferred, and it must be labelled that way.
- **It does not produce a dollar figure.** Copilot's own unit is the premium request; `nano_aiu` is
  an internal accounting unit. `assistant_usage_events.token_details_json` carries a
  `{batchSize, costPerBatch, tokenType}` table per request, which is a *rate card in nano-AIU* — a
  conversion to currency still needs a nano-AIU-to-dollar rate that no local file states. **This is
  the residual open question**, and it is narrower than the one the PRD records.

### 2. Copilot fixes the FR-21/FR-22 read-path gap by construction

`src/AecoLedger.Insights.Domain/CLAUDE.md` records a gap: `ToolCall` has no general file-path field,
so `Read`/`NotebookRead` paths are discarded during Claude reconstruction and FR-21 and FR-22 work
around it with caller-supplied lookups.

On Copilot the read tool is `view` and its argument is `{path, view_range}` — measured on 5 201 of
5 201 calls. Combined with `agentId` on the same event, a per-agent read set is a one-pass group-by
with no lookup, no correlation and no heuristic.

This is the PRD's stated reason for Copilot being second — a second source that tests whether the
schema generalises. It does generalise, and it also argues concretely for closing that gap on the
entity rather than working around it twice.

---

## Part 8: Corrections to existing documents

Recorded rather than silently overwritten, per evidence discipline. All five are cases where the
current corpus contradicts a claim in `docs/product-superpowers/discovery/2026-08-13-agent-work-analytics.md` §4.4.

| Claim in discovery §4.4 | Measured 2026-08-16 |
|---|---|
| "**Absent:** … explicit aborted-turn event" | `abort` exists — 9 events across 8 sessions, `{reason}` |
| "**Absent:** … subagent pass/fail flag" | `subagent.failed` exists — 6 events across 2 sessions, `{error, toolCallId, agentName}` |
| "**Absent:** … any resume/rewind event (`restart/` is empty, no resume field on `sessions`)" | `session.resume` exists — 2 events, `{resumeTime, eventCount, selectedModel, context, eventsFileSizeBytes}` |
| "There is no `postCompactionTokens` field — `compactionTokensUsed` is the cost of the summarization call" | Still true, and `preCompactionTokens` **is** present on 7/7 `compaction_complete` events, so the pre-side is Observed |
| "`assistant_usage_events` … covers only 5 distinct sessions of 37 (13.5%)" | 7 of 40 (17.5%) — and the drift is explained: the file is live-written and the earlier figure likely excluded the WAL |

Also not recorded anywhere before: `session.shutdown.modelMetrics` (Part 7), `session.usage_checkpoint`,
`session.model_change`, `session.workspace_file_changed`, `session.plan_changed`, `session.info`,
`session.warning`, `session.context_changed`, `session.remote_steerable_changed`.

**Consequence for `SourceRegistry/source-coverage-registry.json`.** Its Copilot row is static
reference data seeded from discovery §4.1 (`docs/source-registry.md`). At minimum the rows for
aborted turns, subagent failure and resume are now wrong in the conservative direction. The registry
type already carries `IsLiveMeasurement = false`, so this is a data correction, not a design fault —
but it should be corrected in the same change that ingests Copilot, when those rows become computed.

---

## Part 9: What a Copilot ingestion would have to build

Component inventory only. No sequencing, no sizing, no approval implied.

**Reused unchanged.** `RawEvent`, `RawEventHash`, `IRawEventStore`, `SqliteRawEventStore`,
`InsightsStore` and its schema, `TranscriptLineReader`, `InsightsStorePurge`/`Statistics`. RAW is
already provider-agnostic; `Provider` is a column.

**New in `AecoLedger.Insights.Ingestion`:**

1. `Discovery/CopilotSessionPaths` — resolve `~/.copilot/session-state/`, classify `events.jsonl`,
   `session.db`, `rewind-snapshots/index.json`; report a missing directory rather than throwing,
   matching `ClaudeTranscriptPaths`.
2. `Discovery/CopilotEventLineParser` — validate JSON, read `copilotVersion` from line 1 rather than
   scanning for a first-declared version, and register a `version: 1` parser range.
3. `Discovery/CopilotEventIngestor` + `CopilotIngestionPipeline` — the `ClaudeTranscriptIngestor`
   analogue, reusing the resume / rotation / content-mismatch rules verbatim.
4. A decision on `rewind-snapshots/index.json`: it is a single JSON object like `.meta.json`, so the
   existing "one file, one RAW event" rule applies — but it is **rewritten in place** as the session
   grows, which `.meta.json` is not. RAW's "keeps both versions at the same offset" rule handles
   that correctly; it should be stated deliberately rather than inherited by accident.
5. A decision on `session-store.db`: whether to ingest it at all. It is live-written, WAL-dependent
   (Part 2), covers a measured 17.5% of sessions, and everything it uniquely offers is per-request latency and
   nano-AIU. Leaving it out of v1 of Copilot ingestion loses no entity in Part 5.
6. `Command/CopilotIngestCommand` and self-exclusion by `session.start.data.context.cwd` — the
   self-contamination requirement (discovery §5.2) applies identically.

**New in `AecoLedger.Insights.Domain`:** nothing, if the extractors stay honest to their contract.
The project rule is that extractors take already-parsed shapes, not `RawEvent`. A Copilot
`TurnGraphBuilder` analogue belongs beside the existing one; the six detectors and the threshold
seam should need no change, which is itself the test of whether the schema generalised.

**New in `AecoLedger.Insights.Api/Serving/Assembly/`:** a Copilot projection alongside
`SessionGraphAssembler` — this is where the RAW decode lives and therefore where the
Copilot-envelope-to-entity mapping in Part 5 is implemented.

**Contract-level decisions a plan must settle first**, because they are visible on the wire:

- How a source-agnostic session id, provider tag and provenance level reach the API contract.
- Whether FR-50's split degrades to two-way on Copilot, and how that is named on the wire.
- Whether priced signals fall back to output-token pricing, session apportionment, or counted
  impact when full per-turn usage is unavailable — and how the chosen fallback is labelled.

---

## Part 10: Open questions

1. **Nano-AIU to currency.** No local file states the rate. Without it, Copilot cost is expressible
   only in premium requests or nano-AIU. Is a non-currency unit acceptable on the session header,
   or does Copilot ingestion need a configured rate?
2. **Session-level tokens vs. per-turn signals.** Is an apportioned per-turn token figure, labelled
   Inferred, useful — or does it violate the product's own provenance discipline enough that priced
   signals should degrade to counted on Copilot?
3. **`session-store.db` in or out.** Recommendation: **out** of the first Copilot ingestion, per
   Part 9 item 5.
4. **Does the enriched registry row become computed in the same change?** `docs/source-registry.md`
   says the interface was shaped for exactly this. Part 8 shows the static row is already wrong.

---

## Part 11: Content retrieval — what is readable, per agent

Added after the map above, answering a different question: not "can the entities be built" but "can a
reader see what each agent was told, what it thought, and what it produced". Measured 2026-08-16 over
the same corpus.

| Wanted | Copilot source | Measured |
|---|---|---|
| The user's prompts | `user.message.data.content`, plus `transformedContent` | 413 events, all 35 sessions |
| A subagent's spawning prompt | `tool.execution_start` `task` → `arguments.prompt`, alongside `description`, `agent_type`, `name`, `model`, `mode`, `reasoning_effort` | 486 prompts, longest 8 779 chars, 791 819 chars total |
| An agent's prose output | `assistant.message.data.content`, plaintext string on 8 261 of 8 261 | 2 714 carry prose; longest single message 33 925 chars |
| A subagent's **final** output | the **last `assistant.message` bearing that `agentId`** — *not* the parent's tool result | 470 subagents carry their own messages: median 9, max 79 |
| The agent's reasoning to itself | `assistant.message.data.reasoningText` | 1 252 messages, plaintext, longest 16 495 chars |
| …the rest of the reasoning | `reasoningOpaque` | 6 627 messages, provider-encrypted, **not readable** |
| Questions asked of the user | `ask_user` `arguments{question, choices, allow_freeform}` and its result `"User selected: …"` | 124 asked, 124 answered |
| Permission prompts | `permission.requested.promptRequest` → `permission.completed.result.kind` | 1 033 → 1 031 |
| Tools used | `tool.execution_start.data.toolName` + full `arguments` | 16 085 calls |
| MCP servers and their tools | `mcpServerName` / `mcpToolName` | 251 calls, measured across `codebase-memory-mcp`, `github-mcp-server`, `ide` |
| Skills used | `skill.invoked{name, path, description, pluginName, pluginVersion, content}` — `content` is the **full SKILL.md text** | 794 invocations; 445 of them scoped to a subagent |
| **The rules the agent operated under** | `system.message.data.content` — the complete system prompt, including `<custom_instruction>` blocks holding **verbatim `AGENTS.md` and `CLAUDE.md`** | 337 events, median 54 335 chars, longest 59 982 |

### The rules are captured verbatim, and this is the strongest finding of the section

`system.message` is not a summary or a reference — it is the literal prompt text, with the repo's own
instruction files inlined in `<custom_instruction>` blocks.
Measured across the 337 system messages: 335 contain `CLAUDE.md`.
Measured: 158 contain `Repo Rules`, 93 contain `codebase-memory`, 89 contain `AGENTS.md`.
A rule such as "prefer querying codebase-memory-mcp over Glob/Grep/Read for navigation"
is recoverable as the exact sentence the agent was given, per session.

### Two limits that shape any product built on this

**1. `system.message` is main-thread only.** Measured: 336 of 337 share the same opening line.
Measured: a session carries 1–3 distinct system-prompt texts, and the session holding the most such
events, measured at 66 of them, still holds only 2 distinct texts.
No `system.message` carries an `agentId`. So a *subagent's* own system prompt is never
recorded. What a subagent's rules can be shown from instead, all Observed:
`subagent.started.agentDescription`, its `task.arguments.prompt`, and the `skill.invoked` events
carrying its `agentId`. Attributing the session's `<custom_instruction>` blocks to a subagent is
inheritance — **Inferred**, and must be labelled so.

**2. The parent's log truncates the subagent's report.**
Measured on `read_agent` completions: 200 results, median 48 chars, longest 266, ending in the
literal marker `(Full response provided to agent)`.
Background `task` completions return only a handle. The full text is not lost — it is in the
subagent's own message stream under its `agentId` — but a reader that follows the parent's tool
result sees a stub. Any UI must reconstruct subagent output from the agent-scoped stream.

### Not a limit, though it looks like one

Measured: 5 547 of 8 261 `assistant.message` events carry empty `content`.
Measured breakdown: 5 518 of those are tool-call-only messages carrying `toolRequests`.
Measured: just **29 of 8 261, 0.35%**, are empty with neither prose nor tool calls.
`encryptedContent` is a parallel field, not a replacement — `content` is a plaintext string on every
event.

### Volume, for a product that stores all of this

From the measured median, system prompts alone are 337 × 54 KB of near-duplicate text.
Measured: subagent prompts total 791 819 chars.
A per-agent view is buildable, but system-prompt de-duplication by content hash is a design
question, not an optimisation to defer.

---

## Self-review — what was checked and how

- Every figure in Parts 1–6 was produced by the Appendix scripts on 2026-08-16, reading
  `~/.copilot/` directly. Nothing was carried forward from discovery §4.4; the five disagreements
  are in Part 8.
- The `agentId` claim was checked against the failure mode discovery §5.2 warns about — a circular
  query that confirms the schema. It was verified by cross-referencing `agentId` on tool events
  against `subagent.started.toolCallId` from the same file, which is the non-circular direction, and
  the nesting figure was derived the same way rather than from an `agent_id == parent_tool_call_id`
  comparison.
- The WAL finding was checked by opening the same file both ways in one script rather than inferred
  from two runs at different times.
- **Append-only, since checked.** Measured after the first draft: on all 8 events carrying
  `eventsFileSizeBytes` — 7 `session.shutdown` and 1 `session.resume` — the declared value equals the
  byte offset at which that same event begins, delta 0 in every case. A file rewritten rather than
  appended to could not hold that relationship, so byte offsets are safe to use as RAW identity, and
  a resumed session continues the same byte stream. This was listed as unchecked in the first draft.
- **Not checked:** whether any Copilot session in this corpus is an Insights-analysis session that
  would self-contaminate its own signals.
- **One corpus, one machine, 14 CLI versions measured.** Everything here is an observation about this
  corpus, per discovery §5.1 — never a format guarantee.

---

## Appendix: reproduction

Event census and field coverage:

```python
import json, glob, os, collections
keycov = collections.defaultdict(collections.Counter)
typecount, agentid, sess = collections.Counter(), collections.Counter(), collections.defaultdict(set)
for p in glob.glob(os.path.expanduser("~/.copilot/session-state/*/events.jsonl")):
    sid = os.path.basename(os.path.dirname(p))
    for line in open(p, "rb"):
        if not line.strip():
            continue
        e = json.loads(line)
        t = e.get("type", "<none>")
        typecount[t] += 1
        sess[t].add(sid)
        if "agentId" in e:
            agentid[t] += 1
        for k in (e.get("data") or {}):
            keycov[t][k] += 1
for t, c in typecount.most_common():
    print(t, c, len(sess[t]), agentid[t], dict(keycov[t]))
```

Subagent linkage and nesting (the non-circular derivation):

```python
import json, glob, os
nested = root = 0
for p in glob.glob(os.path.expanduser("~/.copilot/session-state/*/events.jsonl")):
    spawner, starts = {}, []
    for line in open(p, "rb"):
        e = json.loads(line)
        if e["type"] == "tool.execution_start" and e["data"].get("toolName") == "task":
            spawner[e["data"]["toolCallId"]] = e.get("agentId")
        elif e["type"] == "subagent.started":
            starts.append(e["data"]["toolCallId"])
    for tc in starts:
        if tc in spawner:
            nested, root = (nested + 1, root) if spawner[tc] else (nested, root + 1)
print(nested, root)
```

The WAL check:

```python
import sqlite3, os
p = os.path.expanduser("~/.copilot/session-store.db")
for uri in (f"file:{p}?mode=ro&immutable=1", f"file:{p}?mode=ro"):
    con = sqlite3.connect(uri, uri=True)
    print(uri, con.execute("select count(*) from assistant_usage_events").fetchone()[0])
    con.close()
```
