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
| `Digest.cs` | FR-41 part 1 (issue #44, S-36): `MastheadCounters`, `RuleCoverageStatus`, `DigestState`, `Masthead`, `ProcessDigest` — the corpus masthead and the findings ranking; FR-41 part 2 (issue #45, S-54): `RepositoryScope`, carried on `Masthead`. FR-48 (issue #52, S-42) split `ProcessDigest.RankedFindings` into that (Observed/Derived only) plus `InferredFindings`, its own unranked section |
| `ProvenanceLabel.cs` | FR-48 (issue #52, S-42): `ProvenanceLabel.For(Provenance)` — the fixed, textually distinct sentence per provenance level, served on every `FindingEnvelope` so the distinguishing signal is in the finding's own words, not only its styling |
| `HookFailureFinding.cs` | FR-17 (issue #27): `HookFailureEvent` (one failed hook pair, plain input), `HookFailureFinding.Build` — orchestrates `Rules.HookFailureCheck` into `Finding`s and a `CheckRegistryEntry` |
| `RepeatedFileReadFindingCheck.cs` | FR-15's orchestration (issue #25): reads `ToolCall` through `Data`, decides which calls are reads (today: `ToolName == "view"` with a path — see its own remarks), calls `Rules.RepeatedReadCheck`, and folds the result into one `Finding` per path plus a `CheckRegistryEntry` |
| `FailedToolCallsFinding.cs` | FR-16 (S-14, issue #26): orchestrates `AecoPostMortem.Rules.FailedToolCallsCheck` into `Finding`s (`FindingClass.Waste`) and a `CheckRegistryEntry` |
| `InterruptionLoadFinding.cs` | FR-20 (issue #30): reads `Permission` and `ToolCall` through `Data`, decides which tool calls are questions (`ToolName == "ask_user"`), calls `Rules.InterruptionLoadCheck`, and folds the result into one `FindingClass.Waste` finding plus a `CheckRegistryEntry` |
| `SessionTokenFigures.cs` | FR-24 (S-11, issue #20): reads `Session`'s own token fields into the masthead's token-totals contract — not a `Finding`, no rule adherence involved — closed to `Observed` and `SessionTotalsNotRecorded` |
| `AbortedTurnFinding.cs` | FR-18 (S-16, issue #28): reads `AecoPostMortem.Data.Execution.Turn` rows, calls `AecoPostMortem.Rules.AbortedTurnCheck`, and writes one `Finding` per aborted turn — never grouped — plus a `CheckRegistryEntry` |
| `PhaseChurnFinding.cs` | FR-19's orchestration (issue #29): takes `Rules.DeclaredIntent` operands directly (no `Data` reference — see below), orchestrates `Rules.PhaseChurnCheck` into one `Finding` per churning session (`FindingClass.Waste`, `Provenance.Derived`) plus a `CheckRegistryEntry`; `PhaseChurnFinding.Result` bundles both |
| `OperatorResponseLog.cs` | FR-45 (issue #49, S-39): `OperatorResponseRecord` (one recorded response against a finding identity and its provenance level) and `OperatorResponseLog` — the append-only history, `CurrentResponses()` (latest per finding), and `Apply(Finding)` to populate `Finding.OperatorResponse` |
| `Guardrail.cs` | PRD §5.4 (issue #49, S-39, FR-45): `Guardrail.Compute(OperatorResponseLog)` — the rejection share and the share of adjudicated (accepted-or-rejected) findings that were `Provenance.Inferred` |
| `ToolFailureClusterFinding.cs` | FR-46 (S-40, issue #51): `MandatedTool` (a tool identity paired with the `Rules.RuleStatement` that mandates it, plain input); `ToolFailureClusterFinding.Run` reuses `Rules.FailedToolCallsCheck` (S-14) rather than recomputing rates, and turns each rate into one `FindingClass.MissingCapability` finding (`Provenance.Inferred`) plus a `CheckRegistryEntry`; `ToolFailureClusterResult` bundles both |
| `SessionRecording.cs` | FR-21, part 1 of 3 (S-08, issue #15): `SessionTapeStepKind`, `SessionTapeStep`, `SessionTape`, `SessionMasthead`, `SessionRecording` — the Flight Recorder's masthead and time-ordered tape, built from plain `Data.Execution` rows, not a `Finding` |
| `ContradictionCheck.cs` | FR-43 (S-38, issue #47): orchestrates `Rules.ContradictionCheck` — groups sessions by rule-set version before calling in (never comparing statements from two versions), wraps the result in `ContradictionCheck.Result` (`Candidates`, a required `Provenance` always `Inferred`, and the `CheckRegistryEntry`), `CheckId = "contradiction-check"`. Not a `Finding`-producing check — see below |
| `SubagentAttribution.cs` | FR-49 (S-43, issue #53): `SubagentRuleDisplay` — closed to `Nothing` (the default) or an explicit, caller-stated `AssumedInherited` (always `Provenance.Inferred`, computed not settable) — and `SubagentObservedContext.From(Agent, taskPrompt, sessionSkills)`, the subagent's own spawn description, task prompt and skill invocations, always `Provenance.Observed`. Neither type infers or guesses a rule set; not a `Finding` |

## References

`Rules` and `Data` — it does the orchestration `Rules` deliberately cannot. `Rules` takes plain
inputs and returns results with no knowledge of storage or of what produced its inputs; `Findings`
is the project that reads through `Data`, feeds `Rules` its operands, and writes the results back.
That split is why the non-negotiable invariant in `AecoPostMortem.Rules/CLAUDE.md` holds: the
orchestrator can name tools and repositories, the checker never sees them.

The `Rules` reference is used by `HookFailureFinding` (issue #27), which calls
`HookFailureCheck.Evaluate` for the paired denominators and builds `Finding`s from the result; by
`RepeatedFileReadFindingCheck` (issue #25), which reads `AecoPostMortem.Data.Execution.ToolCall` and
calls `AecoPostMortem.Rules.RepeatedReadCheck` — the first real use of both references; by
`FailedToolCallsFinding` (issue #26), which calls `FailedToolCallsCheck` and shapes its
`ToolFailureRate` results into `Finding`s; by `InterruptionLoadFinding` (issue #30), which reads
`AecoPostMortem.Data.Execution.Permission` and `ToolCall` and calls
`AecoPostMortem.Rules.InterruptionLoadCheck`; and by `AbortedTurnFinding` (issue #28), which reads
`AecoPostMortem.Data.Execution.Turn` and calls `AecoPostMortem.Rules.AbortedTurnCheck` — the second
check, after `RepeatedFileReadFindingCheck`, to read a real derived entity rather than take plain
inputs, because `Turn.AbortReason` and `Turn.Outcome` already carry everything FR-18 needs. The
`Data` reference is still not used by `HookFailureFinding` or `FailedToolCallsFinding`:
`HookFailureFinding.Build` takes plain inputs (`allSessionIds`, `sessionsWithToolCall`,
`HookFailureEvent`s) rather than querying `PostMortemContext` directly, because no code in this
repository yet turns `raw_event` into the `Hook`/`ToolCall` rows a real query would read — that ETL
is a separate, not-yet-built story. `FailedToolCallsFinding`'s tests likewise build `ToolCallOutcome`
operands directly rather than reading through `PostMortemContext`; the query that resolves
`ToolCall` rows into that plain shape for this check is later work (S-40). The caller that
eventually does read through `Data` for these two supplies their plain inputs from the derived
tables once that pipeline exists.

`SessionTokenFigures` (issue #20) also uses the `Data` reference directly — `From` takes a
`Session` and reads its own nullable token fields, no `Rules` call involved: there is no rate or
threshold to check here, only a value already computed by whatever populates `Session` (S-49) and a
presence test.

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

`ContradictionCheck` (issue #47, S-38) is the fourth caller of `Rules`, and the first whose `Rules`
call takes `Rules.SessionRuleSet`s rather than a plain per-call/per-session operand: FR-43's second
scenario needs the rule-set-version grouping `Rules.RuleSetVersioning`/`RuleSetVersionScope` already
established the identity for (repository + `RuleSetVersionHasher.ComputeHash`), and
`Rules.RuleStatementDeduplication.Deduplicate` for each version's own in-force statement set, before
`Rules.ContradictionCheck.Run` ever sees them.

`ToolFailureClusterFinding` (issue #51, S-40) is the second caller of `FailedToolCallsCheck`,
alongside `FailedToolCallsFinding` — both read the identical `ToolFailureRate` result and diverge
only in what they build from it (`FindingClass.Waste`/`Provenance.Derived` vs.
`FindingClass.MissingCapability`/`Provenance.Inferred`), which is why this story's own edge case
says it does not recompute failure rates from scratch. It also uses `Rules.RuleStatement` (FR-26,
S-19) as the shape a "mandated tool" names the rule that mandates it — no new type duplicates what
`RuleStatement` already carries (`SourceFile`, `Text`).

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

### `SessionTokenFigures` is not a `Finding`, deliberately

FR-24's token totals are a masthead display fact, not evidence of rule adherence or waste, so they
carry no `FindingClass`, `Provenance`, `Evidence` or `Recurrence` — the `Finding` contract's seven
fields don't fit and aren't forced to. Instead `SessionTokenFigures` is its own closed union, built
the same way `Api.SuggestionEnvelope` builds "no suggestion" as an explicit state rather than a
nullable field: `NotRecorded` is the one value for "this session's shutdown event carried no token
metrics", and `Observed` is the only shape carrying `InputTokens`/`OutputTokens`, both `required`.
`From(Session)` treats a session carrying only one of the pair (a case reflection can't
distinguish from a bug without a name) the same as a session carrying neither: half a pair of
totals is a missing pair, not a partial one, because both figures come from the same shutdown
event. No property on either shape may be named after cost, price, currency or spend —
`SessionTokenFiguresTests.No_shape_carries_a_cost_or_currency_field` reflects over both shapes to
prove there is nothing on this type a masthead could accidentally render as a price (Scenario 3);
FR-24 forbids the figure entirely because Copilot prices in premium requests and nano-AIU and no
local file states a conversion rate, so apportioning a total into a price is Inferred and this
product does not compute one anywhere. `SessionRecording.Build` (S-08, below) is the first caller of
`From`, wrapped by `Api.SessionTokenFiguresEnvelope` as `SessionMasthead.ContextSize` — "context
size at end" reuses this shape verbatim rather than inventing a second token-totals contract.

### `SessionRecording` takes plain `Data.Execution` rows, the same reason `AbortedTurnFinding` does

FR-21's masthead and tape (S-08, issue #15) is not a `Finding` — no rule adherence or waste is being
judged, so `SessionRecording.Build` takes a `Session` plus `Turn`/`ToolCall`/`Agent`/`Skill`/`Hook`
lists as plain inputs and returns `SessionMasthead`/`SessionTape` directly, never reading through
`PostMortemContext` itself. This project cannot query the store on its own by design (see
`References`, above); the caller — `AecoPostMortem.Api`'s session endpoint — is the one that reads
`Data.Execution` and decides where its rows come from. That matters here specifically because
nothing in this repository yet *writes* those rows at ingest time
(`AecoPostMortem.Ingestion/CLAUDE.md`, "not yet wired into the store"): the derived tables exist and
are queryable (`DerivedSchema.EnsureCurrent` creates them on open), they are simply empty until a
later story's ETL populates them, so `Api.ApiHost.GetSession` reads them today exactly as it would
once that writer exists — no separate code path.

`SessionTapeStepKind.Prompt` stands in for "the user's prompt that started a turn": Copilot's event
log carries no separate prompt entity, and `Turn` itself carries no message text
(`AecoPostMortem.Data/CLAUDE.md` — "messages are read from RAW"), so a prompt step's `Label` is the
turn's own `Outcome` (`"Completed"`/`"Aborted"`/`"Unfinished"`) rather than a transcript excerpt this
layer cannot see. `SessionTapeStepKind.McpCall` is a `ToolCall` whose `McpServerName` is not null,
kept a distinct tape-step kind from a plain `ToolCall` rather than folded into it, matching the
Gherkin's own five-way step vocabulary ("hooks, prompts, skills, tool calls and MCP calls").
`SessionMasthead.ModelCount` reuses `Session.ModelCount` verbatim rather than deriving a second
"models" figure from `Agent.Model`: NORMALIZED carries no main-thread model field today, only a
subagent-scoped one, so the count already summed into `ContextSize`'s totals is the one figure this
layer can state honestly as "models" — a documented scope note, not an oversight.

Steps are ordered by wall-clock timestamp (Scenario 2), ties broken by step kind then the step's own
id (`StringComparer.Ordinal`) for the same determinism reasoning `AbortedTurnCheck` gives its own
tie-break (PRD §3.8) — two entities can share a timestamp, never a `(kind, id)` pair within one
session. `SessionTape.HasSteps` is computed from `Steps.Count`, never a second stored flag: an empty
list already states Scenario 3's "no steps were recorded" on its own.

### `ProcessDigest.Build` takes plain, already-resolved inputs — the same reason it can prove it never scans

S-36's own edge case says the masthead's totals are the one place this surface could scan the corpus,
and it must not (counting a million rows measured 126 ms on SQLite and 118 ms on Postgres —
`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md`). `MastheadCounters`
is therefore a plain input record — "the stored counters maintained at ingest", not a live count —
the same reasoning `HookFailureFinding.Build` and `FailedToolCallsFinding` give for taking plain
inputs instead of reading through `Data` directly: no code in this repository yet writes those
counters at ingest time, so the caller (a later story) supplies them. `ProcessDigestStructureTests`
proves the "no scan" guarantee structurally, the same way `SuggestionRendererStructureTests` proves
"no model call": an allowlist of every type `ProcessDigest`'s public surface may mention has no room
for an `IQueryable` or a `DbContext`, so a method that cannot accept a live data source cannot issue
a query when it runs.

### Two designed "nothing to show" states, and they do not collapse into each other

`DigestState.NotYetAnalyzed` (no check has ever run — reusing `CheckRegistryEntry`'s own
`Ran`/`Refused` distinction, issue #23 Scenario 5, at the digest level) and `DigestState.Incomplete`
(`MastheadCounters.IngestInProgress`) answer different questions — "has analysis ever happened" versus
"is analysis still running right now." `ProcessDigest.Build` checks `IngestInProgress` first, so a
corpus that is both mid-ingest and has no check registered yet still reads `Incomplete`: the more
urgent, more specific claim wins rather than the two states being merged or left to declare in
whichever order a caller happens to check them.

### `RepositoryScope` is another already-resolved plain input, not a live filter

FR-41 part 2 (issue #45, S-54) and PRD Part 8 Q5: the digest shows one repository at a time,
selectable, because ranking findings across repositories would mix rule sets that were never in
force together (FR-28's reasoning applied to this surface). `RepositoryScope` follows the exact
shape `MastheadCounters` already established for "already resolved, not computed here" — the caller
of `ProcessDigest.Build` has already filtered `findings` to one repository before calling it;
`RepositoryScope` only states which repository that was (`SelectedRepository`) and which others
exist to select (`AvailableRepositories`), for a UI selector to render. It does not itself re-filter
`RankedFindings` when more than one repository is available — that is the seam this story's own edge
case names for a later cross-repository story, not a filtering mechanism to build here.
`ProcessDigestStructureTests`'s allowlist includes `RepositoryScope` for the same reason it already
includes `MastheadCounters`: it is a plain, already-resolved data type, so admitting it does not
weaken the "no live query" guarantee that test proves.

### `InferredFindings` is a separate, deliberately unranked field — not a filter a caller applies

FR-48 (issue #52, S-42) says an Inferred finding is "never ranked beside" an Observed or Derived
one — `ProcessDigest.Build` partitions its input by `Provenance` itself (`RankedFindings` excludes
`Provenance.Inferred`; `InferredFindings` is exactly that subset) rather than publishing one mixed
list and trusting every caller to filter it before ranking. `InferredFindings` also does not run
through `OrderByDescending(SessionsAffected)`: `SessionsAffected` is the "how many sessions this
touched" figure the PRD names as measured, and applying it to a hypothesis would dress the
hypothesis up with the same measured-looking number that ranks Observed and Derived findings — the
exact "guess laundered into a process change" the PRD's risk table (§3.8) names for this FR.
`InferredFindings` therefore preserves whatever order its caller supplied, same as `RankedFindings`
does for ties (`A_tie_in_sessions_affected_preserves_input_order_rather_than_reordering_arbitrarily`).

**Gap closed by S-54, issue #45:** FR-48's Scenario 2 says the three levels must be distinguishable
"without reading the label," which most naturally asks for a non-textual discriminator too (icon,
shape, position) — not only the wording `ProvenanceLabel` supplies. This story stopped at the wire
contract (see `ProvenanceLabel` below and `Api/CLAUDE.md`'s matching note): `Provenance` is served
so a client can branch a shape/icon on it, but defined no such discriminator itself, because no
rendering surface existed yet to define it for. `web/src/digest/ProvenanceBadge.tsx` (S-54) is that
discriminator: a `data-provenance` attribute drives a distinct background/text colour per level in
`ProvenanceBadge.css`, alongside — not instead of — the badge's own text label, so the colour is a
second signal on top of the word rather than the only one (per this story's own accessibility
edge case: distinguishable without colour alone).

### `ProvenanceLabel` is text rendering of the existing enum, not a new domain concept

FR-48's second scenario says the three provenance levels must be distinguishable without reading
the label, and its edge case adds that an Inferred finding must read as a hypothesis in its own
text, "since the styling does not survive being quoted elsewhere" — a CSS class or an icon
disappears the moment a finding is copied into a report; wording doesn't. `ProvenanceLabel.For`
is a `static` lookup, one fixed sentence per `Provenance` value, deliberately not a new field on
`Finding` itself — nothing it returns isn't already derivable from the enum, only its human-readable
form is new. It is served on every `FindingEnvelope` (`ProvenanceLabel`, `required`, alongside the
raw `Provenance` enum) rather than left for a client to derive, so the distinguishing text travels
with the finding on the wire. Only the Inferred sentence contains the word "hypothesis" — the other
two do not — which is what makes `Only_the_inferred_label_reads_as_a_hypothesis` a meaningful
assertion rather than a coincidence of wording.

### `RuleCoverageStatus` has exactly one member today, on purpose

FR-26 and FR-40 (rule extraction, the coverage bar's population) are Release 2. Rather than a
nullable or boolean stand-in for "not yet analysed" that a Release-2 figure would later have to
un-collide from a real zero, the enum simply has no other case yet — the same reasoning
`FindingEnvelope`'s closed shapes give for making an unrepresentable state a compile-time fact
instead of a runtime one. A Release-2 value is added here when FR-26/FR-40 land; nothing about
`Masthead` needs to change to admit it.

### `AbortedTurnFinding`'s recurrence key is the turn itself, not the abort reason

FR-57 names a class-specific key, but an abort has no recurring *cause* the way a hook or a tool
does — `AbortedTurnFinding.ToFinding` keys `Recurrence` on `$"{SessionId}:{TurnId}"`, not
`AbortedTurnOccurrence.TurnId` alone: `Turn`'s own natural key is the composite
`(SessionId, TurnId)` (`PostMortemContext.MapTurn`), and a bare `TurnId` is not guaranteed unique
across sessions — two unrelated aborts that happened to share one would otherwise collide into the
same `Recurrence.Key`, which `Recurrence.cs` documents as impossible ("no constructor that could
produce a second `Finding` for the same key"). Two aborts that happen to share reason text
(`"user_interrupt"`, say) in two different sessions still stay two distinct findings, each with
exactly one `RecurrenceOccurrence` — grouping by reason instead would let a measured 9-across-8
volume collapse into fewer, more heavily "recurring" findings than the corpus actually shows, the
inflation issue #28's edge case warns against. `Provenance.Derived` rather than `Observed` for the
same reason `RepeatedFileReadFindingCheck` gives: "position in the session" comes from ordering
every turn in the session (`AbortedTurnCheck.Run`), not from a single event's own field, even
though the abort reason itself is a bare observed value.

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

### A question is a completed `ask_user` tool call; a permission prompt is its own entity

`InterruptionLoadFinding` (issue #30, FR-20) is the first check to draw its two operand kinds from
different `Data` entities: permission prompts come straight off `Permission` (already a distinct
row per prompt, no tool name involved), while questions have to be filtered out of `ToolCall` by
name the same way `RepeatedFileReadFindingCheck` filters `view` — `QuestionToolName = "ask_user"`
is the one place in this project allowed to name it (Repo Rule 6 binds `AecoPostMortem.Rules`
only). One `Finding` covers a whole analysis run rather than one per hook/path/tool identity,
because there is no natural per-entity grouping for "how many times was the operator interrupted" —
its `Recurrence.Key` is the fixed string `"interruption-load"`.

### The result-kind breakdown groups by whatever the field says, never by a hardcoded denial string

`InterruptionLoadFinding.PermissionOutcomeBreakdown` groups permission prompts by their literal
`ResultKind` value (`ResultKind ?? "no outcome recorded"`) and quotes each group's count as
`EvidenceItem { Field = "result_kind:<value>" }`. It never compares `ResultKind` against a literal
like `"denied"`: this project does not know Copilot's exact enum values, and matching against a
guessed string would turn FR-20's "Observed, not string-matched" claim into exactly the string
match it is contrasted against. This is also what makes the edge case hold without special-casing
it — an unresolved prompt (`ResultKind` is `null`) renders as its own literal group,
`"no outcome recorded"`, and can never be merged into a resolved outcome's count by construction.

### `OperatorResponseLog` is append-only, and the guardrail reads its current state, not its full history

`Finding.OperatorResponse` (S-44, issue #23) is the field that already exists for "the operator's
response" — this story does not add a second, competing response field. What it adds is the piece
that field alone cannot express: FR-45's edge case says changing a verdict later must not lose the
earlier one, and a single mutable `OperatorResponse` property has no way to keep both. `Finding`
itself is also re-derived from RAW on every run (Repo Rule 4) and carries no id, so there is nowhere
on the domain type to persist a history against even if the field weren't overwritable. The response
history therefore lives beside `Finding`, keyed by its own `(Class, RecurrenceKey)` identity
(FR-57): `OperatorResponseLog.Record` only ever appends to `Entries`, `CurrentResponses()` reduces
that history to the latest entry per finding identity, and `Apply(Finding)` is what makes
`Finding.OperatorResponse` "meaningfully populated" — it copies the current response onto a finding,
leaving the field's own default (`Ignored`) alone when the log has no entry for that identity.

`OperatorResponseRecord` captures `Provenance` at the moment of recording rather than reading it back
off a `Finding` later — Scenario 1 of issue #49 says the outcome is stored "against the finding **and
its provenance level**", so the level travels with the response, not with a later re-derivation of
the finding that produced it. `RecordedAt` is a caller-supplied `DateTimeOffset`, not read from a
clock inside this type, for the same determinism reason `SuggestionRenderer` reads no clock — the
log's ordering has to be reproducible from its own data, not from when the code happened to run.

`Guardrail.Compute` takes the whole `OperatorResponseLog` and reduces it through
`CurrentResponses()` before counting — never the raw `Entries` — so a finding whose verdict flipped
from Rejected to Accepted counts once, as Accepted, toward both figures. Counting every historical
entry instead would let one operator's indecision on one finding inflate the sample the same way a
repeated tool-name string match would inflate `FailedToolCallsFinding`'s rate; the guardrail is
about current judgment, not edit history. "Adjudicated" (PRD §5.4's own word) means
`Accepted`-or-`Rejected`; `Ignored` — the default for a finding nobody has acted on — is excluded
from both shares, and both `RejectionShare` and `InferredShare` are `null`, not `0`, when
`AdjudicatedCount` is zero, matching this project's existing rule that a percentage never appears
without the count that produced it. `Guardrail.MinimumSampleTarget` (20, PRD §5.4) is carried as a
fact on the type but not enforced by `Compute` — whether to actually *read* a share below that
sample is a rendering-layer decision no story has built yet, the same way `RuleCoverageStatus`
carries only the state Release 1 can populate and leaves the decision about a later state to the
story that adds it.

No persistence or CLI/API surface is wired to `OperatorResponseLog` yet — like `SessionTokenFigures`
and `ProcessDigest.Build`, this story publishes the contract the operator-facing "accept/reject/
ignore" action and the guardrail's rendering will build against; nothing in this repository yet
writes an `OperatorResponseRecord` from a real operator action or persists `OperatorResponseLog`
across runs.

### A subagent's rules are `Nothing` or an explicit `Inferred` assumption — never derived

FR-49 (S-43, issue #53) exists because Copilot's own system prompt carries no agent id: there is no
event anywhere in RAW this product could quote as "the rules this subagent ran under," unlike a
session's own rule set (`RuleStatementExtractor`, S-19, which reads real `<custom_instruction>`
blocks). `SubagentRuleDisplay` is closed to exactly two shapes through a private constructor, the
same closed-union reasoning `SessionTokenFigures` uses for its own absent state:
`SubagentRuleDisplay.Nothing` (the default, and the story's own preferred outcome — the edge case
says showing nothing "is an acceptable, preferred outcome over a labelled guess appearing in the
digest's ranked list") or `SubagentRuleDisplay.AssumedInherited`, built only from rule statements a
caller supplies on purpose. Nothing in this type walks `Data.Execution.Agent.ParentAgentId` or any
other structural link to *derive* an inheritance assumption — that would be exactly the guess the
edge case forbids ("do not try to infer or guess a subagent's rule set from context"). A future
caller that wants to assume a subagent inherited its spawning session's own rule set resolves that
rule set itself (S-19/S-20) and passes it to `AssumedInherited` explicitly; this type only enforces
that whatever comes out the other side is labelled `Provenance.Inferred` — a computed property, not
a settable field, so `InheritedRuleSetAssumption` cannot be constructed carrying any other
provenance (Scenario 1).

### `SubagentObservedContext` is a second, wholly separate contract from the rule-display question

Scenario 2 needs three facts genuinely on record for a subagent — its spawn description, its task
prompt, its own skill invocations — reported as Observed, never mixed into the same type as the
rule-display question above (a caller could otherwise be tempted to read "we have Observed context"
as license to also assert a rule set). `SpawnDescription` reads `Agent.Description` verbatim
(`subagent.started.data.agentDescription`, S-49) and `SkillInvocations` filters the session's own
`Skill` rows to `OwnerKind.Agent` with a matching `AgentId` — never a parent's or a sibling's
invocations, the same "never a parent's" discipline `ExecutionRecordBuilder` documents for
`ToolCall.TurnId` being `null` on a subagent's own calls. `TaskPrompt` is a plain, caller-supplied
input rather than read off `Data` directly: no derived entity yet carries the spawning `task` call's
own prompt argument — `ToolCall` has no `Arguments` column, only `Path` is extracted today
(`AecoPostMortem.Ingestion/CLAUDE.md`, "arguments is parsed polymorphically") — the same
not-yet-wired gap `PhaseChurnFinding` documents for its own `DeclaredIntent` input. `Provenance` is
again a computed property fixed to `Observed`, never settable, so this shape cannot accidentally
carry any other provenance value.

### `ToolFailureClusterFinding`'s cross-reference is an already-resolved plain input, not a substring match

Scenario 2 of issue #51 needs to know which tool identity a rule mandates — the same "which real
tool call does an operand name" question S-23 (issue #37, FR-31's four-layer resolution) exists to
answer, most confident first: exact tool name, then the logged `mcpServerName` field (never a
substring — `AecoPostMortem.Data.Execution.ToolCall.McpServerName`/`McpToolName` are already real,
separate columns for exactly this), then the derived role, then unresolved. S-23 is not merged as
this story lands, so `MandatedTool` takes the resolved pairing (`ToolIdentity`, the `RuleStatement`
that mandates it) as a plain input the same way `ToolCallOutcome` already does for a completed call
— matching this project's established pattern rather than reintroducing a substring match as a
shortcut, which is the exact failure this story's own edge case names: an earlier 49/15 figure that
pulled in a different MCP server's tool by matching `search_code` as a substring instead of an exact
identity (FR-31 layer 2, FR-48). A future caller supplies `MandatedTool` from S-23's resolution once
it exists, the same way `HookFailureFinding` and `FailedToolCallsFinding` document their own
not-yet-wired `Data` reads.

The cluster itself matches by the exact same `ToolIdentity` `FailedToolCallsCheck` already grouped
by (`string.Equals(..., StringComparison.Ordinal)`) — never a substring — and states that convention
literally as a `matchConvention` evidence item on every cluster (FR-46: "match tool names exactly,
and state the convention on the table"), the same reasoning `Resolution` gives for why an adherence
figure can't be served without the layer that produced it.

### A cluster's link to its mandating rule is evidence, not a new cross-finding-reference type

No `RuleAdherenceToolChoice` check exists yet in this project — S-23's operand resolution is what a
real one would need. "Links to the adherence finding for that rule" (issue #51, Scenario 2) is
therefore represented the same way `RepeatedFileReadFindingCheck`'s per-session read counts are: free
-form `EvidenceItem`s (`mandatingRuleSourceFile`, `mandatingRuleText`, `mandatingRuleLinkKind`), never
a bare pointer or foreign key. The value quoted is exactly the pair a `RuleAdherenceToolChoice`
finding is identified by once one exists — `FindingClassRegistry` already declares that class's
recurrence key as "the rule statement" — so a caller can look the real finding up by
`(FindingClass.RuleAdherenceToolChoice, ruleStatement)` the moment that check lands, without this
evidence shape needing to change. `mandatingRuleLinkKind = "hypothesis"` makes Scenario 2's own
wording — "labelled as a hypothesis" — a literal, assertable value rather than something a reader
has to infer from the finding's `Provenance` alone (even though `Provenance.Inferred` already says
the same thing at the whole-finding level, per FR-48). A tool no rule mandates carries none of the
three fields at all — absence in, absence out, the same discipline `SilentCheckEnvelope.From`
documents for a check the registry has no entry for.

### `MissingCapability`, not `Waste` — the same rate, a different class because it means something different

`ToolFailureClusterFinding` and `FailedToolCallsFinding` both call `FailedToolCallsCheck.Run` and
read the identical `ToolFailureRate`, but they build two different `Finding`s from it, deliberately:
`FailedToolCallsFinding` reports the *fact* of the failure (`Provenance.Derived`, `FindingClass.
Waste` — arithmetic over observed data, no judgment). `ToolFailureClusterFinding` reports the
*hypothesis* that a high rate makes the server the real problem rather than the rule (`Provenance.
Inferred`, `FindingClass.MissingCapability` — Epic E8, "the highest-value findings with the weakest
provenance," PRD Phase D). Two `CheckId`s (`"failed-tool-calls"` vs. `"tool-failure-clusters"`) over
one shared computation, not one check registered twice.

### `ContradictionCheck` produces no `Finding` — it is a special-purpose check, like `MalformedLineCheck` and `SpawnResolutionCheck`

`CheckRegistryEntry`'s own remarks name three "PRD §3.9 special-purpose checks" that use the
abstract `CheckId` string rather than one of `FindingClassRegistry`'s four closed classes:
contradiction, unresolvable-spawn, malformed-line. The other two (`Ingestion.MalformedLineCheck`,
`Ingestion.SpawnResolutionCheck`) already register a `CheckRegistryEntry` directly with no `Finding`
in between — a contradiction is not rule adherence, waste, or a missing capability (PRD §3.3's own
four-item table), so forcing one onto an existing `FindingClass` would either misrepresent what it
is or require widening the closed set for one check. `ContradictionCheck.Result.Provenance` carries
FR-43's "never Observed" requirement directly instead, as a `required` member on this project's own
result type set unconditionally to `Provenance.Inferred` — the same "fails construction by being
required, not by validating" reasoning `Finding.Provenance` already documents above — because this
check can only ever confirm that two statements' surface keyword polarity conflicts, never that
their meaning genuinely does.

### `ContradictionCheck.Run` groups by rule-set version, never comparing across one

FR-43 Scenario 2 ("it compares only statements in force together") is a stronger requirement than
`RuleSetVersionScope.RequireSingleVersion`'s refusal: a corpus spans many rule-set versions over
time by design, and the check has to find contradictions *within* each one, not refuse the moment
more than one version is present in its input. `ContradictionCheck.Run` therefore groups the
sessions it is handed by `RuleSetVersionId` (`Repository` + `RuleSetVersionHasher.ComputeHash`,
the identical identity `RuleSetVersioning.Compute` already uses) and calls
`Rules.ContradictionCheck.Run` once per group — a statement from one version's block set is
therefore never even placed in the same list as a statement from another, structurally, not by a
version-equality check inside the pairwise loop. `Population` on the resulting
`CheckRegistryEntry` is the total session count across every version in the input (matching PRD
§3.9's own worked phrasing, "a measured 0 contradictions found across 35 sessions checked" — sessions,
not statements), while `FindingCount` sums candidates found across all versions combined.

## Status

The finding record, check-registry shapes, and FR-56's generic suggestion-template mechanism, plus
seven real checks: `HookFailureFinding` (issue #27, FR-17, `CheckId = "hook-failure"`),
`RepeatedFileReadFindingCheck` (issue #25, FR-15), `FailedToolCallsFinding`
(`CheckId = "failed-tool-calls"`, FR-16, issue #26), `InterruptionLoadFinding`
(`CheckId = "interruption-load"`, FR-20, issue #30), `AbortedTurnFinding`
(`CheckId = "aborted-turn"`, FR-18, issue #28) and `PhaseChurnFinding`
(`CheckId = "phase-churn"`, FR-19, issue #29) — all `FindingClass.Waste` detection logic — plus
`ToolFailureClusterFinding` (`CheckId = "tool-failure-clusters"`, FR-46, issue #51), the first
`FindingClass.MissingCapability` detection logic, reusing `FailedToolCallsCheck` rather than
recomputing its rate. An eighth check registers a real id — `malformed-line`, built by
`AecoPostMortem.Ingestion.MalformedLineCheck` from FR-6's per-file read stats (issue #3 / S-02) —
but nothing in this project constructs it. No check exists in `AecoPostMortem.Rules` yet to bind a
real `SuggestionTemplate.CheckId` to — `SuggestionWorkedExampleTests` exercises the suggestion
mechanism against a synthetic tool-choice check result standing in for the story that will supply a
real one. Each of the seven checks is self-contained, but `FindingClassRegistry`'s Waste
`RecurrenceKeyDescription` is shared prose more than one touches, so expect it to need merging by
hand.

`ProcessDigest.Build` (issue #44, S-36, FR-41 part 1) ranks whatever findings are handed to it
by distinct sessions affected and states the masthead's designed states
(`NotYetAnalyzed`/`Incomplete`/`Analyzed`, `RuleCoverageStatus.NotYetAnalyzed`). It takes
`MastheadCounters` as a plain input — nothing in this repository yet writes those counters at ingest
time, the same not-yet-wired gap `HookFailureFinding` and `FailedToolCallsFinding` document for their
own `Data` reads. `RepositoryScope` (issue #45, S-54, FR-41 part 2) is now a required parameter too,
the same already-resolved-plain-input shape. Row expansion and the recurrence strip themselves needed
no new domain type — `Finding.Evidence`, `.Provenance`, `.Recurrence` and `.Suggestion` (via
`FindingEnvelope`/`SuggestionEnvelope` in `AecoPostMortem.Api`) already carried everything FR-41 part
2's Scenarios 1, 2 and 4 needed; `web/src/digest/` (`web/CLAUDE.md`) is where that data is actually
rendered as an expandable row. `RankedFindings` now excludes `Provenance.Inferred` and
`InferredFindings` is its own unranked field (issue #52, S-42, FR-48) — see "`InferredFindings` is a
separate, deliberately unranked field" above. `ProvenanceLabel.For` (same story) is the fixed,
textually distinct sentence per provenance level, served on `Api.FindingEnvelope`; S-54's own
`web/src/digest/ProvenanceBadge.tsx` is the first web consumer of both `Provenance` and
`ProvenanceLabel`, closing the non-textual-discriminator gap FR-48 left open (see "Gap closed by
S-54" above) — `web/src/routes/DigestPage.tsx` is no longer the `ComingSoon` placeholder either
story left it as.

`SessionTokenFigures` (issue #20, FR-24) is a non-`Finding` contract, now consumed by the masthead
it was published ahead of: `SessionRecording.Build` calls `From(Session)` for
`SessionMasthead.ContextSize`.

`OperatorResponseLog` and `Guardrail` (issue #49, S-39, FR-45) publish FR-45's recording and PRD
§5.4's guardrail computation as plain, already-resolved-input types, the same contract-first pattern
`SessionTokenFigures` and `ProcessDigest.Build` used for their own stories. Exercised directly by
`OperatorResponseLogTests` and `GuardrailTests`; no CLI command records a real operator action yet,
no store persists a log across runs, and no `AecoPostMortem.Api` envelope serves a `Guardrail` —
those are later work this story only makes possible.

`SessionRecording` (issue #15, S-08, FR-21 part 1 of 3) — the masthead and time-ordered tape — has
landed: `SessionMasthead` states identity, repository, branch, CLI version, elapsed time and the
five step-population counts; `SessionTape` orders every `Prompt`/`Hook`/`Skill`/`ToolCall`/`McpCall`
step by wall-clock time with its offset from session start, and states plainly when it carries none.
Consumed by `AecoPostMortem.Api.SessionEnvelope` (`GET /api/sessions/{sessionId}`) and rendered by
`web/src/routes/SessionPage.tsx`. Finding chips (joining findings per session — a different data
path from the tape) and the inspector/Detail/Thinking/Raw tabs are S-52 and S-53, not built here.

`ContradictionCheck` (issue #47, S-38, FR-43) publishes the third of PRD §3.9's special-purpose
checks, alongside `Ingestion.MalformedLineCheck` and `Ingestion.SpawnResolutionCheck`: pairwise,
self-match-excluding, keyword-polarity detection (`Rules.ContradictionCheck`) scoped to one
rule-set version at a time and registered on the "checks that found nothing" surface FR-42 (S-37,
issue #46) already published a `"contradiction-check"`-shaped test for ahead of this story landing.
No corpus-wide caller wires a real store's sessions into it yet — like every other check-shape
story in this project, it publishes the contract the eventual analysis-run orchestrator (the `run`
CLI command, not yet built) will call.

`SubagentRuleDisplay` and `SubagentObservedContext` (issue #53, S-43, FR-49) publish the same
contract-first pattern for a subagent's rules and its own context: no CLI command or
`AecoPostMortem.Api` envelope serves either yet — that wiring, and whatever caller eventually
decides to construct an `AssumedInherited` display, are later work (plausibly S-52/S-53's own
inspector tabs, which already own the session-page rendering this would slot into) this story only
makes possible.
