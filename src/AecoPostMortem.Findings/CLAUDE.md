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
| `HookFailureFinding.cs` | FR-17 (issue #27): `HookFailureEvent` (one failed hook pair, plain input), `HookFailureFinding.Build` — orchestrates `Rules.HookFailureCheck` into `Finding`s and a `CheckRegistryEntry` |

## References

`Rules` and `Data` — it does the orchestration `Rules` deliberately cannot. `Rules` takes plain
inputs and returns results with no knowledge of storage or of what produced its inputs; `Findings`
is the project that reads through `Data`, feeds `Rules` its operands, and writes the results back.
That split is why the non-negotiable invariant in `AecoPostMortem.Rules/CLAUDE.md` holds: the
orchestrator can name tools and repositories, the checker never sees them.

The `Rules` reference is used starting with `HookFailureFinding` (issue #27): it calls
`HookFailureCheck.Evaluate` for the paired denominators and builds `Finding`s from the result. The
`Data` reference is still not used — `HookFailureFinding.Build` takes plain inputs
(`allSessionIds`, `sessionsWithToolCall`, `HookFailureEvent`s) rather than querying
`PostMortemContext` directly, because no code in this repository yet turns `raw_event` into the
`Hook`/`ToolCall` rows a real query would read; that ETL is a separate, not-yet-built story. The
caller that eventually does read through `Data` supplies this function's plain inputs from the
derived tables once that pipeline exists.

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

### `HookFailureEvent` is a plain input, not `AecoPostMortem.Data.Execution.Hook`

`Evidence.cs` says evidence is "quoted from the event that produced a finding" — the RAW event, not
the NORMALIZED projection that already dropped fields it didn't index. `Data.Execution.Hook` has no
error-text column, so `HookFailureFinding` takes its own `HookFailureEvent` (`SessionId`,
`HookName`, `Success`, `Error`) instead of widening the shared derived entity for one check's
evidence. `EvidenceItem.Field` values (`data.success`, `data.error`) name the JSON path a future RAW
reader would quote them from.

### The suggestion text is the only path to FR-17's paired denominators

`HookFailureFinding.BuildSuggestion` takes `HookFailureCounts` as a whole and is the only producer
of `Finding.Suggestion.Text` for this check — there is no overload or code path that renders
`OverAllSessions` or `OverSessionsWithToolCall` alone. That is what makes "neither figure appears
alone" (issue #27, Scenario 1) hold structurally rather than by the caller remembering to print
both.

### The finding is one per hook identity, and disappears once the hook is fixed

`FindingClassRegistry`'s recurrence key for a hook failure is "the hook identity", so
`HookFailureFinding.Build` groups failures by `HookName` and emits one `Finding` per group, each
carrying the same corpus-wide `HookFailureCounts`. When there are no failures, `Build` returns no
findings and a `CheckRegistryEntry` with `Status = Ran` and `FindingCount = 0` — a clean check, not
a refused one. That is what makes the finding disappear from the digest on its own the moment the
operator fixes the hook, per issue #27's edge case: intended behaviour, not a regression to guard
against.

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
one real check: `HookFailureFinding` (issue #27, FR-17, `CheckId = "hook-failure"`) —
`FindingClass.Waste`'s first detection logic. No check exists in `AecoPostMortem.Rules` yet to bind
a real `SuggestionTemplate.CheckId` to — `SuggestionWorkedExampleTests` exercises the suggestion
mechanism against a synthetic tool-choice check result standing in for the story that will supply a
real one. Other `Waste`-class checks (repeated file reads, issue #25; failed tool calls, issue #26)
are landing concurrently in sibling branches as their own self-contained files — nothing here
depends on them, and nothing they add should require touching `HookFailureFinding.cs`.
