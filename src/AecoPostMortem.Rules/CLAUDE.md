# AecoPostMortem.Rules

Rule-set extraction and the check-shape catalogue: `<custom_instruction>` extraction, rule-set
versioning, tool-vocabulary and role derivation, operand resolution, the check shapes.

## Structure

| File | What it holds |
|---|---|
| `ToolInvocationShape.cs` | one observed tool call reduced to its argument shape — booleans for path, pattern, replacement, file text, command, and whether it spawned an agent. `ToolName` is carried as an opaque label; nothing reads it as meaning |
| `ToolVocabulary.cs` | `ToolVocabulary.Build` — the distinct tool names in whatever corpus is passed in (FR-29) |
| `ToolRole.cs` | `ToolRole` — the closed five-member enum (`FileRead`, `Search`, `FileWrite`, `Shell`, `Spawn`); no sixth "unclassified" member, see below |
| `ToolRoleDeriver.cs` | `ToolRoleDeriver.Derive` — classifies each tool by its calls' argument shapes (FR-30); `ToolRoleCount`, `ToolRoleSummary` (with `DominantTool`), `ToolRoleDerivation` |
| `HookFailureCheck.cs` | FR-17's check shape: `SessionHookOutcome` (plain per-session input), `SessionCount` and `HookFailureCounts` (the paired-denominator result), `HookFailureCheck.Evaluate` |
| `RepeatedReadCheck.cs` | FR-15's check shape (issue #25): `ReadEvent` (a session and a path — generic, no tool name), `RepeatedReadOccurrence`, and `RepeatedReadCheck.Run`, which groups events per `(SessionId, Path)` and reports the groups at or above `Threshold` (4) |
| `FailedToolCallsCheck.cs` | FR-16 (S-14, issue #26): `ToolCallOutcome` (the plain per-call input), `FailureRate` and `ToolFailureRate` (the check-shape result), and the check itself |

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
through `AecoPostMortem.Data` and writing findings back. `ToolInvocationShape` is this project's own
plain-input record for a tool call: it does not reference `AecoPostMortem.Data.Execution.ToolCall`
(that would be a `ProjectReference` to `Data`, which the invariant forbids) — `Findings` reduces the
real `ToolCall` and its RAW payload into `ToolInvocationShape` values before calling in.

## Non-obvious decisions

### Tool vocabulary and roles are the load-bearing proof of the invariant

S-21 (issue #34, FR-29/FR-30) is the mechanism that makes Repo Rule 6 satisfiable for every check
that follows: a check that needs to know "which tool writes files" asks
`ToolRoleDeriver.Derive(...).Roles[ToolRole.FileWrite].DominantTool` instead of hardcoding a tool
name. `ToolVocabulary.Build` and `ToolRoleDeriver.Derive` never read `ToolInvocationShape.ToolName`
as anything but an opaque label to group and report back — classification is driven entirely by the
six argument-shape booleans the record carries.

### Roles are a closed five-member enum; "unclassified" is not a sixth member

`ToolRole` has exactly `FileRead`, `Search`, `FileWrite`, `Shell` and `Spawn` — the five FR-30 names.
A tool whose calls match none of the shapes is reported in
`ToolRoleDerivation.Unclassified` (a list of tool names) instead of being forced into one of the
five or into a sixth `ToolRole.Unclassified` value. Adding an "unclassified" role would make every
future `switch` over `ToolRole` need a default case that means "we guessed wrong" rather than one
that is unreachable; keeping it out of the enum makes a guess structurally impossible.

### Classification precedence: spawn, then write, then search, then read, then shell

A tool's calls can carry more than one signal at once — an edit tool typically has both a path and
replacement text. `ToolRoleDeriver.Classify` checks spawn first (the only structural, non-argument
signal: it comes from whether the call produced a subagent, not from its own arguments), then
writing (replacement or file text), then searching (a pattern), then reading (a path with neither of
the above), then shell (a command) last. This order is what makes "a tool taking a path but no
pattern is file-reading" and "a tool taking a pattern is searching" both true regardless of which
other flags a call also carries.

### Classification is per tool, not per call

`ToolRoleDeriver.Derive` groups invocations by `ToolName` first and classifies the group: if any call
of a tool carries a role's signal, the whole tool gets that role. A single tool's argument schema is
structurally fixed, so a call that omits an optional argument (e.g. a search invoked without an
explicit pattern) does not fracture that tool across two roles.

### Derivation is a pure function of its input, never cached

`ToolRoleDeriver.Derive` holds no state between calls — every call recomputes vocabulary and role
membership from the `IEnumerable<ToolInvocationShape>` passed in. The measured 61 distinct tools in
the reference corpus is a fact about that corpus, not a constant this project could bake in: the
next machine's log has a different vocabulary, and role derivation has to run again for it to mean
anything.

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

Tool vocabulary and role derivation (S-21, issue #34) has landed. The check-shape catalogue has
three entries: `HookFailureCheck` (issue #27, FR-17), `RepeatedReadCheck` (issue #25, FR-15) and
`FailedToolCallsCheck` (issue #26, FR-16). The shape they establish — plain per-call/per-session
input records in, structurally-required or structurally-paired results out, no branch on any
specific tool name — is the pattern later checks in this project should follow.
