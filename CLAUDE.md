# CLAUDE.md

## Working Rules
- Response style (conversational narration and status updates only — see exemptions below):
  - Lead with the result; no pleasantries, preamble, or restatement of the request.
  - Do not narrate routine tool calls (routine codebase-memory-mcp lookups, file reads, standard edits).
  - Do not repeat unchanged plans or previously reported findings — reference them instead of restating.
  - During implementation, report only important discoveries, blockers, or decisions.
  - Final response on a coding task: outcome, changed files, tests run, unresolved issues — nothing else.
  - Match length to the task, not a fixed cap. Always preserve technical caveats, commands, paths, numbers, and error messages, even if it lengthens the response.
  - Exempt from the above: PRDs, discovery docs, story lists, roadmaps, brainstorming option analysis, User Review Gate content, and any skill-defined output format (e.g. ReportFindings). Size these to the content, not to brevity.
- When implementing, write code directly — skip preamble.
- Do not re-read files already in context.
- Read only files directly needed for the current task; never explore the codebase broadly before starting — prefer querying codebase-memory-mcp over generic file search for navigation once this repo is indexed (see `docs/claude/KNOWLEDGE_TOOLS.md`).
- Only invoke Superpowers / Product Superpowers skills when explicitly named (slash command or direct request). Do not speculatively invoke skills based on topical relevance.
- Before invoking a plugin skill by name, confirm it's present in the current skill listing — if it's missing, stop and ask whether to enable it (`claude plugin enable <plugin>@<marketplace>` then `/clear`) rather than manually reproducing its process as a workaround.
- Knowledge/navigation/editing tool policy (Codebase Memory MCP, serena, file-read discipline): see `docs/claude/KNOWLEDGE_TOOLS.md`.
- Subagent model selection — always pass an explicit model param when dispatching; never omit it and rely on inheritance (it silently inherits the session's most expensive model):
  - When dispatching through `subagent-driven-development` (Superpowers) or `pm-autonomous-execution` (Product Superpowers), follow that skill's own Model Selection section — do not duplicate or drift from it.
  - For everything else (aeco's own `github-issue-*` skills, MCP-driven mechanical work, and standalone gates not normally dispatched like `brainstorming`/`writing-plans`/`product-discovery`/`writing-prd`/`pm-artifact-review`): cheap/fast model for mechanical, low-ambiguity tasks; mid-tier (the default) for bounded implementation/analysis work; most capable model for decisions later work depends on and can't easily undo (architecture, strategy, final review).
- Module documentation — keep each project/module's `CLAUDE.md` accurate as part of the same change that touches it (add, update, or delete content — it should never just accumulate):
  - Architecture: structure, entry points, key dependencies.
  - Non-obvious decisions: why something is built the way it is, invariants, gotchas — skip anything already obvious from reading the code.
  - Playbook (when the module has recurring multi-step procedures, e.g. "how to add an endpoint here"): a terse `## Playbook` section with the steps. This is module-scoped and separate from CASS Memory's auto-learned playbook (`docs/claude/KNOWLEDGE_TOOLS.md`) — don't conflate the two.

## What is AecoPostMortem?

Session Post-Mortem reads GitHub Copilot CLI’s own event logs and reports where a session diverged from the process it was given: which written rules were followed, which were ignored, where effort was wasted, and which capability was missing.

Tech: .NET 10, C#, EF Core over SQLite, xUnit; TypeScript, React, Vite in `web/`.

Layout (PRD §3.1): `src/AecoPostMortem.{Data,Ingestion,Rules,Findings,Api,Cli}/`, `test/` one
project per source project (+ Containment.Tests), `web/` the React app, `fixtures/` the frozen
corpus, `scripts/` the checkers.

## Task → Read These First

<!-- Fill this in as the codebase grows: map common tasks to the files/docs that should be
     read first. This is the highest-leverage section in this file — it's what turns a
     generic agent into one that knows this codebase's shape. -->

| Task | Read These |
|------|-----------|
| Add entity / model | `AecoPostMortem.Data` router → entity → DbContext → migration (RAW only; Repo Rule 4) |
| Add API endpoint | `AecoPostMortem.Api` router → endpoint + service interface |
| Add business logic | Relevant module's router → service interface + implementation |
| Add data access | `AecoPostMortem.Data` router → data-access interface + implementation |
| Add a rule check | `AecoPostMortem.Rules` router → check shape + operand resolution (name no tool — Repo Rule 6) |
| Frontend page | the `web` router → shared types → page file |
| Product intent, requirements, stories | `docs/product-superpowers/prds/2026-08-16-copilot-session-postmortem.md`, then the stories doc |
| Add new project / module | Create/update its `CLAUDE.md` (architecture + playbook, keep in sync — see Working Rules) |
| Add a CLI command | `Cli` router → `CommandSurface.Commands` |
| Parse `tool.execution_start.data.arguments` | `AecoPostMortem.Ingestion` CLAUDE.md → `ToolArguments.cs` (FR-4; object/string/unparsed, never coerced) |
| Change the solution's shape | `Containment.Tests` enforces it |
| Security / quality review | `docs/claude/SCANNING_TOOLS.md` |
| _(add project-specific rows here as modules are built out)_ | |

## Security & Quality Scanning

Three tools, each with a primary purpose:
- **SonarQube** — code quality, bugs, maintainability, coverage, technical debt
- **Semgrep** — source-code security patterns (injection, XSS, auth, secrets in code)
- **Trivy** — dependency CVEs, Docker images, IaC, secrets in config/repo files

For decision rules, overlap cases, scan order, and token discipline: `docs/claude/SCANNING_TOOLS.md`

## GitHub Integration

The `github` MCP server (`mcp__github__*`) is always configured — general GitHub operations (issues, PRs, review comments, code/issue search) are available regardless of which optional features were enabled during `aeco init`. If `.claude/skills/github-issue-{sync,start,commit}/` exist (only added when the GitHub Issue Workflow was enabled), use those dedicated skills for the tracked-issue workflow instead of hand-rolling the same steps.

## Repo Rules

1. Never read `src/AecoPostMortem.Data/Migrations/` unless the task is explicitly about migrations.
2. Preserve existing project names and namespaces when refactoring.
3. Frontend commands run from `web/`, not the repo root.
4. Only RAW carries a migration. NORMALIZED and FINDINGS are re-derived from RAW, never
   migrated — a migration against them is a defect (PRD §3.8).
5. RAW appends bypass EF Core change tracking: batched raw SQL, a measured 56,138 rows per
   full ingest (PRD §3.1).
6. Nothing in `src/AecoPostMortem.Rules/` may name a tool, MCP server or repository — the
   non-negotiable invariant, structural so one project's source proves it (FR-34).
7. No project references an AecoLedger assembly; no project reference resolves outside this
   repo (PRD §3.1).
8. `bench/bench.csproj` sits outside the solution; a root project breaks containment.
9. MSBuild settings live under `src/` and `test/`, not the repo root (reaches `bench/`).

## Local DB Defaults

One local SQLite file, owner-only, no server or account (FR-11). Migrations apply on first use, so
there is no database command to run.

| DB | Connection String |
|---|---|
| Store (SQLite) | `Data Source=<StoreLocation.Default>;Pooling=False` — `%LOCALAPPDATA%\AecoPostMortem\store.db` on Windows, `~/.local/share/AecoPostMortem/store.db` elsewhere; `purge` deletes it |

<!-- Add one row per database this project connects to locally, and note any env var
     used to override each connection string (e.g. `<NAME>_DB_CONNECTION`). -->

## Deeper Context (read as needed)

- `docs/claude/DOMAIN_MODEL.md` — all entity schemas and DB columns
- `docs/claude/NORMALIZED_MODEL.md` — the eight NORMALIZED entities and their invariants
- `docs/claude/PATTERNS.md` — coding conventions and architectural rules
- `docs/claude/SCANNING_TOOLS.md` — when to use SonarQube, Semgrep, and Trivy
- `docs/claude/KNOWLEDGE_TOOLS.md` — when and how to use Codebase Memory MCP, CASS Memory, and serena

