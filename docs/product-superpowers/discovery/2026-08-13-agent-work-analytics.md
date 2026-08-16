# AecoLedger Insights — Product Discovery

**Date:** 2026-08-13
**Status:** Approved (conditional — v1 scoped to JTBD-A)
**Evidence base:** direct inspection of real on-disk logs for Claude Code, Codex CLI and GitHub Copilot CLI on the reference machine. All counts below are observations of *that corpus at those CLI versions*, not format guarantees. See §5 on why that distinction is structural, not a caveat.

---

## 1. Problem Statement

AecoLedger today answers *"how much did I spend?"* It cannot answer *"was it worth it?"*

The stakeholder runs multi-agent AI coding workflows (Superpowers skills, subagent fan-out, GitHub-issue-tracked tasks) across three tools with no way to see which runs succeeded, which failed, which burned tokens on rework, or which agent/model/workflow combination is actually efficient. The felt pain is specific: *"finding the patterns you currently feel are wasting tokens"* — a hypothesis about waste with no instrument to test it.

## 2. Desired Outcome

> **Reduce token spend per merged unit of work by identifying and eliminating the top 3 waste patterns, measured over a rolling 30-day window.**

The baseline is currently unmeasurable; establishing it is deliverable #1. Secondary outcome: reduce time-to-diagnosis of a failed agent run from "read the whole transcript" to "open one screen."

### 2.1 On the proposed efficiency formula

The originally proposed metric was:

```
Engineering Efficiency = Quality of Outcome / (Tokens × Cost × Time × Rework)
```

Two problems, both accepted by the stakeholder:

1. **`Tokens × Cost` double-counts.** Cost is a deterministic function of tokens and model. Multiplying them squares the token term while appearing to weigh two independent factors. Use cost alone (it encodes model choice) or tokens alone (model-neutral) — not both. An additive form (`Token + Time + Cost + Rework`) fixes the squaring but still carries the same quantity in two units.
2. **`Quality of Outcome` has no data source.** No log in any of the three tools records whether the work was correct, merged, reviewed, or reverted. See §4.5.

**Decision: do not ship a single composite "Engineering Efficiency" score in v1.** Show the dimensions independently until there are enough real runs to know how they should be weighted. A composite score invented before the weights are known will be defended rather than tested.

## 3. Jobs-to-be-Done

Three jobs surfaced. They are **not the same product**, and conflating them is the main scoping risk.

### JTBD-A — Post-mortem a single run *(diagnostic, per-session)* — **v1 SCOPE**
> When an agent run finishes and I'm not sure what actually happened, I want to see the tree of what it and its subagents did, what failed, and where the tokens went, so I can tell whether to trust the result and what to fix in my prompt or workflow.

- **Push:** reading a 5,000-line JSONL transcript by hand, or trusting a summary that may paper over failures.
- **Pull:** one screen showing the run as a tree with failures highlighted.
- **Anxiety:** "does the tool's notion of failure match mine?"
- **Habit:** skimming terminal scrollback and moving on.

### JTBD-B — Compare workflows and models over time *(longitudinal, portfolio)* — **destination, not v1**
> When deciding whether Opus-with-Superpowers beats Sonnet-with-plain-prompting for a class of work, I want cost-per-successful-outcome by model, workflow phase, and task type, so I can stop paying for ceremony that doesn't pay off.

- **Anxiety:** small samples; one bad run distorting a model's average.

### JTBD-C — Catch waste as it happens *(operational)* — **deferred**
> When a session is spiralling — re-reading the same files, repeatedly failing the same test, thrashing between two approaches — I want to be told, so I can interrupt before it burns another 200k tokens.

**Scope boundary:** JTBD-A is buildable from disk today. JTBD-B requires an external outcome-truth layer. JTBD-C requires near-real-time ingestion — a different architecture from AecoLedger's read-on-request model.

## 4. Research Findings — Signal Inventory

Surveyed across two rounds by six parallel agents: ~40 Claude `.jsonl` transcripts across 17 projects (157 top-level sessions, 342 subagent transcripts corpus-wide); 25 Codex rollout files plus 4 SQLite DBs; 40 Copilot sessions plus `session-store.db`, 35 per-session `events.jsonl` (56,138 lines) and per-session `session.db`.

### 4.1 Headline: the three sources are wildly asymmetric

| Capability | Claude Code | Codex CLI | Copilot CLI |
|---|---|---|---|
| Structured tool success/failure | strong (`is_error`) | weak (exit code in prose) | **strong** (`success` + `error.code`) |
| Subagent attribution | **strong** (own transcripts + `.meta.json`) | medium (thread graph; prompts encrypted) | **strong** (`subagent.completed` + `agentId` on every event) |
| Subagent nesting observed | yes, rare (3 of 342 at depth 2, via `parentAgentId`) | no (depth 1 only, 0 of 14) | **yes, common** (38% of spawns; 10 of 15 sessions) |
| Subagent parallelism observed | **common** (9 of 29 sessions, dozens of pairs) | **rare** (1 of 42 sibling pairs) | **common** (11 of 15 sessions) |
| Skill/workflow attribution | **strongest** (`attributionSkill` on later turns) | none | strong (`skill.invoked` + `report_intent` + `todos` table) |
| Turn delimiters | derived from entry sequence | **strongest** (`task_started`/`task_complete` + duration) | strong (`turn_start`/`turn_end`) |
| Compaction measurement | **not observed** in surveyed corpus | not observed | partial (before-side only) |
| Permission denials | inferred (54 exact-string matches) | 1 case, via patch failure | **observed** (`result.kind` enum) |
| Rework / diffs | strong (`file-history` backups, diffable) | **strongest** (`unified_diff` per patch) | medium (hashes, no diff bodies) |
| Explicit latency fields | none (derive from timestamps) | task + MCP durations | **strong** (`duration_ms`, `time_to_first_token_ms`) |

**Product implication:** a single cross-source score would silently compare instruments of different precision. Copilot would appear to fail more often simply because it *records* failure honestly. Every metric must be gated on per-source signal availability — see §5.

### 4.2 Claude Code — `~/.claude/`

Entry types (15-file sample): `assistant` 1246, `user` 794, `attachment` 204, `last-prompt` 188, `mode` 182, `ai-title` 172, `permission-mode` 121, `system` 118, `file-history-snapshot` 93, `queue-operation` 37, `file-history-delta` 36, `bridge-session` 9, `frame-link` 1.

Signals AecoLedger currently discards:

- **`attributionSkill` / `attributionPlugin` / `attributionAgent`** on assistant entries — attributes *downstream* token spend back to the invoking skill. The best workflow-cost signal found in any of the three tools, and **richer than first assessed: 9,248 occurrences** with a well-populated distribution — `github-issue-start` 2198, `superpowers:writing-plans` 956, `subagent-driven-development` 824, `using-git-worktrees` 691, `github-issue-sync` 639, `github-issue-commit` 515, `brainstorming` 506, `pm-parallel-research` 480, `run` 431, `test-driven-development` 388, `dispatching-parallel-agents` 297, `finishing-a-development-branch` 249. This is a real workflow dataset already on disk, not a thin signal — it is the single strongest asset for analysis #3.
- **Subagent transcripts** at `<session>/subagents/agent-<id>.jsonl` + `.meta.json{agentType, description, toolUseId, spawnDepth, model, parentAgentId}`. `toolUseId` links to the parent's `Agent` tool_use. Corpus: 342 total, **3 at depth 2** (all carrying non-null `parentAgentId`) — nesting is real but rare.
- **Subagent parallelism is common, not rare.** Across 29 parent sessions with ≥2 subagent transcripts, **9 (31%) contain at least one overlapping pair**, with dozens of overlapping pairs in total (one 15-agent session has 51 overlapping pairs, longest overlap 13.4 min). The largest sessions (28/26/21 agents) *are* strictly sequential — that narrower observation holds — but they are not representative. Spans derived from each transcript's first/last `timestamp`; overlaps run to many minutes, far beyond any write-latency artifact.
- **Subagent result delivery**: the parent receives a `user` entry with the subagent's full `<result>` inlined — provable delivery (not provable *use*).
- **Tool results**: `user.toolUseResult` = `{stdout, stderr, interrupted, isImage, noOutputExpected, timedOutAfterMs, persistedOutputSize}`. `timedOutAfterMs` observed 25× (20s/30s/120s). **1033 results are ≤3 characters** — a waste signal.
- **Failures**: `is_error: true` — **653 occurrences**. `isApiErrorMessage: true` — **only 7 occurrences corpus-wide** (an earlier "30 files" figure counted files containing the *field*, which is usually `false`). API/rate-limit errors are **rare**, so the rate-limit component of analysis #9 is a much smaller effect than first implied. `timedOutAfterMs`: 14 occurrences.
- **Permission denials**: 54 occurrences of the exact literal `"The user doesn't want to proceed with this tool use. The tool use was rejected..."` paired with `is_error: true`. Categorically distinct from interrupts (`"[Request interrupted by user]"`, ~40 files). **`permissionDecision`/`permissionDecisionReason`: 0 hits** — this is string matching, not a field.
- **`permission-mode` snapshots**: `acceptEdits` 1147, `default` 927, `auto` 34, `plan` 5, `bypassPermissions` 4. Standalone rows, not transitions.
- **Resume marker**: `system.subtype = "away_summary"` — **94 occurrences**, sits immediately before long gaps (one observed: 418 minutes). Filename UUID always equals `sessionId` (zero mismatches); resume **appends to the same file**, so fragmentation is in-file time gaps, not multi-file stitching.
- **`bridge-session`** is local↔claude.ai cloud sync bookkeeping (`bridgeSessionId: cse_...`, `lastSequenceNum`), **not** a fork/resume link. `frame-link` records Artifact iframe opens.
- **Rework**: `file-history-delta{trackingPath, backup.version}` plus readable plain-text backups at `~/.claude/file-history/<sessionId>/<hash>@vN` — **99 session directories**. Diff reconstruction verified independently (a 31-line unified diff produced from two adjacent versions). Real path recoverable via `realParentDir` + `trackingPath`.
  - **But the churn tail is thin.** Version-count histogram across 731 tracked files: 1 version 341, 2 versions 343, 3 versions 35, 4 versions 6, 5–8 versions 6. **94% have ≤2 versions**; only ~47 files have 3 or more. Churn factor (#23) is computable but will have few high-churn examples to learn from — the metric is sound, the sample for calibrating a "high churn" threshold is small.
- **Task tracking**: no `~/.claude/todos/`, zero `TodoWrite` usage. Instead `TaskCreate` (205) / `TaskUpdate` (365) with `pending → in_progress → completed`.
- **Tool census** (corpus-wide): `Bash` 5429, `Read` 3543, `Edit` 1662, `Grep` 1027, `Write` 641, `Glob` 387, `TaskUpdate` 365, `Agent` 350, `Skill` 324, `ToolSearch` 291, `WebSearch` 286, `WebFetch` 244. MCP: `codebase-memory-mcp` 724 calls, `github` 281, `sonarqube` 49, **`serena` 20**.
- **Model/effort variance**: **2 of 158** top-level sessions contain >1 distinct model — **and one of the two is this discovery session itself** (a `/model` switch made while doing this analysis). The genuine pre-existing count is **1 of 157**. `effort` is `"high"` on all 27,799 occurrences — zero variance. Analysis #18 has essentially no natural experiment to study on Claude. (Second confirmed instance of §5.2 self-contamination.)

**Not observed in this corpus/version:** any compaction marker (`isCompactSummary`, `type: "summary"`, `compactMetadata` — all zero, independently re-verified). Any exit-code field on Bash results — `exitCode` exists 1808× but **only on hook invocations**.

**Trap:** entries can carry both `sessionId` and a different `session_id` pointing at another transcript. `sessionId` is the reliable file-scoped key.

### 4.3 Codex CLI — `~/.codex/`

Envelope types (25 files): `response_item` 1854, `event_msg` 945, `world_state` 123, `turn_context` 51, `inter_agent_communication_metadata` 34, `session_meta` 25. Payload types: `message` 465, `reasoning` 414, `function_call` 273, `function_call_output` 272, `agent_message` 197, `custom_tool_call(_output)` 192 each, `task_started` 51, `task_complete` 46, `patch_apply_end` 44, `user_message` 36, `mcp_tool_call_end` 29, `sub_agent_activity` 20, `tool_search_call/output` 6 each, `turn_aborted` 5, `web_search_end` 4.

- **`task_started` / `task_complete` are the cleanest turn delimiters in any of the three tools.** `task_complete` = `{type, turn_id, last_agent_message, started_at, completed_at, duration_ms, time_to_first_token_ms}` — exactly 7 keys across all 46 records. Accounting is airtight: 51 started = 46 completed + exactly 5 `turn_aborted`, never dangling. **No `status`/`success`/`outcome` field exists** — pass/fail is prose in `last_agent_message`.
- **`patch_apply_end`** — `{success, stdout, stderr, changes.<path>: {type, unified_diff}}`. 43 success / 1 failure (`"approval request aborted"`). The richest rework signal anywhere — **with one gap: 55 of 63 change entries carry a `unified_diff`, 8 do not** (~13%), so churn measurement on Codex has a hole and cannot assume every change is diffable.
- **`turn_aborted`** — `{turn_id, reason: "interrupted", started_at, completed_at, duration_ms}`.
- **`mcp_tool_call_end`** — `{invocation:{server,tool,arguments}, duration:{secs,nanos}, result}`. Errors live *inside* `result` text (5 of 29), not a flag.
- **`token_count.rate_limits`** — `{primary.used_percent, window_minutes, resets_at, plan_type}`, 522 instances.
- **`state_5.sqlite.threads`** (25 rows) — `tokens_used, model, reasoning_effort, agent_nickname, agent_path, git_branch, git_sha, first_user_message`.
- **`session_meta.payload.git`** = `{commit_hash, branch, repository_url}`.
- **Tool census**: `exec` 171, `apply_patch` 21, `collaboration.wait_agent` 99, `shell_command` 76, `wait` 46, `update_plan` 15, `spawn_agent` 14. MCP: **only `codebase-memory-mcp`** (29 calls). No serena, sonarqube, or github MCP.
- **Structured timeout flag**: 84 occurrences of `{"message":"Wait timed out.","timed_out":true}` from subagent polling — a genuine boolean, and itself a waste signal (parent tokens spent polling).
- **Model/effort variance**: **0 of 25** threads change model mid-thread. Variance is across sibling subagents; one chain escalates `high` → `xhigh` (`final_reviewer`) → `high` (`final_fixer`).
- **Topology**: 14 spawns, all `depth: 1`, **no nesting**. 13 of 14 strictly sequential; one genuine ~8-minute overlap (`final_fixer` nested inside `final_reviewer`'s span).

**Absent / unusable:**
- `spawn_agent`'s task prompt is **Fernet-encrypted**; `first_user_message` is `""` for every subagent thread.
- `reasoning` payloads: **100% encrypted** (`encrypted_content` blob, `summary: []` in all 414).
- `wait_agent` returns only `{timed_out}` booleans, never subagent content.
- `thread_spawn_edges.status` is `"open"` on all 14 rows, never updated — **not** a completion signal.
- `sub_agent_activity.kind` ∈ `{started: 14, interacted: 6}` only — no `completed`/`failed`.
- `goals_1.sqlite` (with a `complete`/`blocked`/`budget_limited` status enum) and `memories_1.sqlite`: **schema present, zero rows**.
- `logs_2.sqlite`: 0 rows, autoincrement reached 104,493 — rotating internal log.
- `world_state` is an environment/config snapshot, **not** a file/repo snapshot.
- No `error`/`refusal`/`stream_error` payload type. No resume/fork field on root sessions.

### 4.4 Copilot CLI — `~/.copilot/`

**No `logs/otel/` directory exists on this machine** — the OTEL export AecoLedger's Copilot adapter targets is absent here. This is a live question about the existing adapter (§12 Q6).

`session-state/<id>/events.jsonl`, one session (5,762 lines): `tool.execution_start/complete` 1450 each, `assistant.message` 852, `hook.start/end` 495 each, `assistant.turn_start/end` 284 each, `permission.requested/completed` 86 each, `subagent.started/completed` 52 each, `skill.invoked` 20, `session.start/compaction_start/compaction_complete/shutdown` 1 each.

- **`tool.execution_complete`** — `{success, error:{message,code}, toolTelemetry.metrics:{resultLength, commandTimeout}, parentToolCallId}`. **Failure rate by tool** across 35 sessions: `web_fetch` **61.2%** (112/183), `codebase-memory search_graph` 29.7%, `search_code` 28.3%, `create` 28.3%, `index_repository` 23.5%, `task` 4.4%, `apply_patch` 3.1%, `view` 2.6%, `rg` 1.2%.
- **Tool census** (35 files): `view` 5201, `powershell` 3504, `report_intent` 2167, `rg` 1346, `skill` 798, `glob` 540, `task` 486, `apply_patch` 381, `sql` 330, `edit` 239, `read_agent` 200, `web_fetch` 183, `grep` 129, `ask_user` 124. MCP: `codebase-memory-mcp` ~130, `github-mcp-server` ~25. **No serena, no sonarqube.**
- **`report_intent`** (2167 calls) — `{intent: "<gerund phrase>"}`, e.g. `"Exploring auth context"`, `"Writing design spec"`, `"Committing auth scaffold"`. A consistent phase label emitted alongside each tool batch. **Directly segments a session into phases**; non-monotonic intent sequences (cycling back to an earlier phase) are the best wandering/rework proxy found anywhere.
- **Per-session `session.db`** — `todos{id, title, description, status: pending|in_progress|done|blocked, created_at, updated_at}` plus **`todo_deps{todo_id, depends_on}`** (a real dependency DAG). Not present in `session-store.db`. `inbox_entries` schema exists for cross-session messaging, unused.
  - **Sparse, and biased toward success — do not treat as a success/failure oracle.** Across all 34 `session.db` files: 16 have todo rows, **17 have the table but zero rows**, 1 has no table. Aggregate status distribution: `done` **224**, `in_progress` 9, `pending` 6, **`blocked` 4** (1.6%). The `blocked` state exists and is used, but abandoned work is far more likely left `pending` or deleted than marked `blocked`, so this **systematically under-reports failure** — the exact direction that matters for the core question. The `todo_deps` DAG is sparser still: **38 rows across only 5 of 34 sessions.**
- **`subagent.completed`** — **only about half the events are fully populated.** Three distinct key-sets across 462 events: the full `{agentDisplayName, agentName, model, totalTokens, totalToolCalls, durationMs, toolCallId}` on **215 (47%)**; `{agentDisplayName, agentName, model, toolCallId}` on 10; and a bare `{agentDisplayName, agentName, toolCallId}` on **237 (51%)** — no tokens, no duration, no model. Example of a full one: `general-purpose`, 26 tool calls, 670,521 tokens, 257,856 ms. **Subagent cost/duration on Copilot is therefore a ~47%-coverage signal, not a complete one**; the remainder must be reconstructed from `assistant_usage_events` (itself populated in only 5 of 37 sessions) or from the agent's own scoped events. **Every event carries a top-level `agentId`**, so per-agent file attribution still works for all agents even during overlap.
- **Topology**: overlap confirmed in **11 of 15** multi-subagent sessions. **Nesting is common** — 178 of 470 `subagent.started` events (**38%**) were spawned by a tool call whose caller was itself an active subagent, across 10 of 15 sessions; one chain verified end-to-end (a `general-purpose` subagent invoking `task` to spawn a `code-review` subagent that completed before the parent resumed).
  - ⚠️ **Methodological warning, recorded because it nearly became a finding:** an earlier pass concluded "zero nesting" from `agent_id == parent_tool_call_id` in all 806 `assistant_usage_events` rows. That query is **circular** — the schema *defines* `agent_id` as the spawning `toolCallId`, so the equality holds at any depth. Nesting is only visible by cross-referencing `subagent.started.toolCallId` against the caller `agentId` on the corresponding `tool.execution_start`. See §5.
- **Duplicate work, proven and recurring:** best case, session `c247afe9-…` — 6 code-review subagents concurrently active at peak; 4 of them each made 6–8 `view` calls over a union of **7 distinct files with 6 common to all four**, three pairwise comparisons at **Jaccard = 1.00** (identical file sets). A second instance (5 concurrent reviewers, 10 of 21 files shared by all) confirms this is a recurring pattern, not a cherry-picked example.
- **`permission.completed.result.kind`** — 982 approved / 41 location / 4 session / **4 denied-interactively-by-user**, with full command text and `intention`.
- **`hook.end.success: false`** — **35 hook failures across sessions** (e.g. a `sessionStart` PowerShell `ParserError`). Currently invisible tax.
- **`session-store.db`**: `sessions`(37) `{cwd, repository, branch}`; `turns`(347) with full text; `checkpoints`(5) with narrative `work_done`/`next_steps`.
  - **`assistant_usage_events`** — 1135 rows, and a **richer schema than first documented**: `{session_id, turn_index, agent_id, parent_tool_call_id, model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, reasoning_tokens, total_nano_aiu, request_multiplier, duration_ms, time_to_first_token_ms, inter_token_latency_ms, initiator, api_endpoint, reasoning_effort, finish_reason, content_filter_triggered, token_details_json, created_at}`. The best per-request record in any of the three tools — **but it covers only 5 distinct sessions of 37 (13.5%)**. High fidelity, very low coverage; row counts drift because the DB is live.
- **`rewind-snapshots/index.json`** — per-user-turn `{gitCommit, gitBranch, userMessage, backupHashes, fileCount}`. Ties turns to exact commits.

**Compaction — partial only.** `compaction_start{systemTokens, conversationTokens, toolDefinitionsTokens}` → `compaction_complete{checkpointNumber, checkpointPath, compactionTokensUsed{input,output,cachedInput}, preCompactionMessagesLength, preCompactionTokens, requestId, success, summaryContent}`. Real example: 217,889 tokens before, 94.5s duration. **There is no `postCompactionTokens` field** — `compactionTokensUsed` is the cost of the summarization call, not the resulting context size. **Compaction delta is not directly measurable on any of the three tools.**

**Absent:** structured HTTP status codes or retry counters; explicit aborted-turn event; subagent pass/fail flag; any resume/rewind event (`restart/` is empty, no resume field on `sessions`); `session_refs` table (designed for issue/PR linkage) — **0 rows**; `forge_trajectory_events` (with `command`/`output`/`exit_code`) — 0 rows. `resultLength` is populated for the `view` tool only.

### 4.5 The universal gap: nothing records outcomes

**No source in any of the three tools records whether the work was correct, merged, reverted, or reviewed.** Every quality input must come from outside the logs:

| Quality input | Source |
|---|---|
| Tests pass/fail | parsing test-runner stdout (unstructured, three formats) — see §7.1 |
| PR merged / reverted | GitHub API |
| PR review comments | GitHub API |
| Sonar / Semgrep / Trivy findings | those tools' APIs (SonarQube MCP already configured in this repo) |
| Issue ↔ session linkage | git branch/commit correlation + GitHub API; **no log field exists** |

## 5. Provenance and Capability — a Core Domain Requirement

Every analytical fact must carry its epistemic status. Three levels:

| Level | Meaning | Example |
|---|---|---|
| **Observed** | read directly from a field the tool wrote | "tool failed" from Copilot `success: false` |
| **Derived** | computed deterministically from observed facts | "81K tokens before first test" from timestamps + token sums |
| **Inferred** | requires a heuristic, model, or judgment | "this subagent was useless"; "this prompt was a correction" |

**Critical design point: provenance is a property of the `(metric, source, version)` triple, not of the metric.** The same metric has different standing per tool:

- *permission denied* — **Observed** on Copilot (`result.kind` enum) · **Inferred** on Claude (exact-string match, 54 occurrences, no field) · **Absent** on Codex
- *tool failed* — **Observed** on Copilot and Claude · **Derived** on Codex (regex the result text)
- *test failed* — **Derived** on all three, by three different regexes

Storing provenance on the metric definition makes the matrix lie the moment a source is added. It must be stored **per fact**, alongside a confidence score and its evidence:

```json
{
  "task_id": "AECO-418",
  "correlation": {
    "level": "inferred",
    "confidence": 0.61,
    "evidence": ["opening prompt semantically matches issue 418"]
  }
}
```

versus a 0.97 fact evidenced by `["branch contains 418", "commit belongs to PR 432", "PR closes issue 418"]`. A dashboard that renders these identically will be trusted once and abandoned the first time a number is checked by hand.

### 5.1 Capability by version — not defensive polish

Every "not present" in this document is an observation about **one corpus at one CLI version**, not a format guarantee. The Claude compaction finding illustrates the risk precisely: zero markers were found and independently re-verified, but that must be recorded as *not observed in the surveyed corpus/version*, never as *"Claude does not record compaction."*

Model this as capability observations, in the same store as provenance:

```
provider   claude
version    <cli version>
feature    compaction_boundary
observed   false
fields     {trigger: no, pre_tokens: no, post_tokens: no}
first_seen / last_checked
```

Consequences for ingestion:

1. **Never discard unknown JSON.** The RAW layer preserves the original event verbatim (provider, version, file, offset). AecoLedger's Codex parser already does the right thing by keeping `payload` as an untyped `JsonElement` — that instinct becomes the rule.
2. **Parsers are version-ranged**, not singular: `ClaudeAdapter → parser v2.0.x | v2.1.0–2.1.50 | v2.1.51+ | unknown-field preservation`.
3. **RAW is immutable and re-derivable.** When a needed field is discovered later, reprocess rather than re-collect. This is the property that makes the system survivable across vendor changes.

### 5.2 Two validated failure modes of this kind of analysis

Both were caught during the re-verification pass (§13) and both must be designed against, not merely remembered.

**1. Self-contamination — the analyzer pollutes its own corpus.** Most of Claude's failure signals are *strings, not fields*: the permission-rejection literal, `"[Request interrupted by user]"`, `<tool_use_error>`. Any session that *discusses* agent failures therefore adds spurious matches. Measured directly: a corpus-wide count of the rejection literal returned 63, of which **10 were generated by this discovery work itself** (6 in its own transcript, 4 in a subagent transcript) — a ~16% inflation on that metric, reconciling exactly with the pre-work count of 53–54.

*Requirement:* ingestion must exclude the Insights tool's own analysis sessions (by cwd, or an explicit session tag), and every string-derived signal must be marked **Inferred** with this fragility noted. This belongs in the ingestion contract, not a later dashboard filter.

**2. Circular queries that confirm the schema instead of the data.** An earlier pass concluded Copilot had "zero subagent nesting" because `agent_id == parent_tool_call_id` in all 806 `assistant_usage_events` rows. But the schema *defines* `agent_id` as the spawning `toolCallId` — the equality is a tautology at any depth. Independent cross-referencing found nesting in **38% of spawns**. The general failure: querying a derived/denormalized column and reading the result as evidence about the world.

*Requirement:* every **Observed** claim records the exact field path it came from, so a reviewer can ask "could this field be anything else?" This is a concrete reason provenance must be stored per fact rather than per metric.

### 5.3 Three-layer architecture

| Layer | Contents |
|---|---|
| **RAW** | provider event, original JSON verbatim, provider version, file + offset |
| **NORMALIZED** | session, turn, request, agent, tool_call, file_change, model_usage, workflow_phase |
| **ANALYTICS / INFERENCE** | task correlation, task type, correction, rework, tool leverage, agent contribution, workflow adherence, quality — every row carrying level + confidence + evidence |

## 6. Product Levels

| Level | Question | Status |
|---|---|---|
| **L1 Consumption** | Where did my tokens/money/time go? | **Done** — AecoLedger today |
| **L2 Execution** | What did the agents actually do? | Buildable from disk |
| **L3 Efficiency** | Where did they waste tokens/time/rework? | Buildable from disk |
| **L4 Effectiveness** | Did the work produce a good engineering outcome? | Requires GitHub + tests + scanners |

ccusage addresses L1. The local logs reach well into L2 and L3. Only L4 needs external integration. This replaces the earlier Phase 1–4 framing.

## 7. Feasibility of the 12 Core Analyses

Confidence = how much is derivable from data on disk today, without new instrumentation.

| # | Analysis | Claude | Codex | Copilot | Verdict |
|---|---|---|---|---|---|
| 1 | Cost per GitHub task | inferable | branch/SHA only | branch/SHA + per-turn commit | **Medium** — needs git↔GitHub correlation; no log links a session to an issue |
| 2 | Cost by model × task type | model ✓ | model ✓ | model ✓ | **Medium** — model is free; *task type* exists nowhere and must be classified |
| 3 | Workflow cost (Superpowers phases) | **High** (`attributionSkill`) | none | High (`report_intent` + `todos`) | **High** — best-supported item on the list |
| 4 | Subagent attribution | **High** | low (encrypted, no status) | **High** | **High** — except "did it contribute," which no log records |
| 5 | **Tool efficiency / utilization** *(renamed from "Tool ROI")* | High | medium (prose exit codes) | **Highest** | **High** — most immediately buildable |
| 6 | Context waste | **High** | High | High | **Highest** — cache-create vs cache-read is *already parsed today* |
| 7 | Rework rate | High | **Highest** (`unified_diff`) | medium | **Medium-High** — needs the test-outcome extractor (§7.1) |
| 8 | Human steering cost | High | High (`turn_aborted`) | medium | **High** |
| 9 | Failure tax | High | medium | **High** (+35 hook failures) | **High** — API retries invisible in all three |
| 10 | Quality per token | none | none | none | **Low** — the numerator; requires GitHub/Sonar/test integration |
| 11 | Compaction efficiency | not observed | not observed | partial (before-side only) | **Low** — delta not measurable anywhere |
| 12 | Memory effectiveness | low | none (DB empty) | none | **Lowest** — needs instrumentation, not log mining |

**Terminology note:** #5 is deliberately *not* called "ROI." Cost, failure rate, result size and downstream activity are observable; whether a call contributed to the outcome is not. "Return" implies a numerator that §4.5 shows does not exist.

### 7.1 The test-outcome extractor — a v1 critical path

Pass/fail is prose in all three tools, in three different conventions:

| Tool | Convention | Volume observed |
|---|---|---|
| Claude | stdout/stderr prose only — **no exit-code field on Bash results** (`exitCode` exists 1808× but only on hooks) | `dotnet test` 318, `dotnet build` 129, `npm test` 95, `vitest` 4 |
| Codex | machine-generated `"Exit code: N\nWall time: ..."` prefix inside the output string | `dotnet test` 17, `npm test` 21 |
| Copilot | trailing `<exited with exit code N>` in `result.content`; `success: true` means *the shell ran*, not that the command passed | `dotnet test` 592, `dotnet build` 111, `npm test` 109, `vitest` 55 |

This blocks **two of the six v1 signals** — tokens-before-first-verification and the rework loop — and both #22/#29. The other four v1 signals (duplicate subagent exploration, repeated file reads, failed tool calls, low-value tool results) do not depend on it; failed tool calls in particular is read directly from `is_error`, which is Observed and needs no parsing. See the authoritative v1 signal set (FR-21…FR-26) in `docs/product-superpowers/prds/2026-08-13-aecoledger-insights-v1.md` — **superseded here, and not in this repository:** that PRD belongs to AecoLedger Insights, a different product. Its FR-21…FR-26 are Insights' six waste detectors and are **unrelated to the identically numbered requirements** in `docs/product-superpowers/prds/2026-08-16-copilot-session-postmortem.md`, which are the Flight Recorder and rule extraction. It is small — a per-runner regex plus a per-tool exit-code convention — but it must be scoped in Phase 1, not deferred with the rest of L4. Its output is **Derived**, never Observed, and is version-fragile in all three tools.

## 8. Extended Opportunities (13–30) — Post-MVP

These were validated against the same corpus and are **not v1 scope**. They deepen existing analyses rather than forming a second backlog. Each is filed under its parent.

| Parent | Extension | Evidence / readiness |
|---|---|---|
| **#3 Workflow cost** | **25. Workflow conformance** — expected vs actual phase sequence | Strong: Copilot `todos` status enum + `todo_deps` DAG + `report_intent` (2167); Claude `Skill` (324) + `attributionSkill` + `TaskCreate`/`TaskUpdate`. **No phase-*end* marker anywhere** — boundaries inferred; the "expected" sequence must be authored, it isn't in the data |
| **#4 Subagent attribution** | **13. Agent topology efficiency** — fan-out, depth, parallelism, critical path | Best-supported extension. Full DAG available on all three. **Parallelism differs sharply by tool** — Claude 9 of 29 sessions overlap, Copilot 11 of 15, but Codex only 1 of 42 sibling pairs. Nesting: Copilot 38% of spawns, Claude 3 of 342, Codex none. Any "does fan-out buy wall-clock?" claim must be per-tool; an earlier cross-tool generalisation was refuted (§13) |
| | **24. Subagent duplication** — cross-agent file/tool overlap | **Strongest evidence in the document — measured, not projected**: 4 concurrent Copilot review agents covering 7 files with 6 common to all four, three pairwise comparisons at **Jaccard = 1.00**; a second session (5 reviewers, 10 of 21 files shared by all) confirms recurrence. `agentId` on every event makes attribution trivial. Caveat: deliberate redundancy (adversarial verification) is indistinguishable from waste without the agent's task description — available on Claude/Copilot, **encrypted on Codex** |
| **#5 Tool efficiency** | **15. Navigation efficiency** — discovery vs implementation split | **Blocked on sample size, not data.** Serena: 20 calls on Claude, 0 on Copilot/Codex. Also: tokens are per-message, not per-tool-call, so the split is an allocation heuristic |
| | **17. Tool-result leverage** — did the result influence what followed | Derivable via entity extraction + follow-through scoring 0–3. **Inferred**; must ship flagged |
| | **26. MCP/tool reliability** — failure/timeout/empty rates | Ready now: Copilot per-tool failure rates already computed (`web_fetch` **61.2%**, cbm `search_graph` 29.7%) |
| | **27. Context source mix** | Census works on all three; two of five sources to compare have near-zero volume |
| **#6 Context waste** | **16. Context amplification** — context in ÷ useful output | Denominator needs the edit→commit correlation from #23/#10 |
| **#7 Rework rate** | **23. Code churn** — lines written ÷ lines surviving | Claude backups verified diffable (47-line diff reconstructed); Codex `unified_diff` inline; Copilot has hashes but no diff bodies |
| | **21. Stuck-loop / thrashing detection** | Sequence data complete on all three. Needs **error fingerprinting from unstructured text** — no error code/class field anywhere |
| **#8 Human steering** | **14. Prompt efficiency** — tokens per instruction, prompt classified | Turn segmentation solid (Codex `task_started`/`task_complete` cleanest). **Prompt intent class recorded nowhere** |
| | **20. Permission friction** | Copilot Observed, Claude Inferred (54 strings), Codex effectively absent |
| | **19. Session fragmentation** | Claude `away_summary` (92) is a real resume marker; Copilot/Codex have none — clustering via cwd+branch+time is heuristic |
| **#9 Failure tax** | **22. Verification discipline** / **29. Time-to-first-verification** | Blocked on §7.1 |
| | **28. Time-to-first-useful-change** | Timestamps ready everywhere; "useful/relevant" needs a path-classification definition |
| **#2 Task type** | **18. Model escalation** | **The phenomenon barely exists in this corpus**: Claude 2/157 sessions multi-model and `effort` invariant at `"high"` across ~28,068 messages; Codex 0/25; Copilot only via subagents. Reframe as spawn-time model *selection policy* |
| **#30** | **Task execution entropy** | Entirely a derived construct. Copilot `report_intent` is the best proxy; Codex weakest (`reasoning` 100% encrypted) |

## 9. How Far Are We? — Gap Analysis

AecoLedger is feature-complete as a ccusage port: 3 adapters, offline pricing engine, date/session bucketing, aggregation, 5 API endpoints, React frontend, parity harness.

| Component | State | Reuse |
|---|---|---|
| Pricing engine (offline, fuzzy match, tiering) | done, parity-tested | **100%** — the entire cost term, solved |
| Date bucketing / timezone | done | **100%** |
| Session blocks (5h window) | done | **100%** |
| Path resolution & file discovery | done | **~90%** — Copilot needs a second resolver (`session-state/*/events.jsonl` + `session.db`); `logs/otel/` is absent here |
| API + React shell + test harness | done | **~60%** — scaffolding yes, all 5 views no |
| Aggregation | done | **~70%** — sums generalize, grouping keys don't |
| JSONL parsers | done | **~40%** — streaming and error-tolerance reusable; **the filtering is inverted** |
| **Domain model** | **blocking** | **~5%** |
| **Identity / execution graph** | not started | 0% |
| **Provenance + capability store** | not started | 0% |
| **Test-outcome extractor** | not started | 0% — v1 critical path |
| **Outcome truth layer** | not started | 0% |

**Estimate: ~25–30% of the eventual system exists** — specifically the least glamorous, most tedious 25%: offline pricing accuracy across three log formats plus a parity harness proving the numbers are right.

**The architecture conclusion that should drive the next stage:** this is not "add fields to `UsageRecord`." The ingestion contract must change from *billable records* to a *full event stream*, with `UsageRecord` becoming one projection among many. Concretely, the adapters today read and then discard what's needed — Claude's parser reads `isSidechain` only as a dedup tiebreak; `CodexSessionMeta` fully parses `ParentThreadId`/`Depth`/`AgentPath`/`Originator` and then maps every one away (its own `CLAUDE.md` records this as deliberate: *"do not invent a `UsageRecord` field for it"*); all three skip tool calls, errors, aborts and permissions entirely because those lines carry no tokens.

## 10. Recommendation

**Pursue as a new capability layer inside AecoLedger. v1 scoped explicitly to JTBD-A:** *"show me what happened in this agent run and where it wasted resources."*

### Sequencing

```
AecoLedger today (L1)
    ↓
Full Event Ingestion          ← contract inversion; RAW layer; test-outcome extractor
    ↓
Identity / Execution Graph    ← session → turn → tool call → subagent → file change
    ↓
Run Explorer                  ← L2; the v1 product; VALIDATION GATE
    ↓
Waste Analytics               ← L3; analyses #5, #6, #8, #9
    ↓
Workflow + Subagent Analytics ← #3, #4, then extensions 13/24/25
    ↓
GitHub Outcome Truth          ← L4; #1, #7, #10
    ↓
Comparative Optimization / A-B Testing  ← JTBD-B; #15 lives here
```

### 10.1 The v1 screen

Not another cost dashboard. A single run view:

```
RUN / TASK
Session(s) · Model(s) · Tokens · Est. cost · Duration

EXECUTION
User prompt
 ├─ Skill: superpowers:TDD
 ├─ codebase-memory
 ├─ Subagent A — 112K tokens, 17 tools, 2 failures
 ├─ Edit → Test ❌ → Edit → Test ❌ → Edit → Test ✅

WASTE SIGNALS
⚠ same file read 7 times
⚠ same failure seen 3 times
⚠ 81K tokens before first test
⚠ subagents A/B explored 62% same files
⚠ 143K tokens spent after first failed approach
```

**The v1 unit is the SESSION. Task-clustering is a designed-for later stage, not a v1 feature.** (Stakeholder decision, 2026-08-13.) A session is a defensible unit for a post-mortem screen, and Claude sessions survive gaps intact — resume appends to the same file, with `away_summary` (94 occurrences) marking the resume points. But the schema must not *assume* session-is-the-unit: model the run as a `Session` belonging to an optional `Task`, nullable in v1 and populated later by the clustering layer (git branch + commit + issue correlation, per §7 #1). Retrofitting a task above a hardcoded session root is the expensive version of this change; leaving the foreign key null is nearly free.

**Build it on Claude first, Copilot second.** Not because Claude's data is richer — Copilot's is — but because validation requires runs the stakeholder *remembers*, and this repo's 157 sessions and 342 subagent transcripts are Claude. Cross-source in the first screen multiplies the asymmetry problem before the single-source case is proven. Copilot second doubles as the test that the schema generalizes rather than encoding Claude's quirks.

#### Per-source scope — three different screens, not one screen degraded

| Screen element | Claude | Copilot | Codex |
|---|---|---|---|
| Session identity / git branch | ✅ | ✅ `sessions{cwd, repository, branch}` | ✅ `session_meta.git` |
| **Cost** | ✅ | ⚠️ **open — OTEL absent; usage table covers 5 of 37 sessions** | ✅ |
| Duration / timing | ✅ derived | ✅ `duration_ms`, `time_to_first_token_ms` | ✅ **best** (`task_complete` carries both) |
| Turn boundaries | ✅ derived | ✅ `turn_start/end` | ✅ **best** (51 = 46 + 5, airtight) |
| Main vs subagent split | ✅ | ✅ `initiator`, `agentId` per event | ✅ thread graph |
| Advisor/aux split | ✅ already parsed | ❌ | ❌ |
| **Skill / workflow spine** | ✅ 9,248 attributions | ✅ `skill.invoked` + **`report_intent`** ×2167 | ❌ **no skill event exists** |
| Subagent branch | ✅ own transcripts | ✅ but tokens on only 47% | ⚠️ **prompts Fernet-encrypted** |
| Edits with diffs | ✅ backups (94% ≤2 versions) | ⚠️ hashes, no diff bodies | ✅ **best** (`unified_diff` inline) |
| Test PASS/FAIL | ⚠️ stdout prose, no exit code | ⚠️ `<exited with exit code N>` | ⚠️ `"Exit code: N"` header |
| Resume-gap marker | ✅ `away_summary` | ❌ | ❌ |

Waste signals by source: *duplicate subagent exploration* is strongest on Copilot (where it was proven) and near-pointless on Codex (1 of 42 sibling pairs overlaps, and encrypted prompts hide whether overlap was deliberate); *failed tool calls* strongest on Copilot (`success` + `error.code`); *tokens before first verification* strong on all three; *empty results* weak on Copilot and Codex alike (`resultLength` covers `view` only; string length elsewhere).

**Consequences for scoping:**

- **Copilot is a genuine second target, not a nice-to-have.** It is the only tool that can render a *phase timeline* — 2,167 `report_intent` labels forming a readable arc ("Exploring auth context" → "Writing design spec" → "Committing") — a section Claude cannot draw at all. Building against it early forces the schema to generalise. **Its blocker is cost, not execution data**, which is the inverse of the usual assumption and must be resolved as part of that work (§12 Q4).
- **Codex is not "cost-only" — that undersells it.** Scope it as **cost + timing + rework**: the cleanest turn boundaries of any tool plus full per-patch diffs. Explicitly *omit* the workflow and agent-topology sections rather than rendering them empty, since Codex has no skill events and its subagent intent is encrypted.

### 10.2 Validation gate (resolves discovery step 6)

Discovery step 6 was not completed — there is no baseline and n=1. The Run Explorer is the validation instrument. Acceptance criterion, to be met before Waste Analytics is funded:

> Run the explorer against ~10 sessions the stakeholder remembers. The waste signals should match recollection on the runs that felt wasteful, **and at least one signal should be a genuine surprise.**

If nothing surprises, the tool is confirming priors and JTBD-B should not be funded on its evidence.

> **SUPERSEDED 2026-08-13.** The PRD (`docs/product-superpowers/prds/2026-08-13-aecoledger-insights-v1.md` — **not in this repository; it belongs to AecoLedger Insights, per the note in §7.1**) revises this gate's placement: Waste Analytics (L3) ships inside v1 itself, as v1's Phase B — it is not a separately funded phase downstream of this gate. Gating "before Waste Analytics is funded" would therefore have gated a phase that v1 already contains, which is circular. The gate now governs the later comparative work instead — JTBD-B and the L4 outcome-truth layer — not L3. The text above is preserved as originally written for audit purposes; it no longer reflects the current plan.

### 10.3 Explicitly deferred

- Any single composite "Engineering Efficiency" score (§2.1)
- Analyses #10, #11, #12
- Extensions 13–30 (§8), including #15 until an A/B exists
- JTBD-B and JTBD-C

## 11. Key Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Numerator has no source** — quality unmeasurable from logs | Critical | Deferred out of v1 entirely; one proxy (PR merged without revert) when L4 starts |
| **Test-outcome extraction is version-fragile prose parsing** in all three tools | **High** | Scoped into Phase 1, marked Derived, per-tool conventions documented (§7.1) |
| **Cross-source asymmetry** makes comparisons misleading | High | Per-fact provenance (§5); Claude-only v1; never average across sources silently |
| **Undocumented, unstable formats** — three vendors, no contracts | High | RAW layer preserves unknown JSON; version-ranged parsers; capability registry (§5.1) |
| **Inference presented as fact** | High | Level + confidence + evidence on every analytical row (§5) |
| **#15 has no sample** (Serena n=20) | Medium | Deferred behind a deliberate A/B, not an observational study |
| **Codex is structurally weakest** (encrypted prompts/reasoning, no completion status, empty DBs) | Medium | Cost-only in v1 |
| **n=1 validation** | Medium | Addressed by §10.2's surprise criterion; still blocks any external product ambition |

## 12. Open Questions

1. **Numerator definition** (when L4 starts): PR merged without revert, tests passing, or manual verdict per run?
2. ~~**Run/task boundary for v1.**~~ **RESOLVED 2026-08-13** — session is the v1 unit; `Task` modelled from the start as a nullable parent, populated by a later clustering layer. See §10.1.
3. ~~**History depth.** How far back do logs go, and is retention sufficient for later longitudinal claims? Codex's `logs_2.sqlite` already rotated ~104k rows away.~~ **RESOLVED 2026-08-13** — measured directly: on-disk spans are Claude ~31 days, Codex ~17 days, Copilot 99–111 days depending on layer, with confirmed DB-layer rotation on Codex and Copilot; not sufficient for longitudinal claims without AecoLedger's own durable ingestion. See §13.
4. **Copilot cost source — now a scoping blocker, not a curiosity.** `logs/otel/` does not exist on this machine (verified twice), and `assistant_usage_events` covers only 5 of 37 sessions. So the Copilot header row has no reliable token/cost figure, even though its *execution* data is the richest of the three. Two questions: is the existing `CopilotUsageSource` adapter pointed at a path that is never populated on this machine? And is there a third token source (per-session `session.db`, or enabling the OTEL exporter via `COPILOT_OTEL_FILE_EXPORTER_PATH`) that would give full coverage? Must be answered before Copilot becomes the second target.
5. **Product boundary.** Extend AecoLedger in place, or start `AecoLedger.Insights` as a sibling consuming the same adapters?
6. **Waste-signal thresholds.** "Same file read 7 times" needs a number. Derive from corpus percentiles, or hand-set and tune during §10.2 validation?

## 13. Verification Log

All load-bearing claims were re-derived independently after the initial surveys — by direct query for the countable ones, and by an adversarial pass instructed to *refute* the topology claims. Two claims failed.

### Confirmed exactly (independent re-query)

| Claim | Measured |
|---|---|
| Codex `task_complete` has no status/success field | 46 records, **exactly one key-set**, 7 keys: `completed_at, duration_ms, last_agent_message, started_at, time_to_first_token_ms, turn_id, type` |
| Codex `reasoning` fully encrypted | 414 total, **414 encrypted, 0 with non-empty summary** |
| Codex turn accounting is airtight | `task_started` 51 = `task_complete` 46 + `turn_aborted` 5 |
| Copilot has no `postCompactionTokens` | `compaction_complete.data` = 8 keys, none post-side; string absent from the file |
| Copilot OTEL export absent on this machine | **no `logs/otel` directory**; `logs/` holds 4 process crash logs |
| Copilot `todos` status enum | `CHECK(status IN ('pending','in_progress','done','blocked'))` |
| Claude `effort` is invariant | 27,799 occurrences, **all `"high"`** |
| Claude Serena usage is marginal | **20 calls exactly**, vs **724** codebase-memory calls |
| Claude subagent depth distribution | 342 transcripts, **3 at depth 2**, all with non-null `parentAgentId` |
| Copilot subagent overlap | **11 of 15** multi-agent sessions (session count differed by one from the first pass; numerator identical) |
| Copilot duplicate work | **strengthened** — 6 concurrent reviewers, Jaccard = 1.00 on three pairs, second instance found |
| Codex sequentiality | **1 of 42** sibling pairs overlaps, a full containment |

### Refuted

| Claim as originally stated | Correction |
|---|---|
| "Claude subagent fan-out is almost entirely sequential — one overlap corpus-wide" | **False.** 9 of 29 sessions (31%) contain overlapping pairs; dozens of pairs total. The narrower claim — that the 28/26/21-agent sessions are strictly sequential — does hold, but those sessions are not representative. |
| "Copilot has zero subagent nesting" | **False, and derived from a circular query** (§5.2). Real nesting in 178 of 470 spawns (38%), across 10 of 15 sessions, one chain verified end-to-end. |

**Consequence:** the cross-tool generalisation "fan-out multiplies tokens without buying wall-clock" is withdrawn. It is defensible for **Codex only**. On Claude and Copilot, parallelism is real and common, so topology efficiency (§8 #13) must be measured per tool and cannot be pre-judged.

### Second sweep — remaining claims (2026-08-13)

Confirmed:

| Claim | Measured |
|---|---|
| **Claude Bash results carry no exit code** *(v1 critical path)* | **0 lines contain both `toolUseResult` and `exitCode`.** `exitCode` lives only on hook attachments (`hookEvent`: SubagentStart 696, SessionStart 678, PreToolUse 656, PostToolUse 600) |
| Claude `attributionSkill` | **9,248 occurrences**, 12+ distinct skills well distributed — stronger than first assessed |
| Claude `permissionMode` distribution | acceptEdits 1160, default 933, auto 34, plan 5, bypassPermissions 4 |
| Claude `is_error: true` | 653 occurrences |
| Codex `patch_apply_end` | 43 success / 1 failure, exact |
| Codex MCP servers | **only `codebase-memory-mcp`**, 29 calls; per-tool split matches exactly |
| Codex model invariance | **0 of 25** threads have >1 model; only 2 distinct models corpus-wide |
| Codex root sessions have no resume/fork field | 11 root `session_meta` records, 2 key-sets, neither containing any parent/resume key (the 2 sets differ only by presence of `git` — confirming headless runs omit it) |
| Codex exit codes are regex-recoverable | 246 × `Exit code: 0`, 18 × `Exit code: 1` |
| Copilot per-tool failure rates | **exact match**: `web_fetch` 112/183 = **61.2%**, `task` 21/482 = 4.4%, `apply_patch` 12/381 = 3.1%, `view` 136/5201 = 2.6%, `rg` 16/1346 = 1.2% |
| Copilot `permission.completed.result.kind` | **exact**: approved 982, approved-for-location 41, denied-interactively-by-user 4, approved-for-session 4 |
| Copilot hook failures | **35 false / 2992 true** (1.2%) |
| Copilot `report_intent` | 2167 calls, args `{intent: "Locating EF projects"}` |
| Copilot `resultLength` covers `view` only | 1480 populated, 9 zero-length; no other tool |
| Copilot unused tables | `session_refs` 0, `forge_trajectory_events` 0, `dynamic_context_items` 0, `forge_skill_proposals` 0, `session_files` 0 |

Corrected by this sweep:

| Was | Now |
|---|---|
| Claude "`isApiErrorMessage` in 30 files" | **7 occurrences of `true`** corpus-wide — the 30 counted files containing the field, usually `false`. API/rate-limit errors are rare; #9's rate-limit component shrinks accordingly |
| Copilot `subagent.completed` is a complete `{model, tokens, toolCalls, duration}` record | **Only 47% are** (215 of 462); 51% are bare `{agentName, agentDisplayName, toolCallId}` |
| Codex every patch change is diffable | **8 of 63 change entries lack `unified_diff`** (~13%) |

Additional supporting evidence: Copilot's `powershell` tool reports `success: false` on only **5 of 3501** calls (0.1%) — direct confirmation that its `success` flag means "the shell ran", not "the command passed", exactly as §7.1 states.

### Third sweep — closing the remainder (2026-08-13)

| Claim | Result |
|---|---|
| Claude `file-history` backups are readable and diffable | **Confirmed** — 99 session dirs; a 31-line unified diff reconstructed from adjacent versions. **New limit: 94% of 731 tracked files have ≤2 versions** (histogram 1:341, 2:343, 3:35, 4:6, 5–8:6) — churn is measurable but the high-churn sample is ~47 files |
| Claude subagent result delivery is provable | **Confirmed** — 2,782 `task-notification` blocks in parent transcripts; **341 of 342** `.meta.json` carry `toolUseId` (99.7% linkage) |
| Claude multi-model sessions | **Confirmed but downgraded** — 2 of 158, and **one is this discovery session** (a `/model` switch made mid-analysis). Genuine pre-existing count: **1 of 157** |
| Codex `goals_1` / `memories_1` / `logs_2` are empty | **Confirmed exactly** — `thread_goals` 0, `thread_goal_continuation_deferrals` 0, `stage1_outputs` 0, `jobs` 0, `logs` 0. `state_5`: `threads` 25, `thread_spawn_edges` 14 |
| Copilot `assistant_usage_events` coverage | **Confirmed** — 5 distinct sessions of 37 (13.5%); schema is richer than documented (see §4.4) |
| Copilot `todo_deps` DAG | **Confirmed but sparse** — 38 rows across only 5 of 34 sessions |
| Copilot `rewind-snapshots` present | **Confirmed** — 30 `index.json` files; per-entry key structure not re-verified in detail |

### Retention measurement (2026-08-13)

Measured directly, read-only: every top-level Claude session file, every Codex rollout file, and Copilot's `session-state/*/events.jsonl` were scanned for their earliest/latest `timestamp` field; `session-store.db` and `logs_2.sqlite` were copied to a scratch directory before being queried.

| Tool | Source | Span (earliest → latest) | Days | Count | Rotation evidence |
|---|---|---|---|---|---|
| Claude Code | `projects/*/*.jsonl` (top-level only) | 2026-07-13 → 2026-08-13 | 31 | 158 files (Jul 107, Aug 51); last 7d 39, last 30d 157, last 60/90d 158 | none observed — no DB layer to check, and the file-count matches every session already inventoried in §4.2 |
| Codex | `sessions/**/rollout-*.jsonl` | 2026-07-23 → 2026-08-09 | 17 | 25 files (Jul 19, Aug 6); last 7d 6, last 30/60/90d 25 | **confirmed** — `logs_2.sqlite` `logs` table has 0 rows but `sqlite_sequence` shows 104,493, i.e. ~104k rows were written and later purged |
| Copilot | `session-state/*/events.jsonl` | 2026-04-20 → 2026-08-09 | 111 | 35 files (of 47 session-state dirs on disk) | 49-day gap in event dates, 2026-05-31 → 2026-07-19 — indistinguishable from either non-use or file-level pruning |
| Copilot | `session-store.db` `sessions` table | 2026-05-02 → 2026-08-09 | 99 | 37 rows | **confirmed** — the 4 earliest session-state dirs (events dated 2026-04-20 to 2026-04-29) have no matching row in `sessions`; the DB layer has already discarded what the filesystem still retains. (An earlier read of this same live table returned 40 rows; `session-store.db` is written to during normal use, so its row count is expected to drift between reads, not a measurement error.) |

Claude's ~31-day span is the figure that would naturally get quoted as "how far back logs go," but nothing in the corpus distinguishes a vendor-imposed retention window from this simply being the extent of this machine's Claude Code usage — that ambiguity cannot be resolved from the data and must not be reported as policy. Codex and Copilot, by contrast, show direct and unambiguous rotation at the DB layer: an exhausted autoincrement counter over an empty table, and session rows missing for directories whose flat files still exist on disk. Because every one of the three tools already discards, or is actively discarding, events a longitudinal analysis (JTBD-B) would need, a durable RAW copy captured by AecoLedger's own ingestion is the only way that analysis ever becomes possible — every day that passes without ingestion is history no vendor log will hand back.

### Session-size measurement (2026-08-13)

Measured directly, read-only, over the full corpus at `C:\Users\david\.claude\projects\` (all 17 project directories, not just AecoLedger). A "session" is defined as a top-level transcript `<project>\<sessionId>.jsonl` plus all of its subagent transcripts `<project>\<sessionId>\subagents\agent-<id>.jsonl`. **158 sessions found** (matches the corpus inventory in §4.2/§10.1; the 158th is this discovery work's own session, per the self-contamination note already on record there). Tool calls were counted as raw occurrences of the literal `"type":"tool_use"` per line, not lines — this matters because of the finding below. User/assistant turns were counted by top-level `type` field (`"user"` / `"assistant"`), the same convention as the entry-type census in §4.2.

| Metric | n | mean | median | p75 | p90 | p95 | p99 | max | max session |
|---|---|---|---|---|---|---|---|---|---|
| Tool calls / session | 158 | 106.8 | 34.5 | 108.2 | 351.6 | 469.3 | 706.0 | 774 | `F--git-UpFront` / `dbe4e1f8-369f-4daf-9e59-f114ccd8faed` |
| Total lines / session | 158 | 437.2 | 183.0 | 478.0 | 1367.2 | 1791.6 | 2457.8 | 2899 | `F--git-AecoLedger` / `f30f43c5-bb7c-41e7-99e2-78960118b1c4` |
| Renderable rows / session | 158 | 430.4 | 143.5 | 430.8 | 1494.0 | 1911.2 | 2787.4 | 3224 | `F--git-AecoLedger` / `f30f43c5-bb7c-41e7-99e2-78960118b1c4` |

(Percentiles computed by linear interpolation over the sorted 158-value array — the standard convention, e.g. `numpy.percentile`'s default.)

**Tool-call distribution buckets:**

| Bucket | Sessions | % |
|---|---|---|
| 0–50 | 87 | 55.1% |
| 51–100 | 29 | 18.4% |
| 101–250 | 16 | 10.1% |
| 251–500 | 19 | 12.0% |
| 501–1000 | 7 | 4.4% |
| 1001–2500 | 0 | 0.0% |
| 2500+ | 0 | 0.0% |

**Subagents per session:** mean **2.24**, max **28** (same `F--git-UpFront` session that also holds the tool-call max — its fan-out and its tool volume are the same outlier). **124 of 158 sessions (78.5%) have zero subagents.** 29 (18.4%) have ≥2; the remaining 5 (3.2%) have exactly 1.

**Rows-vs-lines — the apparent contradiction, resolved against the data, not assumed.** Renderable rows exceeded total lines in 25 of 158 sessions and were below it in 133; the largest single case is the `F--git-AecoLedger` session above (3224 rows vs 2899 lines, +325). Before writing this up, the corpus was checked for the presumed mechanism — a single assistant line carrying several `tool_use` blocks in one content array — because that would be the intuitive explanation. **It is not what the data shows: 0 of 16,878 lines corpus-wide containing at least one `"type":"tool_use"` occurrence contain more than one.** Every tool call in this corpus sits on its own JSONL line.

The actual mechanism, verified exactly on the 325-row-excess session: `renderable_rows − total_lines = tool_calls + subagent_count − other_lines`, where `other_lines` is every line whose top-level `type` is neither `user` nor `assistant` (`mode`, `system`, `attachment`, `file-history-snapshot`, `file-history-delta`, `queue-operation`, `ai-title`, `last-prompt`). For that session: `751 + 13 − 439 = 325`, exact. Two things are true at once and both are real:
1. **Renderable rows double-count tool calls by design.** Each `tool_use` block is rendered as its own row *in addition to* the assistant-turn row that carries it (the assistant's text and each of its tool calls are separate UI rows even though they are one JSONL line), and each subagent contributes one further synthetic summary row (a collapsible node) that corresponds to no line in any file.
2. **Total lines include rows a UI would never render.** Bookkeeping entry types — mode switches, system messages, attachment/hook events, file-history snapshots and deltas, queue operations, `ai-title`, `last-prompt` — are counted in `total_lines` but excluded from `renderable_rows` entirely.

When a session's tool-call-plus-subagent volume outweighs its bookkeeping-entry volume, rows exceed lines; when bookkeeping dominates (many mode/system/hook entries relative to few tool calls), rows fall below lines, which is the more common case (133 of 158). Rows exceeding lines is therefore not a bug in either count — it is the expected consequence of counting different things, now traced to an exact arithmetic identity rather than an assumed one.

**Consequences:**

- **The median session is small.** The median session has **34.5 tool calls** — under the smallest distribution bucket's midpoint. Only the top decile is large (p90 = 351.6, p95 = 469.3, max = 774). Any claim that sessions routinely carry thousands of tool calls is false; no session in this 158-session corpus reaches even the 1001–2500 bucket.
- **Most sessions have no subagents at all.** 124 of 158 (**78.5%**) have zero subagent transcripts. Any signal that depends on comparing or attributing across multiple agents — subagent duplication (#24), agent topology efficiency (#13), cross-agent file overlap — is therefore inapplicable to the majority of sessions by construction, not by a data gap; it only has something to measure in the 21.5% of sessions that spawn at least one subagent.

### Known measurement caveats

- **Timestamp proxy** for agent execution spans judged valid on all three tools (overlaps measured in minutes; Codex sibling handoffs monotonic at second scale, inconsistent with lazy-write artifacts). Recorded because the whole topology analysis rests on it.
- **Self-contamination** is live and quantified — see §5.2.
- Two independent counts of Claude `spawnDepth: 1` differed by one (338 vs 339), both immaterial, from differing file-glob scope. Noted so the numbers aren't treated as more precise than they are. Two independent counts of `away_summary` also differed (94 vs 92); resolved in favour of **94**, the later, directly re-verified count — the document uses 94 throughout.
- **Command counts in §7.1 are field-scoped, not text-scoped, and that distinction is large.** A naive corpus-wide text search for `dotnet test` returns **5,063** on Claude; the field-scoped count (Bash `command` only) is **318**. The gap is prose — plan documents, transcripts quoting commands, subagent reports, and this discovery work itself. §7.1 uses the field-scoped figures. Any future metric built on text matching rather than field extraction will be inflated by roughly this ratio, which is a concrete instance of §5.2's contamination problem.
- **One verification query in this sweep was itself faulty** and is recorded rather than silently dropped: a re-check of Codex's `wait_agent` `timed_out: true` count (originally reported as 84) used broken string normalisation and returned 0. The original figure is therefore **neither confirmed nor refuted** — treat it as unverified until re-measured with a proper JSON walk.

---

## Appendix — Discovery Checklist Coverage

| Step | Status |
|---|---|
| 1. Explore current context | Done — repo `CLAUDE.md`s, plan doc, API/frontend state, open issues |
| 2. Define desired outcome | Done — §2, incl. formula critique and the no-composite-score decision |
| 3. Map JTBD | Done — §3, three distinct jobs; v1 scoped to A |
| 4. User research | **Partial** — no interviews; evidence is behavioural (real log corpus, two survey rounds) + stakeholder's stated pain. n=1 |
| 5. Opportunity assessment | Done — §9, §10 |
| 6. Validate the problem | **Deferred with a plan** — §10.2 makes the Run Explorer the validation instrument with an explicit acceptance criterion |
| 7. Present findings | This document |
| 8. Save discovery doc | This file |
| 9. User review gate | **Approved (conditional)** — v1 scoped to JTBD-A |
