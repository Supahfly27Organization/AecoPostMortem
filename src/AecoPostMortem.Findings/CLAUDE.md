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
| `RepeatedFileReadFindingCheck.cs` | FR-15's orchestration (issue #25): reads `ToolCall` through `Data`, decides which calls are reads (today: `ToolName == "view"` with a path — see its own remarks), calls `Rules.RepeatedReadCheck`, and folds the result into one `Finding` per path plus a `CheckRegistryEntry` |
| `FailedToolCallsFinding.cs` | FR-16 (S-14, issue #26): orchestrates `AecoPostMortem.Rules.FailedToolCallsCheck` into `Finding`s (`FindingClass.Waste`) and a `CheckRegistryEntry` |
| `PhaseChurnFinding.cs` | FR-19's orchestration (issue #29): takes `Rules.DeclaredIntent` operands directly (no `Data` reference — see below), orchestrates `Rules.PhaseChurnCheck` into one `Finding` per churning session (`FindingClass.Waste`, `Provenance.Derived`) plus a `CheckRegistryEntry`; `PhaseChurnFinding.Result` bundles both |

## References

`Rules` and `Data` — it does the orchestration `Rules` deliberately cannot. `Rules` takes plain
inputs and returns results with no knowledge of storage or of what produced its inputs; `Findings`
is the project that reads through `Data`, feeds `Rules` its operands, and writes the results back.
That split is why the non-negotiable invariant in `AecoPostMortem.Rules/CLAUDE.md` holds: the
orchestrator can name tools and repositories, the checker never sees them.

The `Rules` reference is used by `HookFailureFinding` (issue #27), which calls
`HookFailureCheck.Evaluate` for the paired denominators and builds `Finding`s from the result; by
`RepeatedFileReadFindingCheck` (issue #25), which reads `AecoPostMortem.Data.Execution.ToolCall` and
calls `AecoPostMortem.Rules.RepeatedReadCheck` — the first real use of both references; and by
`FailedToolCallsFinding` (issue #26), which calls `FailedToolCallsCheck` and shapes its
`ToolFailureRate` results into `Finding`s. The `Data` reference is still not used by
`HookFailureFinding` or `FailedToolCallsFinding`: `HookFailureFinding.Build` takes plain inputs
(`allSessionIds`, `sessionsWithToolCall`, `HookFailureEvent`s) rather than querying
`PostMortemContext` directly, because no code in this repository yet turns `raw_event` into the
`Hook`/`ToolCall` rows a real query would read — that ETL is a separate, not-yet-built story.
`FailedToolCallsFinding`'s tests likewise build `ToolCallOutcome` operands directly rather than
reading through `PostMortemContext`; the query that resolves `ToolCall` rows into that plain shape
for this check is later work (S-40). The caller that eventually does read through `Data` for these
two supplies their plain inputs from the derived tables once that pipeline exists.

`PhaseChurnFinding` (issue #29) takes `Rules.DeclaredIntent` operands directly, for the same reason:
`Data.Execution.ToolCall` carries no field for `report_intent`'s `intent` argument (only `Path`,
added for reads), and `ToolArguments` is not yet wired into the `RawEvent`-to-`ToolCall` pipeline
(`AecoPostMortem.Ingestion/CLAUDE.md`) — so there is no real query today that could resolve a
`report_intent` call's phase label. `DeclaredIntent` already is the plain shape a future caller would
supply once that ETL exists, the same way `ToolCallOutcome` is reused directly by
`FailedToolCallsFinding` rather than wrapped in a second Findings-owned type.

`AecoPostMortem.Ingestion` references this project the other way — for `CheckRegistryEntry` only —
so `MalformedLineCheck` can register FR-6's check without `Findings` needing to know anything about
ingestion. See `AecoPostMortem.Ingestion/CLAUDE.md`.

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

### `RepeatedFileReadFindingCheck`'s recurrence key is the path, not `(session, path)`

FR-57 names the path as this Waste finding's recurrence key. The same path read 4+ times in two
different sessions is therefore **one** `Finding`, with `Recurrence.Occurrences` carrying one
`RecurrenceOccurrence` per session — matching Scenario 2 of issue #25 ("it states how many
sessions it touched"). Per-session counts don't fit any existing typed field, so they ride in
`Evidence` as `EvidenceItem { Field = $"read_count:{sessionId}", Value = "<count>" }`, one per
occurrence, alongside a single `data.path` item. This is a deliberate, documented use of
`Evidence`'s free-form shape rather than a new typed field on `Finding` or `RecurrenceOccurrence` —
revisit if a second check needs a per-occurrence number and the pattern should become a real field.

### `RepeatedFileReadFindingCheck.Population` counts every session considered, not just the ones with a valid read

`CheckRegistryEntry.Population` is the distinct session count across **all** `ToolCall`s passed
in, before the read-event filter runs — so a session whose only tool call is missing a path (the
parser-defect edge case in issue #25) still counts as considered, it just contributes no read
events. This matches `CheckRegistryEntry`'s own doc comment: population is "the candidate set the
check considered", defined whether or not the check goes on to find anything.

### A Waste finding carries its rate in Evidence, never in Resolution

`Resolution` is FR-33's layer-used-per-operand figure, scoped to adherence findings
(`RuleAdherenceToolChoice` / `RuleAdherenceWrittenContent`) — `FailedToolCallsFinding` leaves it
null. The failure rate's counts (`failures`, `calls`, `percentage`, `sessionCount`) are quoted as
`EvidenceItem`s instead, all four built together in one place
(`FailedToolCallsFinding.ToFinding`) so a rendered finding can never show the percentage without
the counts that produced it, or the rate without the session count that contextualizes it (issue
#26, both scenarios). The structural guarantee itself — that a percentage cannot be constructed
without its counts — lives one level down, on `AecoPostMortem.Rules.FailureRate`.

### `PhaseChurnFinding`'s recurrence key is the session id, unlike every other Waste check

The other three Waste checks each recur around a shared sub-object two sessions can both touch — a
path, a hook identity, a tool identity. Phase churn has no such object: it is a whole-session
aggregate over that session's own declared intents, so there is nothing for two different sessions'
churn to be "the same finding" *about*. `PhaseChurnFinding.ToFinding` therefore keys `Recurrence` on
the session id itself, so every churning session is its own `Finding` with exactly one
`RecurrenceOccurrence` — itself. This is a deliberate reading of FR-57 for a check shape the other
three don't fit, not an oversight of "a session is where `Recurrence` says the finding recurred, not
where the finding lives" (this file's own words, above): that guidance is about `Finding` carrying no
bare `SessionId` field, and still holds — `PhaseChurnFinding` never puts a session id anywhere but
inside `Recurrence`.

### Only sessions that actually churned become a finding

`PhaseChurnCheck.Run` (Rules) reports every session that declared at least one intent, churned or
not, the same way `FailedToolCallsCheck.Run` reports every tool observed including clean ones.
`PhaseChurnFinding.Run` filters to `Returns > 0` before building a `Finding`, mirroring
`FailedToolCallsFinding`'s `Failures > 0` filter — deciding what is worth surfacing as a finding is
this project's call, not `Rules`'s. A session with intents but zero returns is therefore silent, the
same as a session with no intents at all (issue #29's named edge case), even though `Rules` can tell
the two states apart if a future caller needs to.

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
four real checks: `HookFailureFinding` (issue #27, FR-17, `CheckId = "hook-failure"`),
`RepeatedFileReadFindingCheck` (issue #25, FR-15), `FailedToolCallsFinding`
(`CheckId = "failed-tool-calls"`, FR-16, issue #26) and `PhaseChurnFinding`
(`CheckId = "phase-churn"`, FR-19, issue #29) — all `FindingClass.Waste` detection logic. A fifth
check registers a real id — `malformed-line`, built by
`AecoPostMortem.Ingestion.MalformedLineCheck` from FR-6's per-file read stats (issue #3 / S-02) —
but nothing in this project constructs it. No check exists in `AecoPostMortem.Rules` yet to bind a
real `SuggestionTemplate.CheckId` to — `SuggestionWorkedExampleTests` exercises the suggestion
mechanism against a synthetic tool-choice check result standing in for the story that will supply a
real one. Each of the four Waste-class checks is self-contained, but `FindingClassRegistry`'s
Waste `RecurrenceKeyDescription` is shared prose more than one touches, so expect it to need
merging by hand.
