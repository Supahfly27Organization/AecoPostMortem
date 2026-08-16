# Knowledge Tools

## Goal

Minimize token usage and file reads while maximizing repository understanding.

Always use the most targeted knowledge source available.

---

# Tool Priority

Default order:

1. Codebase Memory MCP
2. Direct file reads
3. Glob / Grep searches
4. Broad repository exploration

Never start by reading large numbers of files.

---

# Codebase Memory MCP

Primary repository knowledge source.

Use for:

* Finding classes
* Finding interfaces
* Finding methods
* Call chain analysis
* Dependency analysis
* Architecture discovery
* Impact analysis
* Refactoring preparation
* Feature implementation
* Bug investigation
* Similar implementation search

Preferred workflow:

1. search_graph
2. get_code_snippet
3. Read only files required for implementation

Windows note: `search_code` works normally. `search_graph` → `get_code_snippet` is the standard lookup chain. Use `trace_path` for dependency chains, `get_architecture` for module-level overview.

Examples:

* Find all implementations of a service
* Find who calls a method
* Find usages of an entity
* Understand module boundaries
* Understand dependency direction

Failure handling:

If Codebase Memory fails to locate a symbol, class, method, or dependency that is expected to exist:

1. Use Grep for text-based discovery.
2. Read files directly only when necessary.

Do not immediately start broad file exploration.

---

# CASS Memory (cass-memory)

Procedural/episodic memory — what past sessions learned, not code structure. Codebase Memory MCP (above) answers "what does the code look like"; CASS Memory answers "what have we learned about working in this codebase."

Automatic — no action needed:

* Session end: the `cm reflect --days 1` hook (`.claude/hooks.json`) extracts new rules from the session.

Not automatic — recall only happens when explicitly requested:

* Before starting non-trivial or unfamiliar work, call `cm context "<task>"` (or the equivalent MCP tool) to pull relevant rules/history — nothing is recalled at session start on its own.
* A correction or outcome happens mid-session — record it immediately (feedback/outcome) rather than waiting for the end-of-session reflect, so it isn't lost if the session is interrupted.

Inspecting what's been learned:

| Where | Shows |
|---|---|
| `~/.cass-memory/playbook.yaml` | The learned rules themselves, plain YAML — open directly |
| `cm ls` | All current rules/bullets |
| `cm stats` | Health dashboard: counts by scope/state/kind, score distribution, stale/at-risk rules |
| `cm top [n]` | Most effective rules |
| `cm why <bulletId>` | Origin/evidence for a specific rule |

This directory is global (`~/.cass-memory/`), shared across every project on the machine — not project-scoped like Codebase Memory MCP's index.

---

# Serena

Not a knowledge source — an editing tool for cross-file-safe changes, backed by a real language server (LSP).

Use only for:

* rename_symbol — renaming a symbol used across multiple files
* safe_delete_symbol — deleting a symbol only if nothing still references it
* replace_symbol_body — precise, signature-preserving body replacement
* get_diagnostics_for_file — compiler-level feedback without a full build

Do not use Serena for navigation/search — that's Codebase Memory MCP's job (see above). Never query both for the same lookup.

Do not call `initial_instructions` or load Serena's tools speculatively — only when the current task explicitly requires one of the four operations above. This applies per-subagent: a dispatched subagent should only touch Serena if its own assigned task needs it, not by default.

---

# Documentation, ADRs, and specs

Architecture docs, ADRs, product specs, and design decisions live under `docs/` as plain Markdown — use Glob/Grep to locate them and read directly. There is no separate knowledge-graph tool for this content.

---

# Tool Selection Matrix

| Task                    | Tool            |
| ----------------------- | --------------- |
| Find class              | Codebase Memory |
| Find interface          | Codebase Memory |
| Find method              | Codebase Memory |
| Find usages             | Codebase Memory |
| Find dependencies       | Codebase Memory |
| Impact analysis         | Codebase Memory |
| Bug investigation       | Codebase Memory |
| Refactoring             | Codebase Memory |
| Recall prior learnings / anti-patterns | CASS Memory |
| Rename across files     | Serena |
| Safe delete (check references first) | Serena |
| Precise symbol body replacement | Serena |
| Pre-build compiler diagnostics | Serena |
| Architecture document   | Glob/Grep + Read |
| ADR lookup              | Glob/Grep + Read |
| Product specification   | Glob/Grep + Read |
| SQL documentation        | Glob/Grep + Read |
| Business requirement    | Glob/Grep + Read |
| Design decision history | Glob/Grep + Read |

---

# Source of Truth

Priority order:

1. Running application behavior
2. Automated tests
3. Source code
4. Codebase Memory results
5. Documentation
6. Comments

When documentation conflicts with implementation, assume implementation is correct and highlight the discrepancy.

---

# Token Discipline

DO:

* Query Codebase Memory before reading files.
* Read only files required for the task.
* Read the smallest possible set of files.

DO NOT:

* Read entire modules without reason.
* Read every related file before starting.
* Perform broad repository exploration.
* Use Grep when Codebase Memory can answer the question.

The goal is to solve tasks with the minimum number of file reads and tool calls necessary.
