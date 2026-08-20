# AecoPostMortem.Rules

Rule-set extraction and the check-shape catalogue: `<custom_instruction>` extraction, rule-set
versioning, tool-vocabulary and role derivation, operand resolution, the check shapes.

## Structure

| File | What it holds |
|---|---|
| `ToolInvocationShape.cs` | one observed tool call reduced to its argument shape — booleans for path, pattern, replacement, file text, command, and whether it spawned an agent, plus `McpServerName` (the provider's own logged server field). `ToolName` and `McpServerName` are carried as opaque labels; nothing reads either as meaning |
| `ToolVocabulary.cs` | `ToolVocabulary.Build` — the distinct tool names in whatever corpus is passed in (FR-29) |
| `ToolRole.cs` | `ToolRole` — the closed five-member enum (`FileRead`, `Search`, `FileWrite`, `Shell`, `Spawn`); no sixth "unclassified" member, see below |
| `ToolRoleDeriver.cs` | `ToolRoleDeriver.Derive` — classifies each tool by its calls' argument shapes (FR-30); `ToolRoleCount`, `ToolRoleSummary` (with `DominantTool`), `ToolRoleDerivation` |
| `OperandResolver.cs` | FR-31/FR-32 (S-23, issue #37): `OperandResolutionLayer` (the four confidence layers), `ResolvedOperand`, `TwoOperandResolution`, and `OperandResolver.Resolve`/`ResolveTwoOperands` — a rule operand's text resolved to tool names, most-confident layer first, with two-operand subtraction (A winning ties) |
| `HookFailureCheck.cs` | FR-17's check shape: `SessionHookOutcome` (plain per-session input), `SessionCount` and `HookFailureCounts` (the paired-denominator result), `HookFailureCheck.Evaluate` |
| `RepeatedReadCheck.cs` | FR-15's check shape (issue #25): `ReadEvent` (a session and a path — generic, no tool name), `RepeatedReadOccurrence`, and `RepeatedReadCheck.Run`, which groups events per `(SessionId, Path)` and reports the groups at or above `Threshold` (4) |
| `FailedToolCallsCheck.cs` | FR-16 (S-14, issue #26): `ToolCallOutcome` (the plain per-call input), `FailureRate` and `ToolFailureRate` (the check-shape result), and the check itself |
| `InterruptionLoadCheck.cs` | FR-20's check shape (issue #30): `PermissionPromptOutcome` and `QuestionOutcome` (plain per-event inputs, no tool name), `InterruptionLoad` (the paired-count result — permission prompts and questions never summed), `InterruptionLoadCheck.Evaluate` |
| `RuleStatement.cs` | FR-26 (S-19, issue #32): `RuleStatement` (source file + verbatim text) and `InstructionBlock` (a block's source file plus the statements its list items yielded) |
| `RuleStatementExtractor.cs` | FR-26's `<custom_instruction>` parser: `RuleStatementExtractor.ExtractBlocks` takes a system prompt's own text and returns its blocks — pure, no file, no session |
| `RuleStatementDeduplication.cs` | `SessionInstructionBlocks` (one session's blocks, plus `HasInstructionBlocks`), `RuleStatementOccurrence` (a statement plus every session that carried it), and `RuleStatementDeduplication.Deduplicate`, which collapses identical statements across sessions |
| `AbortedTurnCheck.cs` | FR-18 (S-16, issue #28): `TurnRecord` (the plain per-turn input, aborted or not), `AbortedTurnOccurrence` (reason paired with its 1-based position and the session's own turn count), and `AbortedTurnCheck.Run`, which orders each session's turns and reports only the ones that aborted |
| `DeclaredIntent.cs` | FR-19's plain input (issue #29): one self-declared phase — `SessionId`, `Phase` (an opaque label) and `Sequence` (the corpus-wide chronological order, the only ordering input this project trusts) |
| `PhaseOrdering.cs` | `PhaseOrdering.Derive` — the distinct phases in the corpus, ordered by each phase's earliest `Sequence` across every session (FR-19; the S-21 vocabulary pattern applied to phase labels) |
| `PhaseChurnCheck.cs` | FR-19's check shape (issue #29): `PhaseChurnResult` (a session's returns, its own total intents, and the vocabulary/ordering that produced it), `PhaseChurnCheck.Run`, which derives the ordering once and evaluates each session independently |
| `RuleSetVersion.cs` | FR-27 (S-20, issue #33): `SessionRuleSet` (a session's repository, start time and blocks — plain input), `RuleSetVersionId` (repository + content hash, a version's identity), `RuleSetVersion` (the identity plus its window — `FirstSessionId`/`LastSessionId` — and `SessionCount`) |
| `RuleSetVersionHasher.cs` | `RuleSetVersionHasher.ComputeHash` — the order-insensitive content hash of a block set (FR-27; PRD Part 8 Q4) |
| `RuleSetVersioning.cs` | `RuleSetVersioning.Compute` — groups sessions by repository, orders them chronologically, and groups by hash to produce each `RuleSetVersion` and its window |
| `RuleSetVersionScope.cs` | FR-28's refusal: `RuleSetVersionScope.RequireSingleVersion` returns the one `RuleSetVersionId` a set of sessions share, or throws `MixedRuleSetVersionException` — the primitive a later adherence figure scopes itself with before computing anything |
| `RulesInventory.cs` | FR-40 (S-22, issue #35): `RuleStatementStatus` (the closed four-shape status union), `RuleRetirement` (in force / retired at a date), `RulesInventoryRow`, `RulesInventoryStatusCounts`, `RulesInventoryState`, `RulesInventory.Build`/`.MostRecentVersion`, and `UnknownRuleSetVersionException` — one rule-set version's statements, each with exactly one status, its origin, its reach, its in-force window and its retirement |

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

### `PermissionPromptOutcome.ResultKind` is carried verbatim, never matched against a denial string

FR-20's Scenario 2 requires a permission prompt's outcome to come "from the recorded result kind,
not inferred." `PermissionPromptOutcome.ResultKind` is whatever string the caller resolved from
`permission.completed.data.result.kind` — this project never compares it against a literal like
`"denied"`, because Repo Rule 6 forbids naming values this project cannot verify against Copilot's
actual enum, and doing so would turn "read from the field" back into a string match, the exact
thing FR-20 says Copilot data avoids. `null` means the prompt never resolved at all — a distinct
state from any recorded outcome, denial included, per the measured 1,033-requested-against-1,031-
completed edge case (issue #30).

### `InterruptionLoad` pairs its counts the same way `HookFailureCounts` does

`PermissionPromptCount` and `QuestionCount` are both `required` on `InterruptionLoad`, so a caller
cannot construct a result that states one without the other — the same reasoning
`HookFailureCounts` documents for its two denominators. `PermissionPromptsWithoutOutcome` is a
computed property, never a stored field, mirroring `FailureRate.Percentage`: there is no
constructor path that could let it disagree with the two counts it is derived from.

### A block's source file is its own first line, heading marker stripped

`RuleStatementExtractor.ExtractBlocks` takes the first non-blank line inside a
`<custom_instruction>…</custom_instruction>` block, strips a leading `#`/whitespace run and trims
what remains, and uses that as `SourceFile`. This is not a guess: Copilot inlines a repository's own
file verbatim, and this repository's own `CLAUDE.md` (like the reference corpus's) begins with
`# CLAUDE.md` — so the block's own first line already *is* its heading, whether that heading names a
real file (`CLAUDE.md`, `AGENTS.md`) or a section Copilot injects under a non-file label (`Agent
workflow`, `Copilot instructions`, both measured in FR-26). The extractor does not try to tell those
two kinds apart — it reports whichever text the block was headed by, verbatim.

### The extraction unit is a line, not a paragraph

`RuleStatementExtractor` matches a markdown list marker (`-`, `*`, `+`, or `\d+[.)]`) at the start of
a line and treats everything from there as one statement — no continuation-line joining, no
nesting-aware grouping. A list item that is itself a heading for nested bullets (issue #32's edge
case — "Use `codebase-memory` MCP before broad file search when asking:") is one statement like any
other; whether it is a rule, a heading, or a documentation index entry is S-22's classification to
make, not this extractor's to decide by filtering. Prose lines (no marker) are skipped for
statements but never lost — the block's own text is not altered, only read.

### A block with no list item still appears, with an empty `Statements`

`InstructionBlock.Statements` can be empty. Dropping such a block instead would erase the difference
FR-26's fourth scenario is about — a session with no `<custom_instruction>` block at all
(`SessionInstructionBlocks.Blocks` itself empty) is not the same fact as a session whose block(s)
carried only prose (`Blocks` non-empty, every block's `Statements` empty). `HasInstructionBlocks` is
a computed property over `Blocks.Count`, not a second stored flag that could drift from it.

### A statement's identity is `(SourceFile, Text)`, not `Text` alone

`RuleStatementDeduplication.Deduplicate` groups by the pair, so the same wording headed by two
different files is not collapsed into one occurrence — attribution is part of what FR-26's first
scenario asks a recovered statement to carry, so it is part of what makes two statements "the same"
here too. Session ids are deduplicated per statement (a `HashSet`-style check on add) so a session
whose own text repeats a statement across two blocks — or across two of its own `system.message`
events, unioned by `AecoPostMortem.Ingestion.SessionRuleExtractor` — still contributes exactly one
session id, not one per repetition.

### Cross-session resolution deliberately stays in `Rules`, not `Ingestion` or `Findings`

`RuleStatementDeduplication.Deduplicate` takes `SessionInstructionBlocks` — a plain per-session
shape, the same kind of input every other check in this project takes — rather than living beside
`SessionRuleExtractor` in `Ingestion`, which is the project that actually resolves a session to its
`RawEvent`s. The split mirrors `RepeatedReadCheck`/`ReadEvent`: whoever reads the store resolves
sessions to plain shapes and hands them in; the reduction over many sessions' worth of shapes is a
pure function of its input and belongs where every other pure check-shape reduction already lives.

### Position is derived by ordering, not read off a field

`AbortedTurnCheck.Run` groups `TurnRecord`s by `SessionId`, orders each session's turns by
`StartedAt` (ties broken by `TurnId`, ordinal string comparison, for a deterministic result
regardless of input order — PRD §3.8), and reports each aborted turn's 1-based index in that
ordering alongside the session's total turn count. Copilot's own event log carries no ordinal turn
number, so "position in the session" (issue #28, Scenario 1) only exists once every turn in the
session — not only the aborted ones — has been placed in order; that is why `TurnRecord` covers
every turn, `Aborted` and all, rather than taking a list of already-known aborts.

### One occurrence per abort, never grouped by reason

Unlike `HookFailureCheck` (grouped by hook identity) or `FailedToolCallsCheck` (grouped by tool
identity), `AbortedTurnCheck` groups only by session — the reason text plays no role in identity.
A measured 9 aborts across 8 sessions is low volume (issue #28's edge case): two aborts sharing the
same reason string in different sessions are still two independent abandonments, and merging them
by reason would make the finding look more recurring than the corpus measures.

### The phase vocabulary and its ordering are corpus-wide, never per-session

FR-19 requires "an earlier phase" to mean something, which needs a vocabulary and an ordering that
neither implementation may hard-code. `PhaseOrdering.Derive` groups `DeclaredIntent` by `Phase` and
orders by each phase's *earliest* `Sequence` across every session combined — a phase declared late
in one session but early in another is ordered by whichever declaration came first corpus-wide, the
same discipline `ToolVocabulary`/`ToolRoleDeriver` (S-21) apply to tool names. `PhaseChurnCheck.Run`
derives this ordering exactly once per call and reuses it for every session, so two sessions in the
same run are always judged against the same phase order.

### A return is "below the highest phase reached so far", not "below the previous phase"

`PhaseChurnCheck`'s per-session loop tracks `highestReached`, the largest ordering position seen so
far in that session — not simply the position of the previous intent — and `highestReached` only
advances when an intent's position is at or above it. A call to phase 2 then phase 0 then phase 1
counts **two** returns, not one: phase 0 is below the high-water mark of 2 (a return), and phase 1 is
*still* below that same high-water mark of 2 (a second return), because `highestReached` never
dropped to 0 in the first place — only a new maximum moves it. `Each_return_below_the_highest_phase_
reached_so_far_is_counted_separately` (`PhaseChurnCheckTests`) is this exact shape and asserts 2.
Declaring the same phase again in place — whether or not it is the session's current high-water
mark — is never a return, because "at or above" includes equal.

### `PhaseChurnResult` carries its own denominator and the derivation that produced it

Issue #29's edge case is a measured 104 returns across 352 intents in the worst session: an
un-normalised return count always makes the longest session look worst. `PhaseChurnResult.Returns`
never appears without `TotalIntents` — that session's own count, not the corpus's — and every
result also carries `Vocabulary`, the same ordered list `PhaseOrdering.Derive` produced, so a
rendered result is never separated from the derivation that could make two implementations disagree
(Scenario 2). A session that declares no intents contributes no `PhaseChurnResult` at all, because
grouping is over the intents themselves — there is no session-enumeration side channel that could
produce a zero for it.

### Operand resolution is layered lookups over S-21's own outputs, not a parallel classifier

`OperandResolver.Resolve` tries `ToolVocabulary.Build` (exact name), then a structural equality
check against each call's own `McpServerName` (the server-field layer), then
`ToolRoleDeriver.Derive` (the role layer) — in that order, returning on the first layer that
produces at least one tool. It reuses S-21's two functions rather than re-deriving vocabulary or
roles its own way, so a future change to how a role is classified only has one place to change.

### The server-field layer is a structural equality check, never a substring match on `ToolName`

FR-31's own worked example is the reason this exists: a rule about one MCP server must exclude a
different server's tool whose name happens to contain the same text (measured 28 tools resolve
correctly under this layer; issue #51's edge case documents the same failure mode discovered from
the other direction — a circularity check that matched a substring pulled in 9 calls to a different
server and 2 to a variant name). `OperandResolver.Resolve` compares `operandText` against
`ToolInvocationShape.McpServerName` with `string.Equals(..., StringComparison.Ordinal)` — never
`Contains` — and collects every tool whose calls carry that exact server name.
`The_server_field_is_a_structural_match_not_a_substring_match_on_tool_names` in
`OperandResolverTests` proves it with a tool whose own name contains the server text as a substring
but whose logged server field names a different server.

### The role layer matches an operand's text against `ToolRole`'s own member names

FR-31 names "the derived role" as the third layer without stating what text a rule's own operand
would carry to select one. Rather than guess a display convention (`"file-read"`, `"Search tools"`,
...) this project cannot verify against Copilot's actual rule phrasing, `Resolve` parses
`operandText` directly against `ToolRole`'s five member names (`Enum.TryParse<ToolRole>(operandText,
ignoreCase: false, ...)`). This keeps the layer exact and closed to the same five-member vocabulary
`ToolRole.cs` already defines, with nothing here able to invent a sixth label. Whatever project
extracts operand text from a rule statement's own phrasing (S-25, FR-34) is responsible for
normalising it to one of these five names before calling in, the same way every other check in this
project takes an already-resolved plain input rather than doing its own text interpretation.

### `Unresolved` is a fourth enum member, not an empty `Tools` set on a resolved layer

FR-31's fourth layer is required to be reported, never silently dropped. `Resolve` only returns a
resolved layer (`ExactToolName`, `McpServerField`, `DerivedRole`) when that layer actually produced
at least one tool — an empty match at any layer falls through to the next one, and running out of
layers is `Unresolved`, its own enum value rather than an empty `Tools` set on a layer that claims
to have matched. `Layer == Unresolved` is therefore the one condition a downstream caller (S-26,
FR-35 — "this rule names a tool your agent does not have") checks to build its finding, with no
count-based inference needed.

### Subtraction only ever removes from operand B; operand A is returned unchanged

FR-32 states the tie-break as "A winning", not "the more/less confident layer winning" — the two
operands can resolve through different layers entirely (the discovery finding 8 defect was exactly
this: an exact-name match for one operand colliding with a role-layer match for the other) and A
still wins regardless of which layer produced either side. `ResolveTwoOperands` therefore performs
one direction of subtraction only: `OperandB.Tools = ResolvedB.Tools - ResolvedA.Tools`, with
`OperandA` returned exactly as `Resolve` produced it. `OperandB.Layer` is left as whatever layer
originally resolved it, even if subtraction empties its `Tools` — that is a different fact ("B's
own claim was entirely absorbed by A") from `Unresolved` ("nothing matched B in the first place"),
and collapsing the two would make a resolution's own record of what happened lie about which one
occurred.

### A version's identity is the (repository, hash) pair, groups by hash within chronological order

`RuleSetVersioning.Compute` orders each repository's sessions by `StartedAt` (ordinal, `SessionId`
breaking ties — the same discipline `AbortedTurnCheck` and `PhaseOrdering` use) and then groups the
already-ordered sequence by its sessions' content hash. `IEnumerable.GroupBy` preserves first-seen
order both across groups and within one, so a group's first and last members are automatically the
chronologically first and last sessions that carried that hash — `FirstSessionId`/`LastSessionId`
never need a separate min/max pass. Two sessions with the identical hash are the same version even
if a different-hash session sits between them in time (a rule edited, then reverted); this was not
in the measured corpus and the acceptance criteria only ask for "the first and last session carrying
it," so this project does not split a reappearing hash into two windows.

### The version hash is order-insensitive over blocks, order-preserving within one

`RuleSetVersionHasher.ComputeHash` canonicalizes each block to `SourceFile` followed by its
statements in extraction order, then sorts the canonicalized blocks themselves
(`StringComparer.Ordinal`) before hashing — PRD Part 8 Q4 records that whether a session's blocks
arrive in a stable order was never measured, so two sessions carrying the identical set in a
different order must hash identically. Statement order *within* a block is left alone: it comes from
`RuleStatementExtractor` reading the same source document top to bottom, which is not the axis Q4
left open.

### Fields are length-prefixed, never joined with a separator character

`RuleSetVersionHasher.LengthPrefixed` encodes every field (a block's source file, each statement) as
`"{length}:{value}"` (netstring-style) rather than joining fields with a delimiter character. A
delimiter is only collision-safe if it is guaranteed absent from every field's own content, and
extracted rule text is arbitrary, unvalidated operator prose — a first version of this hasher joined
fields with ASCII control characters on exactly that unenforced assumption, and code review caught
the seam it left open: a source file and statement text that happened to contain the same control
character could canonicalize to the identical string as a different split of the same characters,
colliding two different block sets onto the same hash. Length-prefixing has no such seam — the
encoding of a sequence of fields is injective regardless of what those fields contain, so it needs no
assumption about what extracted text does or does not include.

### The refusal is a primitive, not wired to an adherence figure yet

FR-28 says a figure spanning a rule edit "must be impossible to compute, not merely discouraged,"
but no adherence check exists in this project yet (that is later work). `RuleSetVersionScope.
RequireSingleVersion` is deliberately generic: it takes whatever `SessionRuleSet`s a future figure
would be computed over and throws `MixedRuleSetVersionException` unless they share one repository
and one hash — the same `RuleSetVersionId` `RuleSetVersioning` produces — so a later check calls it
first and cannot construct a figure across an edit even by accident, mirroring how `HookFailureCounts`
makes a bare denominator uncompilable rather than merely undocumented.

### A statement's status is supplied by the caller, not decided here

`RulesInventory.Build` takes a `Func<RuleStatement, RuleStatementStatus>` rather than classifying
statements itself, the same shape `Api.DigestEnvelope.From` takes its finding mapper. Deciding
whether a statement matches a built check shape is S-25's catalogue work (FR-34, issue #39), which
this project does not carry yet — and the boundary between *Checkable — not yet built* and *Not
checkable* is exactly the thing that catalogue defines: the first means "a shape could express this,
none exists"; the second means "no shape can express it, because the logs do not record what it
talks about", which is why FR-40 requires it to state a reason. Baking a classifier in here would
have meant guessing that boundary and then having to un-guess it when S-25 lands. When it does, only
the function passed in changes; every scenario this file's inventory implements is unaffected.

### Retirement is positional in the repository's own session order, never a computed date

FR-40's "adherence frozen at the date it was removed" needs a date, and the corpus has no removal
event — a rule vanishes by simply not appearing in the next session's prompt. `RetirementOf` takes
the repository's chronologically ordered sessions, finds the last one whose **own block set** carried
the statement, and reports the *next* session's own `StartedAt`: the first moment the log shows the
statement gone.

The search is over each session's block set, not over the row's `SessionIds`, and the difference is
a real bug that was caught in review rather than a stylistic preference. `SessionIds` holds only the
*selected* version's sessions, and because sessions share a version precisely when their block sets
are identical, it is **the same list for every row in that version**. Searching it dates every
statement's removal to the end of the selected version — so a statement that a later version went on
carrying for weeks is reported as removed at the wrong date, and `AdherenceFrozenAt` inherits it.
`A_statement_a_later_version_kept_is_retired_at_that_later_removal_not_at_this_versions_end` is the
three-version fixture that pins this; a two-version corpus cannot tell the two derivations apart,
which is why the original suite passed.
Nothing here reads a clock (the determinism contract, PRD §3.8), and nothing derives a date
independently of the sessions themselves. A statement present in the repository's most recent
version is never retired, so the default view — the most recent version, `MostRecentVersion` — is
the one in which nothing is frozen.

### Exactly one version, and no shape here can hold two

`Build` takes one `RuleSetVersionId` and produces rows only from sessions carrying that hash;
`AvailableVersions` carries other versions' *identities and windows*, never their statements. This
is FR-40's Scenario 6 made structural rather than conventional, and it matters more than the wording
suggests: a measured 34 of 43 statements are absent from the most recent session, so the
union-of-all-versions table the digest mockup showed would render three quarters of its rows as
though they were still in force (PRD Part 4).

### Three empty states, not one

`RulesInventoryState` distinguishes `NoInstructionBlocks` from `BlocksCarriedNoStatements` even
though Scenario 4 asks only that "no rules were found" be stated. FR-26's own fourth scenario already
established that these are different facts — `SessionInstructionBlocks.HasInstructionBlocks` exists
for exactly this — and they have different fixes: the first says this repository has no written
rules, the second says it has them and the list-item extraction unit found nothing in them, which
would be an extraction defect rather than an empty repository.

### Known seams the inventory inherits rather than fixes

Three things a reader of `RulesInventory.cs` will notice. All three are pre-existing and none is
changed by S-22, because changing any of them changes a merged story's semantics:

- **A null `Repository` is one bucket, not "unknown".** `RuleSetVersioning.Compute` groups by
  `session.Repository`, and `null == null`, so every session whose repository could not be resolved
  shares a scope. `RulesInventory.Build` follows the same rule (`string.Equals(..., Ordinal)`), which
  means under a null scope the "most recent session" driving retirement can belong to an unrelated
  project. The web surface labels this scope "no recorded repository" rather than naming a project,
  so it is at least not mislabelled — but a removal date computed under it is only as meaningful as
  the grouping. Fixing it means giving S-20 a real "repository unknown" identity, which is its story.
- **Every row in a version shares one carrying-session list and one in-force window.** By
  construction: sessions share a version precisely because their block sets are identical, so a
  statement in that version was carried by all of them. The per-row `SessionIds`/`InForceFrom`/
  `InForceUntil` therefore vary across *versions*, not across rows within one. That is correct for
  Scenarios 2 and 3 under version scoping, and it is exactly why retirement could **not** be derived
  from `SessionIds` — see the note above.
- **`RuleSetVersionHasher` canonicalises `block.SourceFile` + `statement.Text`, while
  `RuleStatementDeduplication` keys on `statement.SourceFile` + `Text`.** `RuleStatementExtractor`
  always sets the statement's source file from its block's, so the two agree on any extracted corpus;
  a hand-built `RuleStatement` with a mismatched `SourceFile` could make two sessions share a hash
  yet yield different rows, which would break the one-version-one-statement-set assumption `Build`
  leans on. Worth knowing before hand-constructing fixtures.

### `UnknownRuleSetVersionException` is not the empty state

Asking for a version no session carried is an error; a version whose sessions carried no rules is a
designed, renderable state. Collapsing the two would make "this repository has no rules" and "you
passed a hash that never existed" indistinguishable to a caller — the same reasoning
`MixedRuleSetVersionException` gives for being its own type rather than a bare `ArgumentException`.

## Status

Tool vocabulary and role derivation (S-21, issue #34) has landed, and so has four-layer operand
resolution (S-23, issue #37, FR-31/FR-32): `OperandResolver.Resolve` and `.ResolveTwoOperands` are
the mechanism every check shape with a named operand (S-25's catalogue, S-26's "tool your agent
does not have") builds on rather than reimplementing tool classification. Nothing yet extracts
operand text from a rule statement's own phrasing — that is S-25's job (FR-34); this story
publishes the resolution mechanism the way S-21 published vocabulary/role derivation ahead of
anything calling it with real rule text.

The check-shape catalogue has
six entries: `HookFailureCheck` (issue #27, FR-17), `RepeatedReadCheck` (issue #25, FR-15),
`FailedToolCallsCheck` (issue #26, FR-16), `InterruptionLoadCheck` (issue #30, FR-20),
`AbortedTurnCheck` (issue #28, FR-18) and `PhaseChurnCheck` (issue #29, FR-19). The shape they
establish — plain per-call/per-session/per-turn input records in, structurally-required or
structurally-paired results out, no branch on any specific tool name — is the pattern later checks
in this project should follow.

FR-26's extraction contract (S-19, issue #32) has also landed: `RuleStatementExtractor.ExtractBlocks`
parses `<custom_instruction>` blocks from plain prompt text, and
`RuleStatementDeduplication.Deduplicate` collapses identical statements across sessions while
preserving which sessions carried each one.

FR-40's inventory (S-22, issue #35) has landed on top of both: `RulesInventory.Build` reduces a
corpus of `SessionRuleSet`s to one version's statements, each with exactly one
`RuleStatementStatus`, its source file, its carrying sessions, its in-force window and its
`RuleRetirement`. It is the first consumer of `RuleStatementDeduplication` and `RuleSetVersioning`
together. It still classifies nothing itself (see the non-obvious decision above) — the caller
supplies each status, and S-25's catalogue is what will supply it for real. `AecoPostMortem.Api.
RulesInventoryEnvelope` is the wire shape, and `web/src/routes/RulesInventoryPage.tsx` renders it;
no endpoint serves it from a live store yet, the same not-yet-wired gap `ProcessDigest` documents.

Rule-set versioning (S-20, issue #33, FR-27/FR-28) has also landed: `RuleSetVersioning.Compute` turns
a corpus of `SessionRuleSet`s into `RuleSetVersion`s (identity, window, sample size) per repository,
and `RuleSetVersionScope.RequireSingleVersion` is the refusal primitive a later adherence figure
scopes itself with. Nothing yet resolves a real corpus's `RawEvent`s into `SessionRuleSet`/
`SessionInstructionBlocks` at scale and dedupes or versions the whole store in one pass — that
wiring, and the adherence figure itself, are later work; this project only publishes the shapes that
work builds against, the same way S-49 published NORMALIZED's eight shapes ahead of anything
populating them.
