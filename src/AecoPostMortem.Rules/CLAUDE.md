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
| `RepeatedReadCheck.cs` | FR-15's check shape (issue #25): `ReadEvent` (a session and a path — generic, no tool name), `RepeatedReadOccurrence`, and `RepeatedReadCheck.Run`, which groups events per `(SessionId, Path)` and reports the groups at or above `Threshold` (4) |

## Non-obvious decisions

### `ReadEvent` names no tool, and never will

`ReadEvent` carries only `SessionId` and `Path`. Deciding which raw tool calls count as reads —
today a hardcoded `view` match, eventually S-21's role/vocabulary derivation — is
`AecoPostMortem.Findings`' job (`RepeatedFileReadFindingCheck.ReadEventsFrom`), by the invariant
above. When the role layer lands, only that mapping changes; `ReadEvent` and `RepeatedReadCheck`
do not, because they were never told what a "read" is in the first place.

### One threshold constant, not two conditions

Issue #25's acceptance criteria state the repeat threshold two ways — "four or more times" and
"more than three times". `RepeatedReadCheck.Threshold` is the one place that number lives, so the
two phrasings cannot drift apart by editing only one of them.

## Status

`RepeatedReadCheck` is the first check shape here. Two sibling Waste-class checks (failed tool
calls, issue #26; hook failures, issue #27) are landing concurrently in separate files — expect
this section to grow, not this file's shape to change.
