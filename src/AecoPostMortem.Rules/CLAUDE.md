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
| `FailedToolCallsCheck.cs` | FR-16 (S-14, issue #26): `ToolCallOutcome` (the plain per-call input), `FailureRate` and `ToolFailureRate` (the check-shape result), and the check itself |

## Non-obvious decisions

### A check's plain input never carries the entity that produced it

`ToolCallOutcome` (session id, tool identity, success) exists only because this project cannot see
`AecoPostMortem.Data.Execution.ToolCall` — it has no reference to `Data` at all. Every check-shape
input is a small record shaped like this one: the fields a check needs, resolved by the caller, and
nothing else. `AecoPostMortem.Findings` is the project that reads the real entity and narrows it to
the plain shape.

### A rate is structurally required, never a bare number

`FailureRate.Failures` and `FailureRate.Calls` are both `required`; `Percentage` is a computed,
setter-less property derived from the two. There is no constructor path that produces a percentage
without its counts — `FailedToolCallsCheckTests.The_percentage_is_computed_never_a_settable_member`
proves it by reflection, mirroring the reasoning `AecoPostMortem.Findings/CLAUDE.md` gives for
`Finding.Provenance` being `required`. `ToolFailureRate.SessionCount` is `required` alongside
`FailureRate` for the same reason: a tool called a handful of times must carry that context with
its rate, not as an optional afterthought (issue #26, Scenario 2).

### The check groups by whatever tool identity the operand carries

`FailedToolCallsCheck.Run` groups `ToolCallOutcome` by `ToolIdentity` with no case that names a
specific tool — Repo Rule 6 holds because there is nothing here for a name to hide in, and
`FailedToolCallsCheckTests.The_check_groups_by_whatever_tool_identity_the_operand_carries` exercises
deliberately unusual identities to prove the grouping is generic. The check returns a rate for
every tool observed, including ones with zero failures; deciding which rates are worth surfacing as
a finding is `AecoPostMortem.Findings`'s call, not this one's.

## Status

The check-shape catalogue has one entry: `FailedToolCallsCheck` (FR-16). The pattern it establishes
— a plain input record, a structurally-required rate, a check with no branch on any specific tool
name — is the template for the checks that land here next.
