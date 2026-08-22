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
| `ToolVocabularyMismatchCheck.cs` | FR-35 (S-26, issue #40): `RuleToolMention` (a rule's source text, one named tool, and the `ToolRole` it targets — plain input), `ToolVocabularyMismatch` (sealed base) with `MinorToolNamed`/`NonExistentToolNamed`, and `ToolVocabularyMismatchCheck.Run`, which resolves each mention through `OperandResolver` and compares against the target role's `DominantTool` |
| `BannedToolCheck.cs` | Piece 3's `ToolIsBanned` adherence check: `BannedToolMention` (a rule's source text and the one tool it bans — plain input, no `ToolRole`) and `BannedToolUsage` (the mention plus its resolved tools and call count), and `BannedToolCheck.Run`, which resolves each mention through `OperandResolver` and reports every resolved mention — always a violation, see below for why `ToolVocabularyMismatchCheck` does not fit a prohibition |
| `NeverReadPathCheck.cs` | Piece 3's `NeverReadPath` adherence check: `NeverReadPathMention` (a rule's source text and the one path it prohibits — plain input) and `NeverReadPathViolation` (the mention plus how many real `ReadEvent`s matched it and which sessions), and `NeverReadPathCheck.Run`, which matches each mention's path against the corpus on a path-segment boundary (never a bare substring) and reports only mentions with at least one match — no `OperandResolver` involved, since a path operand is not a tool-vocabulary lookup |
| `UseAAfterBCheck.cs` | Piece 3's `UseAAfterB` adherence check: `UseAAfterBMention` (a rule's source text plus `LaterToolText`/`EarlierToolText` — plain input), `TimedToolCall` (a call's session, tool name and `StartedAt`, opaque and ordinally sortable — no `Data.Execution.ToolCall` reference), `UseAAfterBViolation` (the mention plus how many later-tool calls had no earlier prerequisite call and which sessions), and `UseAAfterBCheck.Run`, which resolves both operands via `OperandResolver.ResolveTwoOperands` (skipping a mention with either side `Unresolved`, the same "no clean case reported" shape `BannedToolCheck` follows) and orders each session's calls by `StartedAt` itself (never trusting caller order) before walking them for the ordering violation |
| `AlwaysPassParamCheck.cs` | Piece 3's fifth and final slice, `AlwaysPassParam`'s adherence check: `AlwaysPassParamMention` (a rule's source text and the one argument key it requires — plain input), `ParamCarryingCall` (a call's session, tool call id, `SpawnsAgent`, `ArgumentsRecorded` and the opaque `ArgumentKeys` set its own RAW arguments carried), `AlwaysPassParamViolation` (the mention plus how many subagent-dispatch calls omitted the key and which sessions), and `AlwaysPassParamCheck.Run`, which filters to `SpawnsAgent && ArgumentsRecorded` calls only — the one structural, Repo-Rule-6-safe population this shape's own operand can name without guessing (see below), narrowed further to calls this project actually has a record of — and reports a mention only when at least one such call is missing the key, the same "no clean case reported" shape `BannedToolCheck`/`NeverReadPathCheck` already follow |
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
| `RuleSetVersion.cs` | FR-27 (S-20, issue #33): `SessionRuleSet` (a session's repository, start time and blocks — plain input), `RuleSetVersionId` (repository + content hash, a version's identity), `RuleSetVersion` (the identity plus its window — `FirstSessionId`/`LastSessionId` — and `SessionCount`). `FirstSessionStartedAt` (chronology fix, follow-up to #108) carries `FirstSessionId`'s own start time — the real sort key every chronological ordering of versions uses; `FirstSessionId` itself is opaque (a random UUID in the reference corpus) and carries no ordering meaning of its own |
| `RuleSetVersionHasher.cs` | `RuleSetVersionHasher.ComputeHash` — the order-insensitive content hash of a block set (FR-27; PRD Part 8 Q4) |
| `RuleSetVersioning.cs` | `RuleSetVersioning.Compute` — groups sessions by repository, orders them chronologically, and groups by hash to produce each `RuleSetVersion` and its window |
| `RuleSetVersionScope.cs` | FR-28's refusal: `RuleSetVersionScope.RequireSingleVersion` returns the one `RuleSetVersionId` a set of sessions share, or throws `MixedRuleSetVersionException` — the primitive a later adherence figure scopes itself with before computing anything |
| `RulesInventory.cs` | FR-40 (S-22, issue #35): `RuleStatementStatus` (the closed four-shape status union), `RuleRetirement` (in force / retired at a date), `RulesInventoryRow`, `RulesInventoryStatusCounts`, `RulesInventoryState`, `RulesInventory.Build`/`.MostRecentVersion`, and `UnknownRuleSetVersionException` — one rule-set version's statements, each with exactly one status, its origin, its reach, its in-force window and its retirement |
| `RuleShape.cs` | FR-34 (S-25, issue #39): `RuleShapeKind` (the closed five-member shape enum), `RuleShapeMatch` (a statement, its shape, and the operand text lifted from it), `UnmatchedStatementDisposition`/`UnmatchedStatement` (FR-40's two middle inventory statuses, each with a reason), and `RuleShapeMatching` (the partition, with a computed `StatementCount`) |
| `RuleShapeCatalogue.cs` | FR-34's catalogue itself: `RuleShapeCatalogue.Shapes`, `.TryMatch` and `.MatchAll` — eight phrasing patterns across five shapes, matched in precedence order, with operands read from the matched statement's own text |
| `RuleOperandText.cs` | `RuleOperandText.Normalize` (a captured span reduced to the operand: code span, article, gerund, subordinate clause, role noun — grammar only), `.NormalizeForParameterNameShape` (the same reduction, minus "and"/"or" as a clause boundary — `AlwaysPassParam`'s own operand-shape guard uses this second reduction so a compound phrase joined by "and" is not silently collapsed to one spurious word), `.LooksLikePath` (a test of the operand's own characters, never a comparison against a path this project knows), and `.LooksLikeParameterName` (a single-token test — a real JSON argument key is always one token, so a multi-word capture is rejected rather than matched with unearned confidence) |
| `ContradictionCheck.cs` | FR-43 (S-38, issue #47): `ContradictionCandidate` (a pair of statements plus their shared, negation-stripped wording) and `ContradictionCheck.Run` — pairwise keyword-polarity detection over whatever statements the caller hands in, `i < j` only so no statement is ever compared against itself |
| `RuleSetVersionAdjacency.cs` | FR-39 (S-35, issue #43): `RuleSetVersionAdjacency.RequireAdjacentPair` — confirms two `RuleSetVersionId`s are the same repository and immediately consecutive within a repository's own chronologically ordered `RuleSetVersion`s, returning both as `(Before, After)`, or throws `MixedRuleSetVersionException` (different repositories), `UnknownRuleSetVersionException` (a hash the repository never carried) or `NonAdjacentRuleSetVersionsException` (naming every intervening version) — the primitive the Monitor comparison scopes itself with before computing anything |

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

**And no name may appear in a literal here** (S-25's second scenario).
`RulesProjectNamesNothingTests.The_rules_project_uses_only_a_reviewed_vocabulary_in_its_literals`
extracts every string and character literal in this project and requires every word in them to be on
`PermittedVocabulary` — an allowlist of English grammar, FR-30's permitted argument field names, and
regex group names. This is an allowlist for the same reason the reference rule is: a blocklist of
tool names can only reject the tools this author happened to know, which is the exact assumption
FR-34 exists to remove. A tool, MCP server or repository name introduces a word that is not grammar,
and the build fails. Adding a word to that list is the reviewable act the test exists to force.
`The_rules_project_names_no_repository_the_frozen_corpus_names` is the second half: it reads the
frozen corpus manifest's own `repository` fields and asserts none appears anywhere in this project's
source — comments and identifiers included, which the literal scan does not reach. Both were checked
by mutation: injecting a tool name, an MCP server name and a repository name here fails them.

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

### `ToolVocabularyMismatch` is a sealed hierarchy, not one type with nullable fields

FR-35's two scenarios carry structurally different data: a minor tool named needs the dominant tool
and both call counts, a non-existent tool needs neither. `MinorToolNamed` and `NonExistentToolNamed`
both derive from `ToolVocabularyMismatch` rather than being one record with nullable
`DominantTool`/counts, the same reasoning `OperandResolutionLayer.Unresolved` gives for being its own
enum member instead of an empty `Tools` set — there is no field here that could be null for one kind
and required for the other, so a caller pattern-matching on the type can never observe a
half-populated result.

### `ToolVocabularyMismatchCheck` reuses `Unresolved` and `DominantTool` rather than re-deriving either

`Run` calls `OperandResolver.Resolve` once per mention and checks `Layer == OperandResolutionLayer.
Unresolved` for the non-existent case — the exact condition `OperandResolver.cs`'s own doc comment
names S-26 as the caller of. For the minor-tool case it compares `resolved.Tools` (not the mention's
raw text) against `ToolRoleDeriver.Derive(...).Roles[TargetRole].DominantTool.ToolName`, so an operand
that resolved through the server or role layer to a set containing the dominant tool is not flagged
even though its own text is not the dominant tool's literal name.

### A target role with no tools at all produces no finding, even for a tool that exists

`DominantTool` is `null` when a `ToolRoleSummary`'s `Tools` list is empty (its own computed-property
contract). `Run` treats that as "nothing to compare against" and skips the mention rather than
fabricate a mismatch against an absent dominant tool — a decision this project's own test,
`A_target_role_with_no_tools_at_all_produces_no_finding_for_an_existing_named_tool`
(`ToolVocabularyMismatchCheckTests`), pins down since neither Gherkin scenario in issue #40 covers it.

### `BannedToolCheck` exists because `ToolVocabularyMismatchCheck` does not fit a prohibition

`ToolVocabularyMismatchCheck` was built for a recommendation ("prefer / always use tool X for role
Y"), where "X is not the dominant tool for Y" is a real mismatch worth flagging. A prohibition
("never use X") does not target a role the way a recommendation does, and neither of that check's
two outcomes reads as a meaningful violation signal for a ban: `NonExistentToolNamed` (the tool was
never called) would mean the ban is being *honored*, and `MinorToolNamed` (the tool, when called, is
not the dominant tool of the role its own calls happen to classify into) would fire on nearly every
real ban and say nothing, since a banned tool being non-dominant is true almost by definition.
`BannedToolCheck` answers the actually adherence-worthy question instead — was the named tool called
at all — with no `ToolRole` involved.

### `NeverReadPathCheck` matches on a path-segment boundary, never a bare substring

A rule's own path operand is typically relative (this repository's own rule, `` Never read
`src/AecoPostMortem.Data/Migrations/` ``, is itself an example), while a real observed
`ReadEvent.Path` is absolute — confirmed against the live reference corpus, whose own `ToolCall.Path`
values are absolute Windows paths (`F:\git\UpFront\...`). An exact-string match would therefore never
fire, but an unqualified substring match risks the same "confident wrong operand" failure mode this
project has already been burned by once (`Ingestion/CLAUDE.md`'s own `EventEnvelopeParserV1`
cautionary tale) — an operand like `Data` would wrongly match a directory named `DataAccessLayer`.
`NeverReadPathCheck.Matches` normalizes both sides to `/` and requires the operand to align on a `/`
boundary at both ends (whole path, leading segment run, trailing segment run, or a segment run in the
middle) — verified against the live corpus: a real rule naming `UpFront.Data/Migrations/` correctly
matches paths under it and does not match the lookalike `UpFront.Auth.Data/Migrations/` directory,
since the two do not share a contiguous substring at all. Matching is case-insensitive
(`OrdinalIgnoreCase`) — real observed paths are Windows filesystem paths, and Windows filesystems are
case-insensitive, the same reasoning `Ingestion.SessionExclusion`'s own path-prefix matcher already
follows (caught in code review: an earlier version used `Ordinal`, which would silently miss a real
violation whose operand and observed path differed only in case).

### `NeverReadPathCheck` needs no `OperandResolver`, unlike every other adherence check in this file

`PreferAOverB` and `ToolIsBanned` both resolve a rule's operand against the corpus' own tool
vocabulary (`OperandResolver.Resolve`/`.ResolveTwoOperands`) because their operands name tools.
`NeverReadPath`'s operand names a path, and a path is matched against `ReadEvent.Path` directly — there
is no tool-name resolution question to ask, so this check is a plain segment-boundary match over its
own input with no dependency on `OperandResolver` at all.

### `UseAAfterBCheck` needs ordering, so it takes `TimedToolCall` rather than `ToolInvocationShape`

`ToolInvocationShape` (the corpus every other adherence check resolves operands against) carries no
timing field at all — it is a per-call argument shape, not a session-scoped, orderable event. Real
observed `Data.Execution.ToolCall.StartedAt` is an already-real, populated ISO-8601 timestamp string
that sorts correctly under plain ordinal comparison (`Ingestion.ExecutionRecordBuilder` already
builds its own `ToolCall` rows this way), so `UseAAfterBCheck` takes a second, generic plain-input
shape — `TimedToolCall` (`SessionId`, `ToolCallId`, `ToolName`, `StartedAt`) — built straight from
that column, the same "no new RAW parsing needed" story `NeverReadPathCheck` tells for
`ToolCall.Path`. Operand resolution (which real tools each operand text names) still goes through
`OperandResolver.ResolveTwoOperands` against the ordinary `ToolInvocationShape` corpus, exactly like
`PreferAOverB` — the two corpora answer two different questions, "which tools" and "in what order",
and neither can answer the other's.

### `UseAAfterBCheck` orders each session by `StartedAt` itself, and any earlier prerequisite call satisfies the whole session

Mirrors `AbortedTurnCheck`'s own "position is derived by ordering, not read off a field" discipline:
`Run` never trusts the order `TimedToolCall`s arrive in — it groups by `SessionId` and sorts each
group by `StartedAt` (ordinal), tie-broken by `ToolCallId` (ordinal, the same tie-break
`ExecutionRecordBuilder.BuildToolCalls` already uses to produce that column in the first place), then
walks the ordered sequence tracking only "has a prerequisite call been seen yet in this session" — a
call to the later tool is a violation exactly when no earlier-tool call preceded it anywhere in that
same session, not only the immediately preceding call. This was a deliberate choice among three
readings scoped via `AskUserQuestion` before coding: requiring the prerequisite to be the
*immediately* preceding call would flag nearly every real session (interleaved, unrelated calls sit
between almost any two related ones), and requiring a *fresh* prerequisite before every reuse of the
later tool adds bookkeeping no rule in the live corpus has been checked against. Once a prerequisite
has been seen, it silently satisfies every later call to the later tool for the rest of that session —
the same "one clean case can cover many violations that never happen" shape `BannedToolCheck`'s own
zero-violation corpus result already established for a different check.

### `AlwaysPassParamCheck` scopes to `SpawnsAgent` calls because the shape's own operand cannot name a population

FR-34's `AlwaysPassParam` grammar captures only a parameter name ("always pass an explicit A") — the
qualifying clause that would say *which* calls need it ("...when dispatching a subagent") is stripped
by `RuleOperandText.TrailingClause` as decorative, the same stripping every other shape relies on to
keep an operand from dragging its sentence along with it. So this check cannot resolve a population
from the statement's own text the way every other piece-3 check does (a path, a tool name, an
ordering pair). `ParamCarryingCall.SpawnsAgent` is the one structural, Repo-Rule-6-safe population this
corpus already exposes without guessing a tool identity — and it happens to match the one real corpus
instance this shape was scoped against: this repository's own rule, "always pass an explicit model
param when dispatching a subagent" (confirmed present verbatim in one real ingested session's own
`<custom_instruction>` block during scoping, via `superpowers:brainstorming`). This was a deliberate
choice among two considered — every call in scope was the alternative, rejected as certain to flood
with false positives (most tools have no reason to carry an arbitrary key like `model`) — settled via
`AskUserQuestion` before coding, the same "settle the design fork, don't guess" precedent
`UseAAfterBCheck`'s own ordering-semantics decision set.

### `ParamCarryingCall.ArgumentsRecorded` keeps "we don't know" from reading as "it violated"

Also caught in code review, before merge: `ArgumentKeys` alone cannot distinguish "this call's own RAW
arguments were never recorded at all" (no matching `tool.execution_start` event, or a non-object-shaped
value) from "arguments were recorded, and the named key genuinely was not among them" — both produce an
empty set. `AlwaysPassParamCheck.Run` filters its spawn-call population to `ArgumentsRecorded && SpawnsAgent`
rather than `SpawnsAgent` alone, so an unrecorded call contributes no violation at all — the same
"`Unresolved` is its own state, never an empty `Tools` set on a layer that claims to have matched"
discipline `OperandResolver` already documents, applied here to argument-key presence instead of tool
resolution.

### `AlwaysPassParam`'s operand guard is two checks, not one — closed in code review

`RuleOperandText.LooksLikeParameterName` alone only rejects an *already multi-word* operand — it has
no way to notice that `TrailingClause`'s own "and" stripping had already manufactured a single word out
of a real compound phrase before the guard ever saw it. A real ambiguity this project's own live corpus
surfaced during scoping: "always pass build and type checks before committing" means "pass a CI check",
not "pass an argument", but `TrailingClause` treats "and" as a subordinate-clause boundary the same way
it treats "when" or "for", stripping " and type checks before committing" and leaving the single word
"build" — which the parameter-name guard alone would have accepted. Code review caught this before
merge (the deferral this entry originally recorded overstated its own fix cost: `TryMatch` already
holds the pre-normalization span at the exact point it calls `OperandSuitsShape`). The real fix needed
no audit of every other shape's own operand captures, only a second, `AlwaysPassParam`-only reduction:
`RuleOperandText.NormalizeForParameterNameShape` mirrors `Normalize` but excludes "and"/"or" from its
own clause-stripping regex (`TrailingClauseExcludingConjunctions`) — "and" coordinates two nouns in a
compound phrase far more often than this corpus phrases a genuine qualifying clause with it.
`OperandSuitsShape` now requires *both* the ordinary normalized operand and this second reduction of
the raw span to pass `LooksLikeParameterName`, so a compound phrase stays multi-word under at least one
of the two and is rejected. Also closed in the same pass: a path-shaped single-token operand
(`` `CHANGELOG.md` ``) is not a JSON argument key either — `AlwaysPassParam` now also requires
`!LooksLikePath(operandA)`, the same discrimination `ToolIsBanned` already makes in the other
direction.

### `BannedToolUsage.CallCount` can never be zero for a returned result

Every layer `OperandResolver.Resolve` can return (`ExactToolName`, `McpServerField`, `DerivedRole`)
is derived from calls that were actually observed in the corpus passed in — there is no layer that
resolves a name to a tool that was never called. A banned tool that was never called is therefore
`Unresolved`, indistinguishable from an unknown name, and `BannedToolCheck.Run` reports neither: both
are the ban being honored (or the corpus never having the tool in the first place), not a violation.
This is why `BannedToolCheck` cannot follow `FailedToolCallsCheck`'s "report every candidate,
including clean ones, let the caller filter" pattern the way `ToolVocabularyMismatchCheck` does —
there is no clean case for it to report at all; every `BannedToolUsage` returned is already a
violation.

### One `RuleToolMention` per named tool, never a list on one record

Issue #40's edge case is explicit: "a rule naming several tools needs one finding per unresolved
name, not one aggregate finding." `RuleToolMention` carries exactly one `NamedTool`, so a rule
naming three tools is three `RuleToolMention` values fed to one `Run` call — the fan-out is a fact
about the input shape, not a loop this check has to remember to run internally. Whatever project
extracts several tool names from one rule statement's own phrasing (S-25's job, not this project's)
is responsible for producing one `RuleToolMention` per name.

### The catalogue holds shapes, and a shape is grammar — the operands come from the statement

`RuleShapeCatalogue` matches on eight regular expressions across five shapes (`NeverReadPath`,
`PreferAOverB`, `ToolIsBanned`, `UseAAfterB`, `AlwaysPassParam` — the five FR-34 measured firing).
Every token in those patterns is an English verb, modal or particle: `never`, `prefer`, `over`,
`instead of`, `after`, `before`, `always pass`. Nothing in them is a tool, a server or a repository,
which is why the vocabulary test above can be an allowlist of grammar and still pass. The operand is
whatever text the statement itself put between the pattern's keywords, so a repository whose rules
this author never saw is matched by the same five shapes as one they did — that is the whole point
of FR-34, and `The_catalogue_reproduces_the_measured_floor_of_eight_rules_across_five_shapes` proves
it against a fictional repository whose tool names did not exist until that test was written.

### Precedence runs most specific to least, and a rejected operand does not consume the statement

Entries are tried in order: path prohibition, explicit comparison, bare prohibition, ordering,
argument obligation. A statement can satisfy two shapes at once — "do not use A instead of B" is both
a prohibition and a comparison — and reading it as the comparison it spells out loses less than
reading it as a ban on the whole phrase. The two `PreferAOverB` entries carry negative lookbehinds
(`(?<!\bnot\s)`, `(?<!\bnever\s)`) for the opposite case: without them, "do not use A instead of B"
would be read as recommending A, the exact inverse of what it says.

When an entry's pattern matches but the operand fails the shape's own test — `NeverReadPath` requires
a path-shaped operand, `ToolIsBanned` requires one that is not — matching moves to that pattern's
*next* match before moving to the next entry, and out to the inventory only if nothing fits. A
rejected operand therefore never silently converts a rule into an unmatched statement on the strength
of a position the statement had already moved past.

### An operand's own full stop is not the sentence's

The operand capture is lazy and bounded by a sentence terminator, and that terminator requires
whitespace or end-of-input after it (`[.;](?=\s|$)`). Without the lookahead,
``Never read `src/AecoPostMortem.Data/Migrations/` …`` bounds the operand at the dot inside
`AecoPostMortem.Data` and yields `src/AecoPostMortem` — a *confident wrong operand*, which S-25's
edge case names as the failure mode this project must catch by construction rather than by review
(a wrong operand mapping produces a wrong number, not an obvious failure).
`An_operand_is_not_truncated_at_a_full_stop_inside_it` covers both a dotted path segment and a
dotfile; this defect was real and was caught by that test, not by reading the regex.

### A statement matching no shape is dispositioned, never dropped

`MatchAll` partitions rather than filters: every statement handed in leaves as either a
`RuleShapeMatch` or an `UnmatchedStatement`, and `RuleShapeMatching.StatementCount` is computed from
the two so it cannot disagree with them. The disposition is FR-40's two middle statuses — a statement
carrying a normative marker (`never`, `must`, `only`, `required`, …) but matching no shape is
`CheckableNotBuilt`, one carrying none is `NotCheckable` — and `Reason` is `required`, so there is no
constructor path that records an unmatched statement without saying why. The marker list is English
modals, not a list of rules; FR-34's own text permits ordinary grammar and forbids only the three
kinds of name.

### The catalogue resolves nothing itself

`RuleShapeMatch` carries operand *text*, not tools. Turning that text into tools is
`OperandResolver.Resolve`/`.ResolveTwoOperands`, against whatever corpus the caller passes in — so
the two-operand shapes get FR-32's A-wins subtraction for free, and S-26 (FR-35, "this rule names a
tool your agent does not have") is `Layer == Unresolved` on a matched operand with nothing further
built here. Keeping resolution out of the catalogue is what lets both stories share one mechanism
instead of the catalogue growing a second, parallel one.

### A version's identity is the (repository, hash) pair, groups by hash within chronological order

`RuleSetVersioning.Compute` orders each repository's sessions by `StartedAt` (ordinal, `SessionId`
breaking ties — the same discipline `AbortedTurnCheck` and `PhaseOrdering` use) and then groups the
already-ordered sequence by its sessions' content hash. `IEnumerable.GroupBy` preserves first-seen
order both across groups and within one, so a group's first and last members are automatically the
chronologically first and last sessions that carried that hash — `FirstSessionId`/`LastSessionId`
never need a separate min/max pass, and `FirstSessionStartedAt` is read straight off that same first
member. Two sessions with the identical hash are the same version even if a different-hash session
sits between them in time (a rule edited, then reverted); this was not in the measured corpus and the
acceptance criteria only ask for "the first and last session carrying it," so this project does not
split a reappearing hash into two windows.

`Compute`'s own overall result is ordered `(Repository, FirstSessionStartedAt)` — real chronological
order, not by `FirstSessionId` text. A real defect (flagged in PR #108, fixed as a follow-up):
session ids are opaque, random UUIDs in the reference corpus, so an earlier version that sorted by
`FirstSessionId` produced an order with no relationship to time — `RuleSetVersionAdjacency.
RequireAdjacentPair` (below) inherited the same bug, and a real chronologically-adjacent pair in this
corpus could be refused as non-adjacent purely because their UUIDs did not happen to sort next to
each other (confirmed against the live corpus: 19 of 22 real consecutive pairs were wrongly refused
before this fix). `RulesInventory.ChronologicalVersions` no longer needs its own workaround for this
either — see its own doc comment.

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

### Self-match exclusion is structural (loop bounds), not a filter applied after the fact

FR-43's own edge case: a keyword-polarity first pass returned a measured 4 candidates and all 4
were spurious — three matched a statement against itself, because a prohibition contains the
phrase it prohibits ("do not use it" contains "use it"). `ContradictionCheck.Run` never gives
itself the chance to make that mistake: its only loop is `for i in 0..count, for j in i+1..count`,
so statement `i` is never compared against itself and no pair is ever visited twice in either
order. Two statements sharing the same polarity — including two literally identical
statements — are never flagged either: `TryGetSharedWording` requires the negation flag to
*differ* between the pair, so agreement (even duplicated agreement) can never read as conflict.
`ContradictionCheckTests.A_real_prohibition_alone_in_the_set_is_never_flagged_against_itself` uses
the PRD's own worked example text as a regression test for exactly this failure mode.

### Scoping to "one rule-set version" is the caller's job, matching every other check shape

`ContradictionCheck` takes whatever `IReadOnlyList<RuleStatement>` it is handed and compares all of
it — it has no concept of a session, a version or a repository, the same plain-input discipline
`RepeatedReadCheck`/`HookFailureCheck`/every other check in this project already follows.
`AecoPostMortem.Findings.ContradictionCheck` is the orchestrator that groups sessions by rule-set
version before calling in, the same split `RuleSetVersionScope`'s own remarks describe for a future
adherence figure ("this project has no adherence figure of its own yet ... the reusable primitive
any future figure scopes itself with") — except here the scoping is per-version grouping rather
than a single-version refusal, since FR-43 asks for contradictions to be found *within* each
version, not for the whole run to be refused the moment more than one version is present.

### The refusal is a primitive, not wired to an adherence figure yet

FR-28 says a figure spanning a rule edit "must be impossible to compute, not merely discouraged,"
but no adherence check exists in this project yet (that is later work). `RuleSetVersionScope.
RequireSingleVersion` is deliberately generic: it takes whatever `SessionRuleSet`s a future figure
would be computed over and throws `MixedRuleSetVersionException` unless they share one repository
and one hash — the same `RuleSetVersionId` `RuleSetVersioning` produces — so a later check calls it
first and cannot construct a figure across an edit even by accident, mirroring how `HookFailureCounts`
makes a bare denominator uncompilable rather than merely undocumented.

### `RuleSetVersionAdjacency` orders by `FirstSessionStartedAt`, not by insertion order or session id text

FR-39's refusal needs "nothing sits between them" to mean something for whatever order a caller
happens to hand `RuleSetVersioning.Compute`'s own output in — that method already sorts its overall
result by `(Repository, FirstSessionStartedAt)`, but a caller filtering or recombining versions from
several calls could hand `RequireAdjacentPair` a list in any order. `RequireAdjacentPair`
re-orders the repository's own versions by `FirstSessionStartedAt` (`StringComparer.Ordinal`, the
same comparer `RuleSetVersioning.Compute` itself uses — ISO-8601 timestamps sort correctly under it,
the same property `Data.Execution.ToolCall.StartedAt` already relies on) before locating either
requested hash, so adjacency is never a fact about the order two versions happened to arrive in.

An earlier version of this method (through PR #108) re-ordered by `FirstSessionId` instead — the
session id itself, not its start time. That is a real, opaque identifier (a random UUID in the
reference corpus) with no ordering meaning of its own, so "adjacent per that re-sort" quietly stopped
meaning "chronologically next": a real pair of chronological neighbours could land anywhere relative
to each other once re-sorted by UUID text, and `RequireAdjacentPair` would refuse them as non-adjacent
purely on that coincidence. Confirmed against the live corpus before and after this fix: of the 22
real consecutive rule-set-version pairs in the dominant repository, 19 answered
`NonAdjacentRuleSetVersionsException` (404 through `/api/monitor-comparison`) under the old
`FirstSessionId`-text ordering — 17 of those correctly succeed under this one, and the remaining 2
still answer 404 either way, confirmed unrelated to adjacency (no `PreferAOverB` statement to compare
in the `after` version, `Api.CLAUDE.md`'s own `GetMonitorComparison` remarks — this early return
happens before `RequireAdjacentPair` is ever called).

### The refusal carries the full `RuleSetVersion`, not just the id that failed

`NonAdjacentRuleSetVersionsException.Before`/`.After`/`.Intervening` are `RuleSetVersion` values —
identity, window and sample size — rather than bare `RuleSetVersionId`s. A caller reporting the
refusal (FR-39's own Scenario 3: "naming the intervening version") can therefore state which
version sits in the way, when it was in force and how many sessions carried it, without a second
lookup back into `RuleSetVersioning.Compute`'s own output.

### Two requested versions passed in the same, or reversed, order still refuse

`RequireAdjacentPair` requires `after`'s chronological position to be exactly one past `before`'s —
not merely "one apart" — so passing the same version twice, or `before`/`after` swapped relative to
their real chronological order, both refuse via `NonAdjacentRuleSetVersionsException` rather than
silently reordering the caller's own labels. Neither case is named by FR-39's Gherkin, so this
project does not invent a distinct exception type for it: refusing is always the safe default, and
`Intervening` is simply empty when the pair was equal or reversed rather than the pair actually
skipping over something.

### A different repository is `MixedRuleSetVersionException`, reused rather than duplicated

Two versions naming different repositories have no single chronological order to place them in at
all — the same fact `RuleSetVersionScope.RequireSingleVersion` already refuses for a figure spanning
more than one repository. `RequireAdjacentPair` reuses that exact exception type rather than
introducing a second one for what is the identical defect one level up (comparing, not merely
scoping), so a caller catching `MixedRuleSetVersionException` already handles both.

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
the mechanism every check shape with a named operand builds on rather than reimplementing tool
classification. S-26 (issue #40, FR-35, "this rule names a tool your agent does not have") has also
landed on top of it: `ToolVocabularyMismatchCheck.Run` takes `RuleToolMention` values (one named tool
plus the `ToolRole` it targets) and reports `MinorToolNamed`/`NonExistentToolNamed`. Nothing yet
extracts operand text — or which role a statement targets — from a rule statement's own phrasing;
that is S-25's job (FR-34, issue #39), not implemented in this project yet. `RuleToolMention` is the
plain-input shape S-25's extraction is expected to produce one of per named tool, the same way this
project's other checks take an already-resolved plain input rather than doing their own text
interpretation.

The check-shape catalogue itself (S-25, issue #39, FR-34) has now landed and closes that gap:
`RuleShapeCatalogue.MatchAll` takes `RuleStatement`s — what `RuleStatementExtractor` already
produces — and returns the shape each one matched with its operands read from its own text, plus a
dispositioned entry for every statement that matched none. It is the last piece the invariant needed
to be *tested* rather than reviewed, because it is the only part of this project that reads rule
prose at all, and it does so with grammar. Nothing yet feeds a real corpus's extracted statements
through it, resolves the matched operands against that corpus's tools, or renders FR-40's inventory —
that is S-26 (issue #40) and S-38 (issue #47), which consume this surface rather than extend it.

The check-shape catalogue has
eleven entries: `HookFailureCheck` (issue #27, FR-17), `RepeatedReadCheck` (issue #25, FR-15),
`FailedToolCallsCheck` (issue #26, FR-16), `InterruptionLoadCheck` (issue #30, FR-20),
`AbortedTurnCheck` (issue #28, FR-18), `PhaseChurnCheck` (issue #29, FR-19),
`ToolVocabularyMismatchCheck` (issue #40, FR-35), `BannedToolCheck` (piece 3's second slice,
FR-35's `ToolIsBanned` counterpart), `NeverReadPathCheck` (piece 3's third slice, FR-35's
`NeverReadPath` counterpart), `UseAAfterBCheck` (piece 3's fourth slice, FR-35's `UseAAfterB`
counterpart) and `AlwaysPassParamCheck` (piece 3's fifth and final slice, FR-35's `AlwaysPassParam`
counterpart). The shape they establish — plain per-call/per-session/per-turn/
per-mention input records in, structurally-required or structurally-paired results out, no branch on
any specific tool name — is the pattern later checks in this project should follow.

`BannedToolCheck` (piece 3's second slice) closes the `ToolIsBanned` gap the `RulesInventoryClassifier`
remarks (`Api/CLAUDE.md`) once left open: turning a ban into a real verdict does not need a
`ToolRole` after all — see this file's own remarks above for why `ToolVocabularyMismatchCheck`
does not fit a prohibition, and why `BannedToolUsage.CallCount` can never be zero for a returned
result. `AecoPostMortem.Findings.BannedToolFinding` is the real caller, wired into
`AecoPostMortem.Api.ApiHost.GetDigest` as a seventh check orchestrator, and
`RulesInventoryClassifier` now also watches a `ToolIsBanned` match whose single operand resolves.

`NeverReadPathCheck` (piece 3's third slice) closes the `NeverReadPath` gap the same way: no
`OperandResolver` involved, only a path-segment-boundary match against real `ReadEvent`s (see this
file's own remarks above for why an exact or bare-substring match were both rejected).
`AecoPostMortem.Findings.NeverReadPathFinding` is the real caller, wired into `AecoPostMortem.Api.
ApiHost.GetDigest` as an eighth check orchestrator, and `RulesInventoryClassifier` now watches every
matched `NeverReadPath` statement unconditionally — unlike a tool-name operand, a path operand always
produces a determinate verdict against the corpus, so there is no `Unresolved` state to fall through
to. Verified against the live 35-session reference corpus: the dominant repository
(`supahfly27/UpFront`) carries a real `NeverReadPath` rule (`` Never read `UpFront.Data/Migrations/`
unless the task is explicitly about migrations `` — phrased two ways across rule-set versions), and
the check found a real violation: 99 real accesses to that path across the corpus, now a genuine
`RuleAdherenceToolChoice` finding — the first piece-3 adherence check on this corpus to find a real
signal rather than an honest empty state.

`UseAAfterBCheck` (piece 3's fourth slice) closes the last of piece 3's three remaining shapes:
both operands resolve via `OperandResolver.ResolveTwoOperands` against the real `ToolInvocationShape`
corpus, exactly like `PreferAOverB`, and ordering comes from a second, generic `TimedToolCall` shape
built straight from the already-real `ToolCall.StartedAt` column — no new RAW parsing needed, unlike
the "known complexity going in" this slice was originally scoped expecting (see this file's own
remarks above for why `ToolInvocationShape` alone cannot answer an ordering question).
`AecoPostMortem.Findings.UseAAfterBFinding` is the real caller, wired into `AecoPostMortem.Api.
ApiHost.GetDigest` as a ninth check orchestrator, and `RulesInventoryClassifier` now watches a
`UseAAfterB` match the same way it watches `PreferAOverB` — `Watched` only when both operands
resolve. Verified against the live 35-session reference corpus: the dominant repository
(`supahfly27/UpFront`) carries a real `UseAAfterB`-shaped statement (`` Use `get_code_snippet` after
`search_graph` to read function source ``) — the catalogue matches it for real, but at least one
operand stays `Unresolved` against this corpus' own tool vocabulary, so it renders
`CheckableNotYetBuilt` honestly rather than `Watched`, the same "mechanism real, corpus doesn't
happen to fully exercise it" story `PreferAOverB` and `ToolIsBanned` already told for their own real
matches.

`AlwaysPassParamCheck` (piece 3's fifth and final slice) closes the last piece-3 gap and completes
FR-34's five shapes: it filters to `ParamCarryingCall.SpawnsAgent` calls (see this file's own remarks
above for why no other population can be resolved from the shape's own operand) and reports a mention
only when at least one such call omitted the named key. `RulesInventoryClassifier` now watches a
matched `AlwaysPassParam` statement unconditionally, the same "a determinate present/absent verdict,
no `Unresolved` state" reasoning `NeverReadPathCheck` already established for a path operand.
`AecoPostMortem.Findings.AlwaysPassParamFinding` is the real caller, wired into `AecoPostMortem.Api.
ApiHost.GetDigest` as a tenth check orchestrator. Verified against the live 35-session reference
corpus: the one real `AlwaysPassParam`-shaped statement found during scoping (this repository's own
rule, "always pass an explicit model param when dispatching") belongs to a session outside the
dominant repository (`supahfly27/UpFront`) this corpus' endpoints default to, so the dominant repo's
own rules-inventory and digest render unchanged — zero new findings, the same "mechanism real, corpus
doesn't happen to exercise it in the selected scope" story `BannedToolCheck`/`UseAAfterBCheck` already
told, confirmed via a real browser session against `/` and `/rules`, plus a synthetic corpus at the
unit level where a violation genuinely fires (`AlwaysPassParamCheckTests`,
`AlwaysPassParamFindingTests`, `RulesInventoryClassifierTests`).

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

The contradiction check (S-38, issue #47, FR-43) has also landed: `ContradictionCheck.Run` is the
pairwise, self-match-excluding candidate detector; `AecoPostMortem.Findings.ContradictionCheck` is
the caller that groups sessions by rule-set version before handing each version's deduplicated
statements in, and registers the run (`CheckId = "contradiction-check"`) on the "checks that found
nothing" surface FR-42 (S-37) already published ahead of this story.

The Monitor comparison's adjacency refusal (S-35, issue #43, FR-39) has also landed:
`RuleSetVersionAdjacency.RequireAdjacentPair` is the primitive `Findings.MonitorComparison.Compare`
scopes itself with before computing anything, the exact role `RuleSetVersionScope.
RequireSingleVersion`'s own remarks anticipated for it. It refuses a pair spanning different
repositories (`MixedRuleSetVersionException`, reused), a hash the repository never carried
(`UnknownRuleSetVersionException`, reused from `RulesInventory.cs`), or two versions with anything
sitting between them in chronological order (`NonAdjacentRuleSetVersionsException`, naming every
intervening version) — and returns the two full `RuleSetVersion` values on success, so a caller
never has to look either back up by hash.

### `TurnRecord`/`AbortedTurnOccurrence` carry an event id, and it is the only identity on them

`AbortedTurnCheck`'s two shapes each carry a `required string EventId` — the id of the event that
opened the turn — alongside the `TurnId` they already had. The distinction is load-bearing rather
than cosmetic, and both fields' doc comments say so: `TurnId` is a small counter the session itself
displays, which cycles and repeats *within* one session, so it can never tell two turns apart;
`EventId`, paired with `SessionId`, can. Every ordering tiebreak here and every key a caller builds
downstream comes from `EventId`.

Both are `required`, so a caller that has only a display counter cannot construct either shape at
all (CS9035) — the same structural-beats-conventional reasoning `HookFailureCounts` gives for its own
paired denominators. That is what forced the review this change wanted: adding the field broke every
existing construction site in this project's own tests, each of which had to state which value it
meant, instead of silently inheriting a plausible default.

Repo Rule 6 is untouched. `EventId` is an opaque label this project only ever groups, sorts and
copies — it is never parsed, never compared against a literal, and nothing here knows which event
type produced it. The vocabulary allowlist
(`RulesProjectNamesNothingTests.The_rules_project_uses_only_a_reviewed_vocabulary_in_its_literals`)
needed no new word, because this change adds no string literal at all; the provider-specific detail
(that this is the `assistant.turn_start` envelope's own `id`) is stated one layer out in
`AecoPostMortem.Findings`, where naming is permitted.

### The abort tiebreak is `EventId`, because the old one tie-broke on the colliding field

`AbortedTurnCheck.Run` orders each session's turns by `StartedAt` and previously broke ties on
`TurnId`. That is the one field two turns of a session are *most* likely to share — measured against
the live reference corpus, 1,903 of 2,384 real turn rows share their `(SessionId, TurnId)` pair with
another turn — so two turns sharing both a timestamp and a counter were left genuinely tied, and
their reported positions could swap between runs over the same input. A tiebreak that can fail to
break the tie is not a tiebreak; ordering by `EventId` restores the determinism PRD §3.8 requires and
the old comment already claimed. `Turns_sharing_the_same_started_at_break_the_tie_by_event_id` pins
it with a fixture whose two turns deliberately share a `TurnId`, so it is unorderable under the old
rule and deterministic under this one.

This is the same correction `AecoPostMortem.Findings/CLAUDE.md` records for `SessionTapeStep.StepId`
("the ordinal tiebreak is now over a genuinely unique key, not merely a deterministic one") — the
third appearance in this codebase of one root cause, after `Data.Execution.Turn`'s own primary key
and that Prompt-step id. See that file's own entry for the finding-side change and the corpus
measurements.
