# AecoPostMortem.Findings

The four finding classes, provenance, recurrence, the Monitor comparison, suggestions.

## Structure

| File | What it holds |
|---|---|
| `Finding.cs` | `FindingClass`, `Provenance`, `OperatorResponse`, and the `Finding` record itself |
| `FindingClassRegistry.cs` | the four finding classes, each declaring its recurrence key (FR-57) |
| `Evidence.cs` | `EvidenceItem` — one quoted event field |
| `Recurrence.cs` | `Recurrence`, `RecurrenceOccurrence` — FR-57's version-independent identity |
| `Resolution.cs` | FR-33's layer-used-per-operand and call count, carried where an adherence figure has one |
| `Suggestion.cs` | FR-56's deterministic template text |
| `SuggestionTemplate.cs` | FR-56's template bound to a check shape — `CheckId` plus a `{Placeholder}` `Format` string |
| `SuggestionRenderer.cs` | FR-56's rendering mechanism — pure substitution of `SuggestionTemplate.Format` from a finding's own `EvidenceItem`s and `Resolution`, nothing else |
| `CheckRegistry.cs` | `CheckRunStatus`, `CheckRegistryEntry`, `CheckRegistry` — every check's run status and population, whether or not it fired |
| `FailedToolCallsFinding.cs` | FR-16 (S-14, issue #26): orchestrates `AecoPostMortem.Rules.FailedToolCallsCheck` into `Finding`s (`FindingClass.Waste`) and a `CheckRegistryEntry` |

## References

`Rules` and `Data` — it does the orchestration `Rules` deliberately cannot. `Rules` takes plain
inputs and returns results with no knowledge of storage or of what produced its inputs; `Findings`
is the project that reads through `Data`, feeds `Rules` its operands, and writes the results back.
That split is why the non-negotiable invariant in `AecoPostMortem.Rules/CLAUDE.md` holds: the
orchestrator can name tools and repositories, the checker never sees them.

The `Rules` reference is now used: `FailedToolCallsFinding` calls `FailedToolCallsCheck` and shapes
its `ToolFailureRate` results into `Finding`s. The `Data` reference is still unused — this check's
tests build `ToolCallOutcome` operands directly rather than reading through `PostMortemContext`;
the query that resolves `ToolCall` rows into that plain shape is later work (S-40).

## Non-obvious decisions

### The finding record has no `Id` and no `SessionId`

The finding contract names seven fields, and only those seven: class, provenance, evidence,
recurrence, the resolution used where one applies, its suggestion, and the operator's response. A
finding's identity is `(class, class-specific key)` per FR-57, not a row id or a session — a
session is where `Recurrence` says the finding recurred, not where the finding lives.

### `Provenance` fails construction by being `required`, not by validating

`Finding.Provenance` has no runtime check for presence — the C# compiler already refuses to
compile an object initializer that omits a `required` member.
`FindingTests.Provenance_is_a_required_member` proves the property still carries
`RequiredMemberAttribute` rather than re-deriving the guarantee at run time, the same reasoning
`AecoPostMortem.Rules/CLAUDE.md` gives for its own invariant: structural beats conventional
because there is no commit at which it can be skipped by accident.

### A Waste finding carries its rate in Evidence, never in Resolution

`Resolution` is FR-33's layer-used-per-operand figure, scoped to adherence findings
(`RuleAdherenceToolChoice` / `RuleAdherenceWrittenContent`) — `FailedToolCallsFinding` leaves it
null. The failure rate's counts (`failures`, `calls`, `percentage`, `sessionCount`) are quoted as
`EvidenceItem`s instead, all four built together in one place
(`FailedToolCallsFinding.ToFinding`) so a rendered finding can never show the percentage without
the counts that produced it, or the rate without the session count that contextualizes it (issue
#26, both scenarios). The structural guarantee itself — that a percentage cannot be constructed
without its counts — lives one level down, on `AecoPostMortem.Rules.FailureRate`.

### A refused check and a clean check are distinguished by null, not by a third status

`CheckRegistryEntry.FindingCount` is `null` when `Status` is `Refused` and a real integer —
including `0` — when `Status` is `Ran`. `CheckRunStatus` has exactly two values because the
acceptance criteria name exactly two states to distinguish; a third "never attempted" status was
considered and rejected as unmotivated by anything in FR-37 or FR-42.

### Suggestions are template substitution, not generation — and the template lives here, not in `Rules`

FR-56 forbids a model call (§3.8), so `SuggestionRenderer.Render` is pure substitution:
`SuggestionTemplate.Format` names its operands as `{PlaceholderName}` tokens, and each token
resolves against the same two things a `Finding` already carries — its `Evidence` (matched by
`EvidenceItem.Field`) and its `Resolution` (two reserved placeholders, `{OperandLayer}` and
`{CallCount}`). Rendering the same template against the same evidence and resolution is
deterministic because those are its only inputs; there is nothing else a template could read.

Several `EvidenceItem`s sharing one `Field` render as one joined list — `FormatOperandList`
generalises FR-35's worked example, *"name `rg`, `glob` and `view`"* — one value alone, two joined
with "and", three or more with an Oxford comma before the final "and".

A placeholder that cannot be bound to a concrete operand makes `Render` return `null` for the whole
suggestion, never a partially-filled one: FR-56's edge case says a vague suggestion poisons the
§5.4 rejection-rate signal, so this is a fail-closed substitution, not fail-open. The same
`null`-means-absent shape as `Finding.Suggestion` itself: no template ships no suggestion, and no
resolvable operand does the same.

`SuggestionTemplate` is bound to a check the same abstract way `CheckRegistryEntry.CheckId` is — a
plain string, because the check-shape catalogue in `AecoPostMortem.Rules` is still empty and
open-ended. This project (`Findings`), not `Rules`, is where a template may say `rg`, `glob` or any
other tool name: Repo Rule 6 restricts `src/AecoPostMortem.Rules/` only, and `SuggestionTemplate` /
`SuggestionRenderer` live in the orchestration layer that is allowed to name tools.

`SuggestionRenderer` is a `static class` on purpose: a `static class` cannot hold an instance field,
so there is structurally nowhere to inject a model client or a clock — proved by
`SuggestionRendererStructureTests` reflecting over the type's shape (no instance state, no mutable
static field, every public method signature drawn from an explicit allowlist of already-resolved
data types) rather than merely asserting the behaviour.

## Status

The finding record, check-registry shapes, and FR-56's generic suggestion-template mechanism, plus
one detection: `FailedToolCallsFinding` (`CheckId = "failed-tool-calls"`, FR-16). No check exists in
`AecoPostMortem.Rules` yet to bind a real `SuggestionTemplate.CheckId` to —
`SuggestionWorkedExampleTests` exercises the suggestion mechanism against a synthetic tool-choice
check result standing in for the story that will supply a real one.
`FailedToolCallsFinding` is a Waste-class check landing alongside sibling Waste checks for repeated
file reads (issue #25) and hook failures (issue #27) in separate branches — each is a self-contained
file, but `FindingClassRegistry`'s Waste `RecurrenceKeyDescription` is shared prose all three touch,
so expect it to need merging by hand.
