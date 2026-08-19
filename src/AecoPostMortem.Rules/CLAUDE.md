# AecoPostMortem.Rules

Rule-set extraction and the check-shape catalogue: `<custom_instruction>` extraction, rule-set
versioning, tool-vocabulary and role derivation, operand resolution, the check shapes.

## The invariant

**Nothing here may name a tool, an MCP server or a repository** (FR-34, PRD §3.1). This is the one
requirement the operator called non-negotiable, and it is structural rather than conventional so
that one project's source proves it.

**This project references nothing** — no package, no project and no assembly. Stated as an
allowlist of zero rather than a list of dependencies to reject, because a list of what to reject can
never be exhaustive: an earlier version named persistence packages by prefix and would have passed
`Npgsql`, which this repository already uses in `bench/`. That is what turns the invariant from an
assertion into a test: a project with no dependencies has a very small surface in which a tool name
could hide.
`The_rules_project_references_no_persistence_assembly` in `test/AecoPostMortem.Containment.Tests`
enforces it, so adding a reference here fails the build.

It takes plain inputs — rule statements as text, the discovered tool vocabulary as a list, call
counts as numbers — and returns results. `AecoPostMortem.Findings` does the orchestration, reading
through `AecoPostMortem.Data` and writing findings back.

## Structure

| File | What it holds |
|---|---|
| `HookFailureCheck.cs` | FR-17's check shape: `SessionHookOutcome` (plain per-session input), `SessionCount` and `HookFailureCounts` (the paired-denominator result), `HookFailureCheck.Evaluate` |

## Non-obvious decisions

### `HookFailureCounts` pairs both denominators structurally, not by convention

FR-17 requires a hook-failure figure to state the count over all sessions and the count over
sessions that made a tool call together, never one alone — the edge case that makes this matter is
a measured 34 of 35 sessions overall against 32 of the 33 that made a tool call: two sessions
failed the hook while making no tool call at all, so either figure printed by itself reads as a
contradiction. `HookFailureCounts.OverAllSessions` and `.OverSessionsWithToolCall` are both
`required SessionCount`, and `SessionCount.Count`/`.Population` are themselves both `required` —
an object initializer that omits any of the four is a compile error (CS9035), the same reasoning
`AecoPostMortem.Findings/CLAUDE.md` gives for `Finding.Provenance` being `required` rather than
validated at run time.
`HookFailureCheckTests.The_denominator_fields_are_required_members` proves the properties still
carry `RequiredMemberAttribute`.

## Status

`HookFailureCheck` (issue #27, FR-17) is the first check to land — the shape it establishes (plain
per-session inputs in, a structurally-paired result out) is the pattern later checks in this
project should follow.
