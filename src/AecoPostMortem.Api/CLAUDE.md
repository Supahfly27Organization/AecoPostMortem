# AecoPostMortem.Api

Endpoints for the three surfaces, and the host that serves them.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General`, `FindingEnvelope.Adherence` and `FindingEnvelope.BaseRate` (FR-44, issue #41), and the `From`/`FromAdherence`/`FromBaseRate` factories that assemble them from a `Finding`. FR-48 (issue #52, S-42) added `ProvenanceLabel`, required on every shape; FR-41 (issue #44, S-36) added `SessionsAffected`, the served ranking key; FR-33 (issue #38, S-24) replaced the adherence shape's `Resolution`/`RuleVersion` pair with one `required AdherenceFigure Figure`. Mockup parity item #5 added `Headline`, required on every shape — `Findings.Finding.Headline` passed straight through, unchanged, the same passthrough `Evidence`/`Recurrence` already are |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |
| `SilentCheckEnvelope.cs` | FR-42's "checks that found nothing" surface — `SilentCheckEnvelope.From(CheckRegistry)` projects only the entries that ran clean. Mockup parity item #6 added `Provenance`/`ProvenanceLabel`, projected straight from `CheckRegistryEntry.Provenance` (below) so a clean-check card can carry the same badge a finding does |
| `DigestEnvelope.cs` | FR-41 part 1 (issue #44, S-36): `MastheadEnvelope` and `DigestEnvelope` — the served corpus masthead and the findings already ranked by sessions affected; FR-41 part 2 (issue #45, S-54): `RepositoryScopeEnvelope`, carried on `MastheadEnvelope`. FR-48 (issue #52, S-42) added `InferredFindings`, served separately from `RankedFindings`. Mockup parity item #2 added `RepositoryScopeEnvelope.SessionIds`, the ordered session list a per-finding session strip needs. Mockup parity item #6 added `SilentChecks` (`SilentCheckEnvelope.From(digest.CheckRegistry)`), threading FR-42's surface through the same fetch. Mockup parity item #15 added `RuleCoverageStatusEnvelope` (`notYetAnalyzed`/`analyzed`, the closed wire shape for `Findings.RuleCoverageStatus`) and changed `MastheadEnvelope.RuleCoverage` from a bare enum to that type — `AnalyzedCoverage.Counts` reuses `RulesInventoryStatusCountsEnvelope` (`RulesInventoryEnvelope.cs`) verbatim rather than a second four-int shape |
| `AppStateReport.cs` | S-48's zero-data diagnosis — `AppStateKind` (`NoSourceFound` / `EmptyStore` / `Ready`) and `AppStateReport.Diagnose`, the two-empty-states-are-different-fixes rule as one pure function over two booleans |
| `ApiHost.cs` | builds the ASP.NET Core host: `GET /api/app-state` (`AppStateRoute`), `GET /api/digest` (`DigestRoute`), `GET /api/rules-inventory?version=` (`RulesInventoryRoute`, `VersionParameter`), `GET /api/sessions/{sessionId}` (`SessionRouteTemplate`), `GET /api/sessions/{sessionId}/steps/{stepId}?kind=` (`StepEvidenceRouteTemplate`, S-52, issue #16), and, when a built web app is available, the static files that serve it from the same process; `DiagnoseAppState`, `GetDigest`, `GetRulesInventory`, `GetSession` and `GetStepEvidence` are the same five without a listener |
| `HookFailureEventLookup.cs` | FR-17's error text (issue #27): resolves failed `hook.start`/`hook.end` pairs straight from a session's own RAW events into `Findings.HookFailureEvent` — `Data.Execution.Hook` carries no error column, so `GetDigest` cannot read it any other way |
| `DeclaredIntentLookup.cs` | FR-19's not-yet-wired gap (issue #29), closed: resolves `report_intent` tool calls' own `arguments.intent` straight from RAW into `Rules.DeclaredIntent`, ordering by the call's own timestamp read as Unix milliseconds (`Data.Execution.ToolCall` carries no field for it, and `RawEvent.Sequence` only orders within one session) — the one place in the codebase allowed to name `report_intent` |
| `SessionRuleSetLookup.cs` | FR-27's own not-yet-wired gap, closed: `SessionRuleSetLookup.BuildAll` resolves a whole store's `RawEvent`s into one `Rules.SessionRuleSet` per `Data.Execution.Session` row, calling `Ingestion.SessionRuleExtractor.Extract` per session — the corpus-wide walk nothing did before this landed |
| `ToolInvocationShapeLookup.cs` | The real `Rules.ToolInvocationShape` corpus (piece 3), closed: `BuildAll` reads `HasPath`/`McpServerName` straight off `Data.Execution.ToolCall` (already real columns) and `SpawnsAgent` off `Data.Execution.Agent.SpawningToolCallId` (already structural) — no new RAW parsing for any of the three — and reads `HasPattern`/`HasReplacement`/`HasFileText`/`HasCommand` from each call's own RAW `tool.execution_start.data.arguments`, field names verified against the live 35-session reference corpus: `pattern` (`rg`/`grep`/`glob`), `old_str`/`new_str` (`edit`), `file_text` (`create`), `command` (`powershell`). `apply_patch`'s own `arguments` is a JSON string (the whole patch body), not an object — a real wrinkle the corpus check caught — so all four are `false` for a string-shaped call rather than guessed at. The public `BuildAll(rawEvents)` overload parses RAW itself; an `internal BuildAll(argumentsByCall)` overload (piece 3's fifth slice, code review) takes an already-built dictionary instead, so `GetDigest` can share one parse pass with `ParamCarryingCallLookup` rather than each lookup parsing the same payloads separately |
| `RawToolArguments.cs` | Piece 3's fifth slice: `ByCall` — the RAW-parsing pass factored out of `ToolInvocationShapeLookup` so `ParamCarryingCallLookup` (below) can reuse the identical `tool.execution_start` → `ToolArguments` read rather than walking `rawEvents` a second time for the same question |
| `ParamCarryingCallLookup.cs` | Piece 3's fifth and final slice: the real `Rules.ParamCarryingCall` corpus `Rules.AlwaysPassParamCheck` resolves its mentions against. `SpawnsAgent` reuses `Agent.SpawningToolCallId` the same structural way `ToolInvocationShapeLookup` does; `ArgumentKeys` reads every field name a call's own RAW arguments carried (`Ingestion.ToolArguments.PropertyNames`, new this slice) rather than one fixed set, since the parameter a rule names is arbitrary — unlike `ToolInvocationShapeLookup`'s four closed booleans. `ArgumentsRecorded` (code review) is `true` only when a call's own arguments were object-shaped, so "no record at all" never collapses into "recorded with no keys". The public `BuildAll(rawEvents)` and an `internal BuildAll(argumentsByCall)` overload mirror `ToolInvocationShapeLookup`'s own split (below) |
| `RulesInventoryClassifier.cs` | FR-40's caller-supplied classify function (`Rules.RulesInventory.Build`'s own contract): `RulesInventoryClassifier.BuildClassifier` maps `Rules.RuleShapeCatalogue.MatchAll`'s output onto `RuleStatementStatus`, taking the real `ToolInvocationShapeLookup` corpus — a `PreferAOverB` or `UseAAfterB` match whose both operands resolve against it (`Rules.OperandResolver.ResolveTwoOperands`) is `Watched` (piece 3's fourth slice added `UseAAfterB` to this branch); a `ToolIsBanned` match whose single operand resolves (`Rules.OperandResolver.Resolve`, no `ToolRole` involved) is also `Watched`; a `NeverReadPath` or `AlwaysPassParam` match is `Watched` unconditionally, no resolution involved (piece 3's third and fifth slices — neither operand is a tool name); every other matched shape stays `CheckableNotYetBuilt`. Mockup parity item #18 gave the caller-supplied `NotCheckable(reason)` its first real constructor: an unmatched, directive statement (`UnmatchedStatementDisposition.CheckableNotBuilt`) gated on whether an action was *needed*/*necessary*/*relevant* to "the task" (`TaskRelevanceObligation`) is `NotCheckable`, everything else in that disposition stays `CheckableNotYetBuilt` |
| `RulesInventoryEnvelope.cs` | FR-40's served inventory (S-22, issue #35): `RuleStatementStatusEnvelope` (four closed shapes, `"watched"`/`"checkableNotYetBuilt"`/`"notCheckable"`/`"notARule"`), `RuleRetirementEnvelope` (`"inForce"`/`"retired"`), `RuleSetVersionEnvelope`, `RulesInventoryRowEnvelope`, `RulesInventoryStatusCountsEnvelope` and `RulesInventoryEnvelope.From` — one rule-set version's statements, never a union across versions. Mockup parity item #7 added `RuleViolationCountEnvelope` (`"counted"`/`"notAvailable"`) and `RulesInventoryRowEnvelope.ViolationCount` — a Watched row's own violation count, `null` for every other status |
| `SessionEnvelope.cs` | FR-21, part 1 of 3 (S-08, issue #15): `SessionTokenFiguresEnvelope`, `SessionMastheadEnvelope`, `SessionTapeStepEnvelope`, `SessionEnvelope` — the served masthead and tape, assembled from `Findings.SessionRecording`. FR-21 part 2 of 3 (S-52, issue #16) added `SessionFindingChipEnvelope` and `SessionEnvelope.Findings`, assembled from `Findings.SessionFindings`; FR-21 part 3 of 3 (S-53, issue #17) added `SessionRecordingStatusEnvelope` (`Complete`/`IngestIncomplete`/`ReconstructionFailed`) and the required `SessionEnvelope.Status` field; FR-22 (S-09, issue #18) added `SessionAgentLaneEnvelope` and the required `SessionEnvelope.Lanes` field (an optional `lanes` parameter on `From`, defaulting to an empty list — every existing call site still compiles). Mockup parity item #14 added `SessionMastheadEnvelope.StartedAt` (`required DateTimeOffset`) and `.EndedAt` (`DateTimeOffset?`), passed through unchanged from `Findings.SessionMasthead`. Mockup parity item #17 added `SessionTapeStepEnvelope.Findings` (`required IReadOnlyList<FindingEnvelope>`, defaulting to `[]` via a new optional `findings` parameter on `From`) and a matching optional `stepFindings` parameter on `SessionEnvelope.From` — see `SessionTapeStepFindingLookup.cs` below for what populates it |
| `SessionTapeStepFindingLookup.cs` | Mockup parity item #17: attaches a finding to the specific tape step(s) it is unambiguously about, for the narrow set of finding shapes whose own `Finding.Evidence` names an identity (a tool name, a hook name) a session's own `ToolCall`/`Hook` rows can be matched against exactly — `Build(sessionFindings, toolCalls, hooks)` returns a `(SessionTapeStepKind, StepId)`-keyed map. Covers exactly two shapes today, matched by the marker `EvidenceItem.Field` name(s) each orchestrator already writes (the same technique `RulesInventoryEnvelope.cs`'s own `BuildViolationCounts` already uses to join a served count back to its source check, applied here to a new question): a `toolIdentity` field (`FailedToolCallsFinding`/`ToolFailureClusterFinding`) matches every failed `ToolCall` of that exact tool identity in the session — every failing call, not a guessed "first" or "most recent" one, since the finding's own evidence is an aggregate rate over all of them; a `data.success`/`data.error` field pair (`HookFailureFinding`) matches every failed `Hook` row whose `Name` equals the finding's own `Recurrence.Key`. Every other finding-producing check (`RepeatedFileReadFindingCheck`, `AbortedTurnFinding`, `InterruptionLoadFinding`, `PhaseChurnFinding`, `BannedToolFinding`, `NeverReadPathFinding`, `UseAAfterBFinding`, `AlwaysPassParamFinding`) is deliberately left uncovered — see the non-obvious decision below for why each one doesn't fit |
| `StepEvidenceEnvelope.cs` | FR-21 part 2 of 3 (S-52, issue #16): `ThinkingEnvelope` (`Present`/`Unavailable`), `RawStepEventEnvelope` (`Present`/`Skipped`), `StepEvidenceEnvelope` — the inspector's Thinking and Raw tab contracts. No Detail contract exists here: every field the Detail tab needs already travels on `SessionTapeStepEnvelope`. FR-23 (S-10, issue #19) added `ModelReasoningReadability` and `ThinkingEnvelope.Unavailable.ReadabilityByModel` — the session's own measured readable-reasoning share, one entry per model, populated only for the provider-encryption reason |
| `StepEvidenceLookup.cs` | FR-21 part 2 of 3 (S-52, issue #16): `StepEvidenceLookup.Find` — resolves a step's raw event and (for a prompt step) its readable reasoning straight from a session's own `RawEvent`s, reading envelopes the same way `AecoPostMortem.Ingestion.ExecutionRecordBuilder` does. FR-23 (S-10, issue #19) added `StepEvidenceLookup.ReasoningReadabilityByModel`, scanning the whole session's own main-thread `assistant.message` events (not just the current turn) to build the per-model readable share |
| `SubagentOutputEnvelope.cs` | FR-22 (S-09, issue #18): the inspector's lane-output contract — `Present`/`NotRecorded`/`Failed`, a closed three-shape union so "a real report", "nothing recorded" and "the subagent failed" are each a stated value, never inferred |
| `SubagentOutputLookup.cs` | FR-22 (S-09, issue #18): `SubagentOutputLookup.Find` — resolves one subagent's real report from the last `assistant.message` carrying its own `agentId`, reading envelopes the same way `StepEvidenceLookup` does. Never reads a `tool.execution_complete` result at all, so the parent's truncated `read_agent` stub cannot surface as a lane's output by construction |
| `MonitorComparisonEnvelope.cs` | FR-39's served comparison (S-35, issue #43): `MonitorComparisonEnvelope` — `BeforeVersion`/`AfterVersion` reuse `RuleSetVersionEnvelope` (S-22), `Before`/`After` carry `Findings.AdherenceFigure` directly, no separate figure envelope — and `MonitorComparisonEnvelope.From(Findings.MonitorComparison)` |

## References

`Findings` — the API is a thin host over the finding classes and their orchestration for the
finding endpoints FR-59 unblocks; nothing here reaches into `Data` or `Rules` for that part.

`Data` and `Ingestion` — added by S-48, for a different reason: `ApiHost.DiagnoseAppState` has to
know whether the store carries any RAW events (`Data.LocalStore`) and whether the Copilot
session-state root exists (`Ingestion.SessionDiscovery`, reusing FR-1's own discovery rather than a
second `Directory.Exists` check). This is a genuine widening of the "thin host" description below,
not an oversight — S-48 is one of the stories `FindingEnvelope.cs`'s own doc comment named as
building "real endpoints," and the app-state endpoint is not a finding endpoint at all.

`Rules` — added by S-22 (issue #35, FR-40), and the first direct reference this project has to it.
`RulesInventoryEnvelope` maps `Rules`' own `RulesInventory`/`RuleStatementStatus`/`RuleRetirement`
shapes onto the wire, so the reference is stated explicitly in `AecoPostMortem.Api.csproj` rather
than leaned on transitively through `Findings` — this project's own dependency list should say what
its source actually names. It does not weaken Repo Rule 6, which binds what `Rules` may *contain*,
not who may read it.

`ApiHost.GetSession` (S-08) widens the same `Data` reference further: it opens a `PostMortemContext`
and reads `Sessions`/`Turns`/`ToolCalls`/`Agents`/`Skills`/`Hooks` directly by session id, then hands
the plain rows to `Findings.SessionRecording.Build` — the same "read through `Data`, feed `Findings`
plain inputs" split `Findings/CLAUDE.md` documents for its own checks, just performed here instead of
inside `Findings` because nothing in `AecoPostMortem.Findings` may query the store on its own. See
`SessionEnvelope.cs`'s own remarks below for why this reads the *derived* tables rather than
re-deriving them from RAW in this project.

S-53 (issue #17, FR-21 part 3 of 3) widens `GetSession` once more, narrowly: alongside the derived-
table read above, it also reads this session's own `RawEvents` and runs them through
`Ingestion.ExecutionRecordBuilder.Build` purely to read back its `SpawnResolutionCheck` — never for
the `Turn`/`ToolCall`/`Agent` rows that same call also returns, which stay unused here. See
`SessionEnvelope.cs`'s "`SessionRecordingStatusEnvelope`" remark below for why this second, narrow
read does not reopen the "duplicate reconstruction path" question the paragraph above already
settled against.

Mockup parity item #4 widens `GetSession` a third way: alongside the two reads above, it now also
reads every `Session`/`RawEvent`/`ToolCall`/`Turn`/`Permission`/`Agent` row belonging to this
session's own repository (mirroring `GetDigest`'s own corpus-wide-then-filter shape, just scoped to
one repository already known rather than a selected one) and calls the same ten check orchestrators
`GetDigest` calls, through the new shared `BuildFindingsForScope` helper — see `SessionEnvelope.cs`'s
own remarks below for how the combined result is filtered down to one session.

`ApiHost.GetStepEvidence` (S-52, issue #16) reads `Data.RawEvent` directly instead — the inspector's
Raw and Thinking tabs are provenance over the *event*, not the derived row, and neither `Turn` nor
`ToolCall` carries a foreign key back to the `RawEvent` that produced it
(`AecoPostMortem.Data/CLAUDE.md`). `StepEvidenceLookup` (this project, not `Ingestion`) reuses the
existing `Ingestion` reference (S-48, above) to call `Ingestion.EventEnvelopeReader.TryRead` — the
same envelope parsing `Ingestion.ExecutionRecordBuilder` already does to build the tape's own rows —
rather than duplicating it a second time.

`ApiHost.GetDigest` (S-36, issue #44) widens the `Data`/`Ingestion`/`Findings` references a third way:
it reads `Session`/`RawEvent`/`ToolCall`/`Turn`/`Permission`/`Agent` corpus-wide, calls seven of the
eight waste/missing-capability/adherence check orchestrators (`Findings.RepeatedFileReadFindingCheck`,
`FailedToolCallsFinding`, `AbortedTurnFinding`, `HookFailureFinding`, `InterruptionLoadFinding`,
`PhaseChurnFinding`, `BannedToolFinding`), and — for the two check inputs no derived table carries
yet — `HookFailureEventLookup`/`DeclaredIntentLookup` (this project, reusing `Ingestion.
EventEnvelopeReader` and `Ingestion.ToolArguments` the same way `StepEvidenceLookup` reuses the
reader). `Rules` gains its second real caller here too: `ToToolCallOutcomes` builds `Rules.
ToolCallOutcome` from `ToolCall` directly, the query S-14's own remarks named as later work.
`ToolFailureClusterFinding` is not run — it needs a mandating rule, which real rule extraction at
scale (S-20) does not populate yet.

Piece 3's second slice (`BannedToolFinding`) widens `GetDigest` a fourth way: it reuses
`SessionRuleSetLookup` and `ToolInvocationShapeLookup` — both already added below for
`GetRulesInventory` — scoped to the selected repository's own sessions this time, and calls `Rules.
RuleShapeCatalogue.MatchAll` directly, this method's own first use of that entry point (see this
file's own remarks below for exactly what is scoped how and why).

Piece 3's third slice (`NeverReadPathFinding`) widens `GetDigest` a fifth way, more narrowly: it
reuses the identical `RuleShapeMatch` list `BannedToolFinding` already built (renamed
`ruleShapeMatches` to reflect that both checks now filter it independently) and `scopedToolCalls`
directly — it needs no `ToolInvocationShape` corpus at all, since `Rules.NeverReadPathCheck` matches
paths, not tool names.

Piece 3's fourth slice (`UseAAfterBFinding`) widens `GetDigest` a sixth way, reusing the identical
`ruleShapeMatches`/`invocations`/`scopedToolCalls` triple `BannedToolFinding` already built — the
ninth check orchestrator this method calls, needing no new read of its own.

Piece 3's fifth and final slice (`AlwaysPassParamFinding`) widens `GetDigest` a seventh way: it reuses
`ruleShapeMatches` and calls the new `ParamCarryingCallLookup.BuildAll` as `GetDigest`'s tenth and final
piece-3 check orchestrator. Unlike every other `RuleAdherenceToolChoice` finding here,
`AlwaysPassParamFinding.Run` takes no `ToolCall` parameter at all: `ParamCarryingCall` already carries
`SessionId` (it was built specifically for this check, not shared the way `ToolInvocationShape` is), so
there is no separate entity read needed for session attribution. `GetDigest` builds
`scopedArgumentsByCall` (`RawToolArguments.ByCall(scopedRawEvents)`) once and passes it to both this
lookup's and `ToolInvocationShapeLookup`'s own `internal` shared-dictionary overloads (code review,
below) rather than calling either lookup's public, RAW-parsing overload twice over the same payloads.

`ApiHost.GetRulesInventory` (S-22, issue #35) is what that real rule extraction at scale turned out
to be: it reads `Session`/`RawEvent` corpus-wide (the same two tables `GetDigest` already reads a
superset of) and calls `SessionRuleSetLookup.BuildAll`, which reuses the existing `Ingestion.
SessionRuleExtractor` reference rather than adding a new one — the same "reuse an existing narrow
reader, add the corpus-wide walk" shape `HookFailureEventLookup`/`DeclaredIntentLookup` established.
`Rules` gains its third real caller: `RuleShapeCatalogue.MatchAll` and `RulesInventory.Build`/
`.MostRecentVersion` are called directly, and `RulesInventoryClassifier` (this project) is the
caller-supplied classify function `RulesInventory.Build` requires — see this file's own remarks below
for what it does and does not classify. It now also reads `ToolCall`/`Agent` corpus-wide and calls
`ToolInvocationShapeLookup.BuildAll`, the real `ToolInvocationShape` corpus `RulesInventoryClassifier`
resolves matched operands against — corpus-wide, not scoped to the selected repository, the same
scope `RuleShapeCatalogue.MatchAll`'s own statement matching already uses (see "`GetRulesInventory`
classifies every statement in the corpus" below).

Mockup parity item #15 widens `GetDigest` an eighth way: it also calls the new `BuildRuleCoverageStatus`
(and the `BuildRulesInventoryInputs` helper factored out of `GetRulesInventory`'s own former inline
sequence) for the Digest masthead's own rule-coverage figure — corpus-wide, at the selected
repository's own most recent rule-set version, the identical pipeline `GetRulesInventory` uses. See
"`GetDigest`'s rule-coverage figure reuses `GetRulesInventory`'s own pipeline..." below for the scope
decision and why the two endpoints can never disagree.

Mockup parity item #17 widens `GetSession` a fourth way, narrowly: it calls
`SessionTapeStepFindingLookup.Build` over `findings.Chips` (the session-scoped findings
`SessionFindings.For` already resolved, mapped back to plain `Finding`s) and the same
`toolCalls`/`hooks` rows this method already reads for the tape itself — no new query. The result
threads through the new `stepFindings` parameter on `SessionEnvelope.From`.

## Non-obvious decisions

### `FindingEnvelope` is three closed shapes, not one type with a nullable figure

Only adherence classes carry an adherence figure (FR-33). The response envelope makes that
distinction structural rather than a nullable field: `General` and `BaseRate` have no `Figure`
member at all, and `Adherence` is the only shape that has one — `required`. Assembling an
`Adherence` envelope without it is a compile error (CS9035), the same guarantee
`Finding.Provenance` already gives (issue #23).

S-24 (issue #38) is the story that closed FR-33's refusal, and it replaced this shape's original
`Resolution` + `RuleVersion` pair with a single `required AdherenceFigure Figure`. Two separate
members were the weak point: they let a caller supply a resolution that did not produce the
percentage it was served beside, and the wire carried a single `operandLayer` string where FR-33
asks for the layer used *per operand*. `AdherenceFigure` (`AecoPostMortem.Findings`) fixes both —
the percentage is a computed property over the per-operand call counts, so there is no bare figure
to refuse at run time because none can be constructed, and `RuleVersion` is
`Rules.RuleSetVersionId` (repository + content hash, S-20) rather than a display string, so S-35's
Monitor comparison can tell whether two figures were even scoped to the same rule set before
comparing them.

`FromAdherence(Finding, AdherenceFigure)` is the only producer of this shape in this project, and
the figure is a non-optional parameter — `FindingEnvelopeTests.The_only_way_to_produce_an_adherence_
envelope_takes_the_figure_as_a_required_parameter` proves by reflection that no second factory has
appeared, and `No_constructor_opts_out_of_required_member_enforcement` proves no constructor carries
`[SetsRequiredMembers]`, the one attribute that would switch CS9035 back off and make the refusal a
convention again.

All three shapes derive from `FindingEnvelope` through a private constructor, so nothing outside this
file can add a fourth shape — the same closed-hierarchy trick `SuggestionEnvelope` uses.
`[JsonPolymorphic]` / `[JsonDerivedType]` carry a `"kind"` discriminator (`"general"` / `"adherence"`
/ `"baseRate"`) so a client can tell the shapes apart without inspecting which optional fields happen
to be present.

### `FindingEnvelope.BaseRate` labels a conditional rule's figure, and cannot be mistaken for a resolved one (FR-44, issue #41)

A conditional rule — the parallel-tool-calling rule's worked example: a measured 43.6% single-call
rate across 7,449 tool-issuing messages, whose *availability of a second independent call* was never
measured — is not a violation rate, and PRD §3.9 names presenting it as one as the exact failure to
avoid. `BaseRate` structurally cannot carry `Resolution` or `RuleVersion` (same as `General` — a base
rate is not a resolved adherence percentage FR-33 could attach a layer to), and instead carries a
`required string UnevaluatedCondition` stating what the logs could not check. Assembling one without
that condition is the same CS9035 compile error `Adherence` gives for its own two required fields —
Scenario 1's "the unevaluated condition is stated" is therefore not optional prose a caller could
forget to add.

Scenario 2 ("A base rate is never ranked as a violation") turned out to be satisfied more strongly
than this story alone set out to: every `BaseRate` finding carries `Provenance.Inferred` (this
story's own worked example), and FR-48 (issue #52, S-42, landed after this one) partitions
`ProcessDigest.Build`'s *entire* input by provenance — every `Inferred` finding, `BaseRate` included,
is structurally excluded from `RankedFindings` and served through `DigestEnvelope.InferredFindings`
instead, never interleaved by rank at all. `DigestEnvelopeTests.A_base_rate_item_never_appears_in_
ranked_findings_and_serialises_a_distinct_kind_in_inferred_findings` proves both halves together: a
base rate is absent from `RankedFindings` and, wherever it is served, still carries the `"baseRate"`
wire discriminator distinct from `"adherence"` — belt and suspenders, not a fallback on the
discriminator alone. `FromBaseRate` still sets that discriminator regardless of which list a caller
eventually serves the envelope through, since this contract has no way to know FR-48 would add a
second list when it was written.

`FromBaseRate(Finding, string unevaluatedCondition)` follows `FromAdherence`'s own precedent:
`unevaluatedCondition` is a required parameter, not read off the `Finding` (which has no field for
it — the same reasoning `FromAdherence` gives for taking `resolution`/`ruleVersion` as parameters
rather than trusting `Finding.Resolution` alone). `SampleConditionalRuleFinding` in both
`FindingEnvelopeTests` and `DigestEnvelopeTests` gives the finding `Provenance.Inferred`, not
`Observed`: the message count itself is a plain fact, but treating it as bearing on the rule at all
assumes the unmeasured condition held, the same reasoning FR-48 gives for labelling the
MCP-unreliability-causes-disobedience hypothesis Inferred even though its two failure rates are each
Observed on their own.

### `ProvenanceLabel` rides alongside `Provenance`, not in place of it

FR-48 (issue #52, S-42) requires the three provenance levels to be distinguishable without reading
the enum's own name, and an Inferred finding's distinguishing text to survive being quoted out of
its original styling. `FindingEnvelope.ProvenanceLabel` is a second, `required` string field —
`Findings.ProvenanceLabel.For(finding.Provenance)` — carried next to the existing `Provenance` enum
member rather than replacing it: `Provenance` stays the machine-readable value a client branches on,
`ProvenanceLabel` is the fixed human sentence a client can render or quote verbatim. Both `From` and
`FromAdherence` set it from the same finding's `Provenance`, so the two can never disagree.

`ProvenanceLabel` only supplies the *textual* half of FR-48's Scenario 2 ("distinguishable without
reading the label"), which read literally also asks for a non-textual discriminator — an icon or
shape a client could use without parsing the sentence at all. That half is not defined on this
contract: `Provenance` itself is still served, so a future client can map it to a shape/icon, but no
such mapping exists here because no rendering surface consumes this contract yet. See
`Findings/CLAUDE.md`'s matching note under "`InferredFindings` is a separate, deliberately unranked
field."

### `SuggestionEnvelope` makes "no suggestion" a value, not an absence

`Finding.Suggestion` is nullable because a finding class with no template (FR-56) ships with none.
Wrapping it in a nullable field on the envelope would let "no suggestion" collide with "the field was
omitted by mistake." `SuggestionEnvelope` is instead a required, closed two-state union —
`Present { Text }` and the `Absent` singleton (backed by the nested `AbsentSuggestion` record) — so
every served finding's `Suggestion` field is present in the JSON, and its value states explicitly
which case applies. `SuggestionEnvelope.Of(Suggestion?)` does the mapping from the domain type.

### `SilentCheckEnvelope.From` filters, it never synthesises

FR-42's surface has exactly one producer, `From(CheckRegistry)`, and it is a pure filter over
`CheckRegistry.Entries` — `Status == Ran && FindingCount == 0` — never a step that fabricates an
entry for a check the registry doesn't carry. That is what makes all three of this story's negative
scenarios (issue #46) hold structurally rather than by caller discipline:

- A `Refused` entry is dropped here; it belongs to the Rules Inventory (FR-53) as "not checkable",
  a different surface this project does not yet implement — showing it here as clean is exactly the
  "silence reading as compliance" failure PRD §3.9 names.
- A check the registry has no entry for at all (not built yet this release, e.g. the contradiction
  check before S-38) has nothing for `From` to project — absence in, absence out. There is no
  hard-coded list of expected `CheckId`s this type could complete against; it only ever reflects
  what `CheckRegistry.Entries` actually contains.
- A `Ran` entry with `FindingCount > 0` is also dropped: this surface is specifically checks that
  found *nothing*, not every check that ran. `FindingCount` is a real int on every served
  `SilentCheckEnvelope` (never null, since `Refused` entries — the only ones with a null
  `FindingCount` — are filtered out before the projection), and it is always `0` by construction of
  the filter, carried explicitly rather than left for the reader to infer from mere presence.

Unlike `FindingEnvelope` and `SuggestionEnvelope`, `SilentCheckEnvelope` is a single plain
`sealed record` rather than a closed hierarchy behind a private constructor. Those two types close
off a *discriminated union* — "which of these shapes is this?" is part of what a client needs to
know. This surface serves only one shape (a clean check's id, population and zero count); there is
no second variant to keep a client from constructing by mistake, so there is nothing for the
closed-hierarchy trick to protect here.

### `FindingEnvelope` and `SuggestionEnvelope` are still a contract, not endpoints

S-50 / FR-59 published the response shape so the stories that build real finding endpoints against
it (S-08, S-22, S-24, S-36, S-37, S-42) have something structural to target. Nothing here reads
through `Data` or calls into `Rules` for those two types yet — the factory methods take a `Finding`
(and, for `FromAdherence`, a `Resolution` and rule version) as plain inputs. `ApiHost` does not
serve `FindingEnvelope`, `SilentCheckEnvelope` or `DigestEnvelope` yet either; the app-state endpoint
is the first real endpoint this project ships, and it does not need the finding contract at all.
`SilentCheckEnvelope.From` follows the same plain-input pattern — a `CheckRegistry` in, a projected
list out — nothing here reads through `Data` or calls into `Rules` for it either.

### `DigestEnvelope.SilentChecks` reuses the exact `CheckRegistry` `ProcessDigest.Build` already carries, never a second read

Mockup parity item #6 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`, "Checks
that found nothing"): `ProcessDigest` (`AecoPostMortem.Findings/CLAUDE.md`) now carries the
`CheckRegistry` its own `Build` already received, so `DigestEnvelope.From` calls
`SilentCheckEnvelope.From(digest.CheckRegistry)` directly — the identical registry
`GetDigest` already assembled from all ten check orchestrators for `ProcessDigest.Build`'s own
`DigestState` computation, not a second registry built or filtered here. `SilentCheckEnvelope.From`
itself needed no change (`Api/CLAUDE.md`'s own remarks on it predate this story) — the only real gap
was that nothing carried its input past `ProcessDigest.Build` to a caller that could apply it.

`CheckRegistryEntry.Provenance` (`AecoPostMortem.Findings/CLAUDE.md`) is what makes the mockup's own
provenance badge per clean-check card possible: every check orchestrator wired into `GetDigest`
has exactly one fixed provenance for the findings it would produce (`hook-failure`/
`interruption-load` are `Observed`; the other eight are `Derived`), so the field is a caller-stated
fact set once per orchestrator, never derived or guessed here. Verified against the live 35-session
reference corpus (dominant repository, `supahfly27/UpFront`): three of `GetDigest`'s ten checks ran
clean — `banned-tool-used`, `use-a-after-b` and `always-pass-param`, all `Derived`, each over a
population of 24 sessions — a real browser renders all three as cards with a `DERIVED` badge.
`never-read-path-used` correctly does not appear: it is the one piece-3 adherence check with a real
violation on this corpus (`Findings/CLAUDE.md`'s own remarks, 99 real accesses).

### `RepositoryScopeEnvelope` mirrors `RepositoryScope` exactly — a plain projection, not a filter

FR-41 part 2 (issue #45, S-54): `RepositoryScopeEnvelope.From` copies `SelectedRepository` and
`AvailableRepositories` straight across, the same shape `MastheadEnvelope.From` already uses for
every other masthead field. It does not re-derive or re-filter anything — `DigestEnvelope.From`'s
`RankedFindings` mapping is untouched by which repository is selected, because the caller of
`ProcessDigest.Build` (`AecoPostMortem.Findings/CLAUDE.md`) already scoped `findings` to one
repository before this envelope is ever assembled.

Mockup parity item #2 (the per-finding session strip, `docs/product-superpowers/discovery/2026-08-21-
ui-mockup-parity.md`) added a third field the same way: `SessionIds`, mirroring `RepositoryScope.
SessionIds` verbatim. This closed a real gap the item's own scoring had missed — neither this
envelope nor `MastheadEnvelope` exposed an ordered, full session list before this change, only a bare
`SessionCount`, so a client had no way to know *which* of the scope's own sessions a finding's
`Recurrence.Occurrences` touched, only how many. `ApiHost.BuildRepositoryScope` (below) is where the
ordering is decided — chronological by the session's own `StartedAt`, never by session id text (the
same PR #108/#112 lesson `RuleSetVersioning`/`RuleSetVersionAdjacency` already learned for exactly
this reason, `AecoPostMortem.Rules/CLAUDE.md`) — and `GetDigest` now reuses that one ordered list to
build `scopedSessionIds` too, rather than re-deriving the same repository filter a second time: the
served strip positions and the sessions every check ranks over can therefore never disagree.

### `RulesInventoryEnvelope` serves the status counts rather than letting a client recount them

FR-40's four-status breakdown (a measured 4 / 9 / 9 / 21) is the figure PRD §2 says every coverage
number derives from, so it is served as `RulesInventoryStatusCountsEnvelope` even though a client
could count `Rows` itself. Two surfaces recounting independently is exactly how the three
conflicting coverage figures the PRD had to reconcile came about; one served figure is one answer.
It is projected from `RulesInventory.StatusCounts`, itself a computed property over the rows, so
the served counts cannot disagree with the served rows.

`RuleStatementStatusEnvelope.Of` and `RuleRetirementEnvelope.Of` switch over the domain's own closed
unions with no catch-all arm that could serialise an unrecognised shape as something plausible — a
fifth domain status would fail to compile here rather than reach a client mislabelled.

### `RuleViolationCountEnvelope` is a closed two-shape union, and a Watched row's own count is joined by matched-shape, not by re-running the classifier

Mockup parity item #7 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`, Part 3
"Violations" column): before this change, a Watched row's violation count lived only on the Digest's
`RuleAdherenceToolChoice` findings, one hop away — an operator had to leave the Rules Inventory to
find it. `RulesInventoryRowEnvelope.ViolationCount` closes that gap, but only four of FR-34's five
`RuleShapeKind`s have a real Finding-producing orchestrator (`BannedToolFinding`,
`NeverReadPathFinding`, `UseAAfterBFinding`, `AlwaysPassParamFinding`) — `PreferAOverB` has none. A
count therefore cannot be a bare `int?` on the row: a `null` from "no orchestrator exists for this
shape" would be indistinguishable from "the check ran and found nothing," exactly the silence-reads-
as-compliance failure PRD §3.9 names. `RuleViolationCountEnvelope` is a closed two-shape union behind
a private constructor instead, the same `[JsonPolymorphic]`/`[JsonDerivedType]` mechanism
`SuggestionEnvelope` uses — `CountedViolations` states a real number (including a real zero: a check
that ran over this statement and genuinely found nothing), and `NoBuiltCheck` states plainly that the
matched shape has no check to draw a count from. `RulesInventoryRowEnvelope.ViolationCount` itself is
`null` for every status but `WatchedStatus` — a row that isn't Watched has no check running against
it at all, a different fact from a Watched row whose shape has no built check.

`ApiHost.GetRulesInventory`'s own `BuildViolationCounts` runs the same four check orchestrators
`GetDigest` runs, but over this method's own corpus-wide `matches`/`invocations`/`toolCalls` — the
exact inputs `RulesInventoryClassifier` already resolved Watched status against (see "`GetRulesInventory`
classifies every statement in the corpus," above) — never a second, differently (repository-)scoped
read the way `GetDigest`'s own seventh check is scoped to `repositoryScope.SelectedRepository`. The
join back to a row is by `RuleShapeMatch.Statement` (the same `RuleStatement` value — `SourceFile` +
`Text` — the classifier's own `byStatement` dictionary already keys on): each of the four Finding
classes keys its own `Recurrence.Key` to the matched statement's own text (`Findings/CLAUDE.md`'s
remarks on each), so `BuildViolationCounts` reads each check's own evidence field for its count
(`call_count` for `BannedToolFinding`, `access_count` for `NeverReadPathFinding`, `violation_count`
for `UseAAfterBFinding`/`AlwaysPassParamFinding` — `Findings/CLAUDE.md`'s "carries its count in
Evidence, never in Resolution" remarks for each) keyed by that same text, and builds one dictionary
entry per matched statement of a built shape — present with `Counted(0)` even when the check produced
no `Finding` for it (a real, checked zero), absent entirely for a `PreferAOverB` match (so the row
envelope's own `GetValueOrDefault(..., NotAvailable)` lookup falls through honestly).

Verified against the live 35-session reference corpus: the dominant repository's own real
`NeverReadPath` violation (`Api/CLAUDE.md`'s own status notes, a measured 99 real accesses at the
time that note was written) now serves as a real `Counted` violation count directly on this row — a
live re-check against the current store measured 103 (the corpus has grown since) — confirmed via a
real `GET /api/rules-inventory` request against the live store, not only at the unit level.

### `ToolInvocationShapeLookup` needed almost no new RAW parsing — real payloads confirmed most fields were already derived columns

The corpus check this piece required (below) turned up less unbuilt surface than the prior "five
unconfirmed fields" framing suggested: `HasPath` and `McpServerName` read straight off
`Data.Execution.ToolCall.Path`/`.McpServerName` — both already real, populated NORMALIZED columns —
and `SpawnsAgent` off `Data.Execution.Agent.SpawningToolCallId`, also already real (a measured
470/470 spawns resolve, `Data/CLAUDE.md`). Only `HasPattern`/`HasReplacement`/`HasFileText`/
`HasCommand` genuinely needed RAW `tool.execution_start.data.arguments` parsing, and this project has
its own cautionary tale for guessing a field name rather than verifying one
(`Ingestion/CLAUDE.md`'s own remarks on `EventEnvelopeParserV1` reading `ts` instead of `timestamp`
and silently losing 100% of the corpus). Verified against the live 35-session reference corpus rather
than guessed: `pattern` (`rg`/`grep`/`glob`), `old_str`/`new_str` (`edit`), `file_text` (`create`),
`command` (`powershell`). The check also caught a real wrinkle: `apply_patch`'s own `arguments` is a
JSON string (the whole patch body), never an object, so none of those four fields exist on it at all
— `ToolInvocationShapeLookup.HasField` guards on `ToolArguments.Kind == Object` before reading any of
them, reporting all four `false` for a string-shaped call rather than parsing the patch text to guess.

### `RulesInventoryClassifier` watches `PreferAOverB` and `ToolIsBanned` for real; `NotCheckable(reason)` was unreachable until mockup parity item #18

With a real `ToolInvocationShape` corpus in hand, `RulesInventoryClassifier.BuildClassifier` takes it
as a second parameter and actually attempts resolution for two shapes. `RuleShapeKind.PreferAOverB` —
`Rules.OperandResolver.ResolveTwoOperands` against the corpus, and `RuleStatementStatus.Watched` only
when *both* operands resolve to at least one real tool (`OperandResolutionLayer` other than
`Unresolved`). Verified against the live 35-session reference corpus:
`"Prefer querying codebase-memory-mcp over Glob/Grep/Read for navigation"` is a real `PreferAOverB`
match whose operand A ("codebase-memory-mcp") genuinely resolves through the `McpServerField` layer —
confirmed via a real browser rendering an `mcpCall` tape step for that same server — while operand B
("Glob/Grep/Read", after `RuleOperandText`'s own "for"-clause stripping) stays `Unresolved`: no single
real tool or `ToolRole` is named that. The statement therefore renders `CheckableNotYetBuilt`,
honestly — the mechanism is real and resolving, the live corpus simply has no `PreferAOverB` rule
phrased narrowly enough on both sides to watch yet (proven separately at the unit level:
`RulesInventoryClassifierTests` constructs a synthetic corpus where both operands do resolve).

`RuleShapeKind.ToolIsBanned` is piece 3's second slice: `Rules.OperandResolver.Resolve` against the
one operand a ban names, `Watched` when it resolves — no `ToolRole` involved, since the question a
ban's own adherence check needs answered ("was the named tool called at all", `Rules.BannedToolCheck`)
never needed a role in the first place, once the check was actually designed instead of assumed to
need `ToolVocabularyMismatchCheck`'s own `RuleToolMention` shape (`Rules/CLAUDE.md`'s two new
non-obvious-decision entries explain why that check does not fit a prohibition).

`RuleShapeKind.NeverReadPath` is piece 3's third slice: `Watched` unconditionally, the moment the
catalogue matches the shape — no `OperandResolver` call at all, unlike `PreferAOverB`/`ToolIsBanned`.
A path operand always produces a determinate real/no-access verdict against the `ToolCall` corpus
(`Rules.NeverReadPathCheck`), so there is no `Unresolved` state for it to fall through to the way a
tool-name operand has.

`RuleShapeKind.UseAAfterB` is piece 3's fourth slice: classified in the same branch as `PreferAOverB`
— `Rules.OperandResolver.ResolveTwoOperands` against the corpus, `Watched` only when both operands
resolve — since both shapes ask the identical question of their operand pair ("do these tool names
resolve"), just for a different downstream check.

`RuleShapeKind.AlwaysPassParam` is piece 3's fifth and final slice: classified in the same branch as
`NeverReadPath` — `Watched` unconditionally the moment the catalogue matches the shape, no
`OperandResolver` involved, since a parameter-key operand (unlike a tool-name operand) always produces
a determinate present/absent verdict against `Rules.ParamCarryingCall`'s own `ArgumentKeys`. This
closes the last piece-3 gap: every one of FR-34's five shapes now has a real classification path.
`RulesInventoryClassifier.BuildClassifier` therefore classifies every matched shape, and
every unmatched statement carrying a normative marker
(`UnmatchedStatementDisposition.CheckableNotBuilt`), as `RuleStatementStatus.CheckableNotYetBuilt`;
an unmatched statement carrying none (`UnmatchedStatementDisposition.NotCheckable`) still classifies
as `RuleStatementStatus.NotARule`. This closed the last piece-3 gap — every one of FR-34's five
shapes has a real classification path — but the caller-supplied `NotCheckable(reason)` variant still
went unconstructed anywhere in this file until mockup parity item #18, below.

### Mockup parity item #18: `NotCheckable(reason)`'s first real constructor is one narrow, real-corpus-grounded pattern

The item's own scoring (`docs/product-superpowers/prioritization/2026-08-21-mockup-parity-gaps.md`,
row #18) named the real gap correctly: "nothing in `RulesInventoryClassifier` has ever decided which
normative-but-unobservable *reason* to attribute to a statement — real design work". Rather than
guess a taxonomy, this story dumped every distinct statement `RuleShapeCatalogue.MatchAll` classifies
`UnmatchedStatementDisposition.CheckableNotBuilt` against the live local store (a throwaway console
tool, not a fixture) and read all nine real hits by hand. Eight ask something a future extension of
tool-name/path/parameter/call-ordering checking could plausibly answer some day (a repeated path, a
call-volume threshold, a call ordering, an argument key) — genuinely "not yet built", not
"unbuildable" — and stay `CheckableNotYetBuilt`. Exactly one is structurally different: this
repository's own root `CLAUDE.md` (`" Read ONLY files directly needed for the current task"`, also
carried verbatim by the live corpus's own dominant repository, `supahfly27/UpFront` — the same author
template, a real, independent hit) gates its obligation on whether a read was truly *needed for the
task* — content and intent, which Copilot's own event logs never carry (they record which tool was
called with which argument, never why). `RulesInventoryClassifier.TaskRelevanceObligation` is a
narrow regex over that one real pattern — `needed|necessary|relevant` immediately followed by
`for`/`to` "the/this/current task"/"request"/"ticket" — deliberately narrow enough that its three
real, adjacent-corpus neighbours (`"Do not re-read files already in context"`, `"Never explore the
codebase broadly before starting"`, and `"...based on topical relevance"`, which never gates on "the
task" itself) all still classify `CheckableNotYetBuilt`, proven by
`RulesInventoryClassifierTests`'s own regression cases using that exact real text. Verified against
the live local store both at the unit level and via a real `GET /api/rules-inventory` request: the
dominant repository's default rule-set version measured `checkableNotYetBuilt` 6 → 5 and
`notCheckable` 0 → 1 (17 rows total, unchanged), with the served `reason` naming why — no other row's
status moved. `web/src/routes/RulesInventoryPage.tsx`'s `StatusCell` already rendered a `notCheckable`
row's reason (`web/CLAUDE.md`'s own remarks); this story needed no frontend change to make that render
against real data for the first time.

This is a deliberately narrow slice, not a taxonomy: several real, adjacent statements in the same
corpus were genuinely ambiguous under closer reading (e.g. `"Fall back to Grep tool only for raw
text/config values not in the graph"` — arguably checkable via MCP-response outcome ordering, arguably
not, since "in the graph" is external knowledge) and were left `CheckableNotYetBuilt` on purpose, per
this item's own scoring ("the current fallback isn't wrong, just less precise, no user-facing harm
today") — a human can revisit them with more corpus evidence later.

### `GetDigest`'s seventh check reuses `GetRulesInventory`'s two corpora, scoped to the selected repository instead of corpus-wide

`Findings.BannedToolFinding` needs the same two things `GetRulesInventory` already builds — a
`Rules.RuleShapeCatalogue.MatchAll` pass over this repository's own rule statements and a real
`Rules.ToolInvocationShape` corpus — but `GetDigest` scopes every other check to
`repositoryScope.SelectedRepository`'s own sessions (`scopedSessionIds`), so this one does too:
`SessionRuleSetLookup.BuildAll(scopedSessions, scopedRawEvents)` and
`ToolInvocationShapeLookup.BuildAll(scopedToolCalls, scopedAgents, scopedRawEvents)`, both narrowed
to the selected repository, unlike `GetRulesInventory`'s deliberately corpus-wide scope (this file's
own remarks below on why that surface classifies every statement in the store). `scopedAgents` is a
new read this method did not need before — `ToolInvocationShapeLookup.BuildAll`'s `SpawnsAgent` flag
needs `Agent.SpawningToolCallId`, and nothing this method ran previously touched `Agent` at all.
`BannedToolFinding.Run` also takes `scopedToolCalls` directly (not through the check-shape layer,
which carries no `SessionId` by design) to attribute each violation to the sessions that actually
called the banned tool, the same split `RepeatedFileReadFindingCheck` already draws between its
generic operand and its own `ToolCall` read.

The resulting finding maps through the same `FindingEnvelope.From` mapper every other `GetDigest`
finding does, not `FromAdherence`: `BannedToolFinding` sets no `Finding.Resolution`, because FR-33's
`AdherenceFigure` is built for a two-operand percentage (`FromTwoOperands`) and a single-operand "was
this banned tool called" fact does not fit it (`Findings/CLAUDE.md`'s own remarks on
`BannedToolFinding`) — so this remains the first `FindingClass.RuleAdherenceToolChoice` finding
`GetDigest` ever serves, but still without a served `AdherenceFigure`, the same gap this file's
`FindingEnvelope.Adherence` remarks name as still open.

### `GetRulesInventory` classifies every statement in the corpus, not only the selected version's

`RuleShapeCatalogue.MatchAll` runs once over every distinct statement across every `SessionRuleSet`
in the store — every repository, not only `repositoryScope.SelectedRepository` — before `RulesInventory.
Build` ever narrows to one version. This is a strict superset of what `Build` will actually
deduplicate over (only the selected version's carrying sessions, all in one repository), which is
what guarantees `RulesInventoryClassifier`'s dictionary lookup never misses: every `occurrence.
Statement` `Build` looks up is provably present, so the classifier's own defensive
`InvalidOperationException` never fires on this call path. Harmless at this corpus' scale (a
measured 17 statements) — matching the same "read the whole table, reduce in memory" discipline
`GetDigest` already established as acceptable here (`Api/CLAUDE.md`'s "no aggregate scan" note
below).

### A missing rule-set version answers 404, the same as a missing session

`GetRulesInventory` returns `null` — mapped to 404 by the route handler, the same shape `GetSession`
already uses for a session id the store carries no row for — both when the store is empty
(`RulesInventory.MostRecentVersion` finds no session for the selected repository at all) and when a
requested `version` hash names one no session in that repository ever carried
(`RulesInventory.Build` throws `UnknownRuleSetVersionException`, caught here). Neither is a designed
empty state the way `RulesInventoryState.NoInstructionBlocks` is — that state only exists once a
version has actually been selected; "there is no version to select" is a different, earlier failure
this surface reports the same way `GetSession` reports "there is no session."

### `GetDigest`'s rule-coverage figure reuses `GetRulesInventory`'s own pipeline through a shared helper, and picks the selected repository's most recent version

Mockup parity item #15: the Digest masthead is corpus-wide, but a rule-set version is scoped to one
repository (`Rules.RuleSetVersionId`), so "the coverage bar" needed a version-scope decision. Two
readings existed — (a) the selected repository's own most recent version
(`RulesInventory.MostRecentVersion`, the exact default `GetRulesInventory` already opens on), or (b)
something scoped differently. (a) was chosen: every ranked finding on the Digest is already scoped to
one repository (`Findings.RepositoryScope`'s own remarks), so mirroring the Rules Inventory's own
default keeps the coverage bar a corpus-wide, deterministic figure with no new selection UI to build
— the same repository `BuildRepositoryScope` already resolves for `GetDigest`'s own findings, at that
repository's own newest rule set (so nothing here is retired or stale by construction).

`BuildRuleCoverageStatus` (new) computes it via `BuildRulesInventoryInputs` (new), a private helper
factored out of what used to be `GetRulesInventory`'s own inline sequence —
`SessionRuleSetLookup.BuildAll` → `RawToolArguments.ByCall` → `ToolInvocationShapeLookup.BuildAll` →
`RuleShapeCatalogue.MatchAll` → `RulesInventoryClassifier.BuildClassifier` — corpus-wide, the same
scope `GetRulesInventory` already uses (not the repository-scoped corpus `BuildFindingsForScope`'s own
piece-3 checks use). `GetRulesInventory` now calls the same helper rather than repeating the sequence
inline, so the two endpoints' four-way breakdowns, for the same version, can never be computed two
different ways — the "one served figure, never recounted differently on a second surface" discipline
`RulesInventoryEnvelope.cs`'s own remarks state for `RulesInventoryStatusCountsEnvelope`, now enforced
structurally across both routes rather than by two independent implementations happening to agree.
Verified against the live 35-session reference corpus: `/api/digest`'s masthead served
`{watched:1, checkableNotYetBuilt:6, notCheckable:0, notARule:10, total:17}` for the dominant
repository's default version, byte-for-byte the same four numbers `/api/rules-inventory`'s own
`statusCounts` served for that version.

This landed as real, non-trivial backend work rather than the item's own "3 (M)" prioritisation-doc
estimate: the estimate's own feasibility note ("`MastheadCounters` currently only stubs 'Rules not yet
analysed' corpus-wide") undersold both the domain-type change (`RuleCoverageStatus` had to become a
closed union, not a bare enum — `Findings/CLAUDE.md`'s own remarks) and the real question of which
rule-set version a corpus-wide masthead should reflect, which needed its own reasoned answer rather
than being a pure wiring task.

### `ApiHost.Build` returns an unstarted `WebApplication`

The caller (`AecoPostMortem.Cli`'s `serve` command) decides when to start it and how long to run
it. That is what keeps the host testable without a Kestrel listener staying up for the life of a
test run: a test starts it, makes a request, and stops it again, all inside one `[Fact]`.

### `127.0.0.1`, not `localhost`, and a camelCase enum on the wire

`UseUrls` binds `127.0.0.1` rather than `localhost` — Kestrel refuses a dynamic port (`--port 0`)
bound to the `localhost` host name, which the test suite needs to avoid claiming a fixed port
another test (or another `dotnet test` run) might also want, and `127.0.0.1` is what `localhost`
resolves to for the operator's browser regardless.

`AppStateKind` is serialised as a camelCase string (`"emptyStore"`, not `"EmptyStore"`) via
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` — the naming policy has to be passed to the
converter explicitly; it is not inherited from `ConfigureHttpJsonOptions`'s own camelCase property
naming. `ApiHostTests.The_kind_field_is_serialised_as_camelCase_on_the_wire` is a regression test
for exactly this: an earlier version of this host shipped `"EmptyStore"` because of the missing
naming policy, silently mismatching `web/src/api/appState.ts`'s `AppStateKind` union without either
side's own tests catching it (both sides mocked past the real wire format).

### The web shell is optional, never a hard dependency on Node

`ApiHost.Build`'s `webRootPath` parameter is resolved by the CLI's `ServeWebRoot.Resolve()`, which
walks up from the running executable looking for `web/dist/index.html` (the output of
`scripts/build-web.ps1`). `dotnet build` and `dotnet test` never run that script (`web/CLAUDE.md`),
so a machine that has only built the .NET solution has no web shell to serve; `serve` still answers
`/api/app-state`, it just falls through on `/` instead of returning `index.html`. This is why
`Build` accepts `webRootPath: null` as a normal case rather than throwing.

### `DigestEnvelope.From` takes a mapper, not a fixed factory

`DigestEnvelope.From(ProcessDigest, Func<Finding, FindingEnvelope>)` cannot assume every ranked
finding maps through `FindingEnvelope.From` — an adherence finding needs `FromAdherence` with its
`AdherenceFigure` instead (FR-33), and only the caller (which already resolved the operands) knows
which shape a given finding needs. The mapper preserves `ProcessDigest.RankedFindings`' order:
the ranking already happened in `Findings`, this only converts each entry to its wire shape. The
same mapper is reused for `ProcessDigest.InferredFindings` (FR-48, issue #52, S-42) — there is no
second, Inferred-only mapping function, because an Inferred finding needs exactly the same
`General`/`Adherence` shape decision any other finding does.

### `SessionsAffected` is served, not left for each client to re-derive

FR-41 (issue #44, S-36) ranks `RankedFindings` by distinct sessions affected, and S-36's edge case
makes that count the most prominent thing on a rendered row — a finding touching one session has to
read as an anecdote beside one touching thirty. `FindingEnvelope.SessionsAffected` carries that
number on the wire even though `Recurrence.Occurrences` technically already contains it: a client
counting its own distinct session ids would be re-implementing `ProcessDigest.SessionsAffected`, and
any drift between the two would show up as a row whose displayed count disagrees with the order it
is being displayed in. It is always computed by `ProcessDigest.SessionsAffected(finding)` inside the
three factories, never accepted as a parameter, so the served figure and the ranking cannot come
from two different rules.

### The "no aggregate scan" guarantee is enforced here, not only in `Findings`

`AecoPostMortem.Findings`' own `ProcessDigestStructureTests` proves `ProcessDigest.Build` cannot be
handed a live data source — but that project has no `Data` reference at all, so the guarantee costs
it nothing. **This** project does reference `Data` (`DiagnoseAppState`, `GetSession`, `GetDigest`),
which makes `MastheadEnvelope` the point on the masthead's path where a live `COUNT` could plausibly
leak in. `MastheadEnvelopeStructureTests` reflects over `MastheadEnvelope`'s public surface
(properties, method parameters and return types, following generic arguments down) and fails if any
of it mentions an `IQueryable`, an `Expression`, or any type out of `AecoPostMortem.Data` /
`Microsoft.EntityFrameworkCore` — that guarantee is about the *served wire shape*, and still holds:
`MastheadEnvelope` itself carries only plain numbers and dates, never a live query object.

`ApiHost.GetDigest`'s own `BuildMastheadCounters` is the deliberate, narrower scope decision this
project made instead of "counters maintained at ingest" (the aspiration `Findings.MastheadCounters`'
own doc comment states): nothing under `AecoPostMortem.Data`/`Ingestion` persists a running total
anywhere (no `StoreMetadata` column for it), so building that would mean a second piece of unbuilt
infrastructure ahead of the digest actually rendering anything. `GetDigest` instead reads
`Sessions`/`RawEvents`/`ToolCalls` into memory once per request and reduces them in C# — a real
corpus-wide read, not a query-time `COUNT`, but still well inside the measured 126 ms/million-row
budget (`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md`) at this
corpus' actual scale (a measured 56,138 RAW rows). Revisit with a real ingest-time counter if the
corpus ever approaches the 500-session/1M-event design target this measurement was taken against.

### `DigestState` serialises as its name, not an ordinal — `RuleCoverageStatus` no longer needs the same trick

`DigestState` is declared in `Findings` with no serialisation attributes of its own — domain types
stay serialisation-agnostic, the same separation `FindingEnvelope`/`SuggestionEnvelope` already draw.
`DigestEnvelope.State` carries its own `[JsonConverter(typeof(JsonStringEnumConverter))]` here
instead, so a client reads `"NotYetAnalyzed"` rather than an opaque integer for a state whose entire
point (S-36's Gherkin) is to be stated in words.

`Findings.RuleCoverageStatus` used to be a second bare enum sharing this same trick, but mockup
parity item #15 turned it into a closed `NotYetAnalyzed`/`Analyzed(RulesInventoryStatusCounts)`
union (`Findings/CLAUDE.md`'s own remarks) — a shape `JsonStringEnumConverter` cannot serialise at
all. `RuleCoverageStatusEnvelope` (`DigestEnvelope.cs`) is the wire projection instead, the same
`[JsonPolymorphic]`/`[JsonDerivedType]` mechanism `SuggestionEnvelope` uses for its own two states,
with a `"state"` discriminator (`"notYetAnalyzed"`/`"analyzed"`) rather than an enum member name.

### `GetSession` reads the derived tables directly, rather than re-deriving from RAW here

Two options existed when this story landed: have this endpoint replay RAW through
`ExecutionRecordBuilder` itself, or read the `Data.Execution` tables and let a later story's writer
populate them. This project took the second path — `GetSession` queries `context.Sessions`/`Turns`/
`ToolCalls`/`Agents`/`Skills`/`Hooks` directly, rather than duplicating a second, partial (no
`Skill`/`Hook`) reconstruction path inside `Api` that the eventual ETL story would have to reconcile
with or replace. That writer has since landed —
`AecoPostMortem.Ingestion.NormalizedLayerWriter`, called by both `ingest` and `rebuild`
(`AecoPostMortem.Ingestion/CLAUDE.md`) — so this read path is live against a real store today, not
only against `SessionRouteTests`' own seeded fixtures (which still exist, and still exercise the read
path independently of the writer, the same stand-in `OwnershipTests` uses).

### `SessionTokenFiguresEnvelope` closes the same gap `SuggestionEnvelope` closed for FR-56

`Findings.SessionTokenFigures` was published (S-11, issue #20) ahead of anything that would render
it, deliberately left unwrapped by any envelope until "S-08's masthead is the story that will call
`From` and render its two states" (`Findings/CLAUDE.md`). This is that story: `SessionTokenFiguresEnvelope`
is a closed two-shape union behind a private constructor — `Observed` and `NotRecorded` — the same
`[JsonPolymorphic]`/`[JsonDerivedType]` mechanism `SuggestionEnvelope` uses, so "context size at end"
is never a nullable field a client could misread as zero.

### `SessionMastheadEnvelope.ElapsedMs`/`SessionTapeStepEnvelope.OffsetMs` carry milliseconds, not a serialised `TimeSpan`

The domain layer (`Findings.SessionMasthead.Elapsed`, `SessionTapeStep.Offset`) uses `TimeSpan`;
the envelope converts both to `long`/`long?` milliseconds instead of letting `System.Text.Json`
serialise `TimeSpan` directly, so a client reads a plain number rather than needing to agree with
the server on a `TimeSpan` text format. `DateTimeOffset` (`SessionTapeStepEnvelope.Timestamp`) is
left as-is — `MastheadEnvelope.SpanStart`/`SpanEnd` already establish that this type serialises
losslessly and needs no format agreement of its own. Mockup parity item #14's
`SessionMastheadEnvelope.StartedAt`/`.EndedAt` follow that same `DateTimeOffset` precedent rather
than joining `ElapsedMs`'s millisecond convention — there is no duration here to convert, only a
timestamp, and `StartedAt`/`.EndedAt` pass `Findings.SessionMasthead.StartedAt`/`.EndedAt` straight
through unchanged, the same passthrough `SessionMastheadEnvelope.From` already gives every other
masthead field.

### `SessionRecordingStatusEnvelope` widens `GetSession`'s read on purpose, and states why that is not the duplicate-reconstruction-path this project already ruled out

FR-21 part 3 of 3 (S-53, issue #17): the "reconstruction failed, states what was skipped" scenario
has exactly one real signal already built anywhere in this repository — `Ingestion.
ExecutionRecordBuilder`'s `SpawnResolutionCheck` (FR-9), which counts a `subagent.started` event
whose `toolCallId` never resolves against a spawning `task` call rather than silently dropping it.
Nothing in `Data.Execution.Agent` records an unresolved spawn (an unresolved one is excluded from
`Agent` entirely — `Ingestion/CLAUDE.md`), so there is no way to answer Scenario 4 from the derived
tables `GetSession` already reads; the only place the signal exists is a fresh pass over the
session's own RAW events. `GetSession` therefore reads `context.RawEvents` a second time, scoped to
the one session being requested (bounded, not a corpus scan), and calls `ExecutionRecordBuilder.
Build` — but reads only `.SpawnResolutionCheck` off the result, discarding the `Turns`/`ToolCalls`/
`Agents` it also returns. That is the deliberate line: the "GetSession reads the derived tables ...
rather than re-deriving them from RAW" remark above is about not maintaining two competing paths
that both produce the masthead/tape's *rows* — this second read produces nothing that overlaps with
that; it exists only to answer one yes/no diagnostic question the derived tables cannot answer today.

`SessionRecordingStatusEnvelope` itself is a closed three-shape union behind a private constructor
— `Complete`, `IngestIncomplete`, `ReconstructionFailed { Skipped }` — the same `[JsonPolymorphic]`/
`[JsonDerivedType]` mechanism `SessionTokenFiguresEnvelope` and `SuggestionEnvelope` already use, so
a client reads which of the three states applies from the wire `"kind"` discriminator
(`"complete"`/`"ingestIncomplete"`/`"reconstructionFailed"`) rather than inferring it from which
optional fields happen to be present. `SessionEnvelope.Status` is `required` — every served session
carries one of the three, never an implicit fourth "unspecified" state.

### `SessionTapeStepEnvelope.Kind` and `OwnerKind` get their camelCase wire form from the global converter, not a per-property override

Unlike `MastheadEnvelope.RuleCoverage`/`DigestEnvelope.State` (which opt out of the global naming
policy via an explicit, un-parameterised `[JsonConverter(typeof(JsonStringEnumConverter))]` so a
client reads the exact word, e.g. `"NotYetAnalyzed"`), `SessionTapeStepKind` and
`AecoPostMortem.Data.Execution.OwnerKind` carry no such override — there is no "must read as this
exact English word" requirement here, so both fall through to the
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` `ApiHost.Build` already registers globally,
the same as `AppStateKind`. A step reads `"toolCall"`/`"mcpCall"`/etc., never `"ToolCall"`.

### `StepEvidenceLookup` matches a step to its raw event by the same field each envelope already carries, not a new identity

Neither `Turn` nor `ToolCall` carries a foreign key back to the `RawEvent` that produced it
(`AecoPostMortem.Data/CLAUDE.md` — the payload stays authoritative and nothing lifts an envelope's
own `id` into a NORMALIZED column). `StepEvidenceLookup.Find` instead matches a step's own
`SessionTapeStep.StepId` against exactly the field `SessionRecording.cs` already documents as that
step's source: a turn's `assistant.turn_start.data.turnId`, a tool call's
`tool.execution_start.data.toolCallId`, or a skill/hook's own envelope `id`. This is a lookup by an
identity that already exists, not a second scheme invented for this story.

### A step's Raw tab answers 200 with `Skipped`, never a 404 — the edge case's own words

`StepEvidenceLookup.Find` cannot fail to find *a* result — when no raw event matches, it returns
`RawStepEventEnvelope.Skipped` and `ThinkingEnvelope.Unavailable`, both with a stated reason, rather
than `null`. `ApiHost.GetStepEvidence` returns `null` (→ 404) only when the *session* does not
exist; a step id that matches nothing within a real session still gets 200 with the skipped state.
This mirrors `SilentCheckEnvelope`'s own discipline of never synthesising an entry, in the opposite
direction: here the type always produces *something*, because "shows that fact rather than an empty
panel" (the story's own edge case) requires an explicit value for a client to render, not an HTTP
status a client would have to interpret.

### Mockup parity item #17 is deliberately narrow: two finding shapes covered, eight left as "not attempted"

The mockup shows a flag on the exact tape row a finding is *about* — but `Finding` (`Findings/CLAUDE.md`'s
own "the finding record has no `Id` and no `SessionId`" note) carries no step-level identity at all,
only `Recurrence.Occurrences` (which sessions, not which steps). Rather than force every
finding-producing check into one taxonomy, this story asked of each: does its own `Evidence` name an
identity a real `ToolCall`/`Hook` row can be matched against *exactly*, with no guessing? Two do:

- **`HookFailureFinding`** — `Recurrence.Key` is the hook's own name (`HookFailureFinding.cs`'s own
  `group.Key`), the identical value `Data.Execution.Hook.Name` carries. A failed `Hook` row of that
  name in this session is unambiguously one of the pairs the finding's own corpus-wide count was
  built from.
- **`FailedToolCallsFinding`**/**`ToolFailureClusterFinding`** — both carry the exact tool identity
  their rate was computed over on a `toolIdentity` evidence field (`ToolCallOutcome.ToolIdentity`,
  verbatim `ToolCall.ToolName`). Every failed `ToolCall` of that identity in this session is
  unambiguously part of the rate — never only "the first" or "the most recent" one, since the
  evidence is an aggregate, not a single call's own identity. This is the conservative reading this
  story picked deliberately: attaching to every confidently-identified call, not fewer, weakly-guessed
  ones (per this story's own instruction to resolve a genuine fork conservatively).

Eight finding-producing checks are left uncovered, honestly, rather than guessed at:
`RepeatedFileReadFindingCheck` (its own recurrence key is a path touched by potentially many reads
across many sessions — which specific read event a flag would attach to is genuinely ambiguous, the
same "which of N reads" question this item's own prioritisation-doc row named), `PhaseChurnFinding`
(a whole-session aggregate over declared intents — `Findings/CLAUDE.md`'s own "no single object" note
for why its recurrence key is the session id, not a sub-object any one step is more "about" than
another), `AbortedTurnFinding` (arguably attachable to its own aborted `Turn`'s `Prompt` step, but
left out this pass — a turn's own tape step is a `Prompt`, not the point of abortion itself, and this
story stopped at the two shapes with no such wrinkle), `InterruptionLoadFinding` (one finding per
whole analysis run, `Recurrence.Key = "interruption-load"` — no per-step identity at all), and
`BannedToolFinding`/`NeverReadPathFinding`/`UseAAfterBFinding`/`AlwaysPassParamFinding` (every one of
these names a tool identity or path in its own evidence the same shape a tool-failure finding does,
and could plausibly be added the same way in a later pass — deliberately left out here only because
none of the ten check orchestrators `BuildFindingsForScope` runs actually produces one on the live
reference corpus today, `Api/CLAUDE.md`'s own status notes, so there was no real data to verify the
join against; a future pass should extend `SessionTapeStepFindingLookup` rather than replace it).

This is the same "honest narrow slice, not a forced full taxonomy" discipline mockup parity item #18
(PR #126) already established in this exact backlog — see that row's own remarks. Verified against
the live 35-session reference corpus, not only at the unit level: a real `GET /api/sessions/{id}`
request for a session in `supahfly27/UpFront` served 20 flagged steps out of 2,249 — the real
`sessionStart` hook-failure step, and every real failed `view`/`grep`/`glob` call in that session,
each carrying its own correct finding headline and none other — confirmed both via the raw JSON
response and via the real browser's own accessibility tree (`role="img"`, `aria-label="Flagged: …"`)
on the matching row.

### Thinking is resolved only for a Prompt step, and only from the main thread

`StepEvidenceLookup.FindThinking` scans every `assistant.message` between a turn's own
`turn_start` and the next `turn_start` (or end of session) for `reasoningText`
(readable) or `reasoningOpaque` (provider-encrypted, never readable) — the exact distinction the
mockup's own footer explains (a measured 3.5%–90.3% split depending on model). Only main-thread
messages count (`envelope.AgentId is null`), the same ownership split
`Ingestion.ExecutionRecordBuilder.WalkTurns` applies to its own output-token accumulation: a
subagent's reasoning belongs to its own step's turn context, not its parent's. Every other step kind
(`ToolCall`/`McpCall`/`Skill`/`Hook`) answers `Unavailable` with a fixed reason — "Thinking is
recorded per assistant message" — never attempting a lookup, matching the mockup's own wording for
selecting a non-assistant step.

### An encrypted step names its model and carries the session's own per-model readable share, computed session-wide (FR-23, S-10, issue #19)

The Thinking tab's empty state has to explain a fact the operator could otherwise mistake for a
missing thought: on `gpt-5.4` the split is a measured 3.5% readable against a measured 88.2% on
`claude-sonnet-4.5` (PRD FR-23, the recorder mockup's own worked example) — a corpus-wide constant
would misstate either model's real behaviour, and an average across two models a session actually
used would misstate both at once (the story's own edge case). `FindThinking` therefore does two
things once it sees `reasoningOpaque` and no readable text: it reads that same message's own
`model` field (`assistant.message.data.model`, present on the mockup's own raw payload, alongside
`reasoningOpaque`) to name the model in `Reason` when one is present, and it calls
`ReasoningReadabilityByModel(ordered)` — a *second*, session-wide scan (not bounded to the current
turn's window, unlike the rest of `FindThinking`) over every main-thread `assistant.message` that
carries any reasoning at all, grouped by `model`. A message with reasoning but no `model` field is
excluded from the breakdown entirely rather than folded into an invented "unknown" bucket — there is
no model to attribute it to, and no acceptance scenario asks for a fourth figure. The result is
ordered by model name (`StringComparer.Ordinal`) for deterministic output (PRD §3.8), and only ever
attached to the `Unavailable` shape for the provider-encryption reason — the "no raw event" and
"wrong step kind" `Unavailable`s carry no `ReadabilityByModel` at all, since there is no per-model
encryption question to answer there. `ModelReasoningReadability.ReadableSharePercent` is a computed
property, not a settable field — the same "a rate never appears without its counts" reasoning
`AecoPostMortem.Rules.FailureRate.Percentage` already documents for its own percentage.

### `SubagentOutputLookup` never reads a `tool.execution_complete` result, so the parent's stub cannot leak through by construction

The data map (`docs/product-superpowers/discovery/2026-08-16-copilot-ingestion-data-map.md`) measured
`read_agent` completions at a median 48 characters, ending in the literal marker `"(Full response
provided to agent)"`, against subagent reports whose own median is far longer — "the parent's log
truncates the subagent's report... a reader that follows the parent's tool result sees a stub." A
filter that excluded that marker string would still be a filter a future change could weaken or
bypass. `SubagentOutputLookup.Find` instead never reads a `tool.execution_complete` event at all —
its only event-type filter is `assistant.message` — so the stub has no path into
`SubagentOutputEnvelope.Present.Text` to be excluded from in the first place. It takes the last
matching message by `RawEvent.Sequence`, matching Scenario 1's own wording ("the last assistant
message bearing that agent's id"), and never a `Turn`-scoped window the way `FindThinking` uses for
a prompt step — a subagent's `assistant.message` stream is not divided into turns the way the main
thread's is (`AecoPostMortem.Ingestion/CLAUDE.md`, `ExecutionRecordBuilder`'s own turn-tracking
covers only the main thread).

### A failed subagent's lane is `Failed`, unconditionally — the output lookup never runs

Scenario 4's own wording ("the failure and its recorded error are shown") reads as: once
`Data.Execution.Agent.Outcome` is `AgentOutcome.Failed`, that is the lane's whole answer.
`SubagentOutputLookup.Find` checks `Outcome` first and returns before ever scanning `sessionEvents`
— the same "the more urgent, more specific claim wins" ordering
`SessionRecording.DetermineStatus` already gives its own two checks (`Findings/CLAUDE.md`). A
missing `Agent.Error` (`subagent.failed.data.error` was not recorded on that event) still produces a
`Failed` shape, with a fixed fallback sentence, rather than falling through to `NotRecorded` —
Scenario 4 does not admit a fourth "failed with nothing to say" state.

### `MonitorComparisonEnvelope` reuses `RuleSetVersionEnvelope` and `AdherenceFigure` verbatim, no third figure shape

FR-39 Scenario 2 ("the session count on each side is as visible as the percentage") is satisfied by
reusing two contracts this project already serves elsewhere, rather than inventing a
`MonitorComparisonEnvelope`-specific figure: `BeforeVersion`/`AfterVersion` are the same
`RuleSetVersionEnvelope` `RulesInventoryEnvelope.cs` (S-22) already carries `SessionCount` on, and
`Before`/`After` are `Findings.AdherenceFigure` directly — the identical domain type
`FindingEnvelope.Adherence.Figure` already serialises. A client that already renders one of those
two shapes elsewhere in this app needs no new parsing logic to render this one.

## Status

The response envelope contract (`FindingEnvelope`, `SuggestionEnvelope`, `SilentCheckEnvelope`,
`DigestEnvelope`, `MastheadEnvelope`, `RepositoryScopeEnvelope`) is now served for real:
`GET /api/digest` (`ApiHost.GetDigest`) assembles a live `ProcessDigest` from the store — seven of the
eight waste/missing-capability/adherence check orchestrators, `MastheadCounters` computed corpus-wide
at request time (not maintained at ingest — see `GetDigest`'s own remarks on why that is still inside
budget at this corpus' scale), and a `RepositoryScope` defaulting to whichever repository carries the
most sessions. Verified end to end against the live 35-session reference corpus: 295 ranked findings
across the six waste/missing-capability checks, including the real `sessionStart` hook failure (25 of
25 sessions, error text read straight from RAW) and a real two-repository corpus exercising the
scope's own filtering — and a real browser renders `web/`'s `DigestPage` against it with no frontend
change, the exact promise `web/CLAUDE.md` recorded ahead of this wiring. `ToolFailureClusterFinding`
is not run here — it needs a mandating rule, which real rule extraction at scale (S-20) does not
populate yet, so it stays a documented gap alongside the Rules Inventory and Monitor comparison
endpoints below. The app-state endpoint and host (`AppStateReport`, `ApiHost`) that S-48 adds were the
first real endpoint this project shipped; `GetDigest` is the second.

`BannedToolFinding`, the seventh, adds zero findings on this corpus: verified across every one of the
dominant repository's 23 real rule-set versions (the same set piece 3's first slice already exhausted
for `PreferAOverB`) that no rule statement anywhere in the corpus is `ToolIsBanned`-shaped at all —
the nearest candidate, `"Use sub-guides (listed above) for context — avoid re-reading entire
codebases."`, correctly does not match (the catalogue's `avoid` pattern requires `avoid`/`refrain
from` immediately followed by a use/call/invoke/run/query verb, which "avoid re-reading" is not), and
renders `CheckableNotYetBuilt`. This is the mechanism reporting an honest empty state, not a bug —
proven separately at the unit level (`BannedToolCheckTests`, `BannedToolFindingTests`,
`RulesInventoryClassifierTests`) with a synthetic corpus where a banned tool genuinely was called.

`NeverReadPathFinding`, the eighth (piece 3's third slice), is the first piece-3 adherence check to
find a real signal on this corpus rather than an honest empty state: the dominant repository carries
a real `NeverReadPath` rule (`` Never read `UpFront.Data/Migrations/` unless the task is explicitly
about migrations ``, phrased two ways across rule-set versions), and 99 real tool calls across the
corpus touched a path under it — verified via the real store's own `tool_call.path` column and via a
real browser rendering both the `/rules` page's `Watched` badge and two real `RuleAdherenceToolChoice`
findings on `/` (`DERIVED`, 4 sessions each), zero frontend changes needed. Segment-boundary matching
was verified not to false-positive on the lookalike `UpFront.Auth.Data/Migrations/` directory, which
shares no contiguous substring with the rule's own operand.

`UseAAfterBFinding`, the ninth (piece 3's fourth slice), closes the last of piece 3's three remaining
shapes and, like `BannedToolFinding`, adds zero findings on this corpus — but this time not because
no matching statement exists: the dominant repository carries a real `UseAAfterB`-shaped statement
(`` Use `get_code_snippet` after `search_graph` to read function source ``), the catalogue genuinely
matches it, and `RulesInventoryClassifier` renders it `CheckableNotYetBuilt` because at least one
operand stays `Unresolved` against this corpus' own tool vocabulary — verified via a real browser
rendering that exact row on `/rules`. The originally scoped "known complexity" (no existing check
shape carries timing) turned out to be overly pessimistic, the same way piece 2's own "five
unconfirmed fields" framing did: `Data.Execution.ToolCall.StartedAt` is already a real, populated
ISO-8601 column, ordinally sortable, so ordering needed a second generic plain-input shape
(`Rules.TimedToolCall`) rather than any new RAW parsing or `Ingestion` work.

`AlwaysPassParamFinding`, the tenth (piece 3's fifth and final slice), closes the last piece-3 gap and
completes FR-34's five shapes. Unlike every prior `RuleAdherenceToolChoice` finding here, it needed no
`Data.Execution.ToolCall` read at all — `Rules.ParamCarryingCall` already carries `SessionId` (built
specifically for this check, not shared the way `Rules.ToolInvocationShape` is) — and it filters to
`SpawnsAgent` calls only, the one structural population its own operand can name without guessing
which tool a rule's stripped-away qualifying clause meant (`Rules/CLAUDE.md`'s own remarks). Verified
against the live 35-session reference corpus: the one real `AlwaysPassParam`-shaped statement found
during scoping — this repository's own rule, "always pass an explicit model param when dispatching a
subagent" — belongs to a session outside the dominant repository (`supahfly27/UpFront`) this corpus'
endpoints default to, so `/api/digest` and `/api/rules-inventory` both render unchanged for that
scope — zero new findings, an honest result, not a bug — confirmed via a real browser session and
proven separately at the unit level with a synthetic corpus where a violation genuinely fires.

FR-48 (issue #52, S-42) added `FindingEnvelope.ProvenanceLabel` (required on every shape) and
`DigestEnvelope.InferredFindings` (served separately from `RankedFindings`, mirroring
`ProcessDigest`'s own split) — both now live through `GetDigest`, and `web/src/digest/ProvenanceBadge.tsx`
(S-54, issue #45) is a real consumer of the shape against real data.

FR-33 (issue #38, S-24) made the adherence shape carry one `required AdherenceFigure Figure`. Every
finding `GetDigest` serves today, `BannedToolFinding`'s `RuleAdherenceToolChoice` findings included,
still maps through `FindingEnvelope.From` (the `General` shape) rather than `FromAdherence` — FR-33's
`AdherenceFigure` is built for a two-operand percentage (`FromTwoOperands`), and a single-operand "was
this banned tool called" fact does not fit it (`Findings/CLAUDE.md`'s own remarks on
`BannedToolFinding`). So `FromAdherence`/`AdherenceFigure` remain contract-only in practice even
though a live adherence-class finding now exists: a check that produces one needs a two-operand
comparison, the same gap S-35's Monitor comparison is built for but not yet wired to a live route
(below). Because the figure's percentage is computed from its operands and the envelope member is
`required`, the endpoint that eventually produces one inherits the guarantee without opting into it.
`web/src/digest/AdherenceFigureBlock.tsx` is the real rendering consumer, reached through `FindingRow`
once a check produces one.

`GET /api/rules-inventory` (`RulesInventoryEnvelope.cs`, `ApiHost.GetRulesInventory`, S-22, issue
#35, FR-40) is now served for real: `SessionRuleSetLookup.BuildAll` resolves the whole store's
`RawEvent`s into `SessionRuleSet`s (the corpus-wide extraction run `Ingestion/CLAUDE.md`'s own status
note names as still missing), `RulesInventoryClassifier` classifies every distinct statement once,
and the selected repository defaults the same way `GetDigest`'s `BuildRepositoryScope` already does
— this surface has no repository selector of its own (`web/CLAUDE.md`). Verified end to end against
the live reference corpus: this repository's own `CLAUDE.md`/`AGENTS.md` rules render as 17
statements, and a real browser renders `web/`'s `RulesInventoryPage` against it with no frontend
change — the same promise `web/CLAUDE.md` recorded ahead of this wiring. A live re-check at the time
of mockup parity item #18 (below) measured 1 watched / 5 checkable — not yet built / 1 not checkable /
10 not a rule (the corpus, and piece 3's own `NeverReadPath` match, has grown since this note was
first written); the one `notCheckable` row is that item's own real, narrow addition.

`ToolInvocationShapeLookup.cs` (piece 3, first slice) closed the missing-corpus gap this section once
documented: `GetRulesInventory` now also builds a real `ToolInvocationShape` corpus corpus-wide and
hands it to `RulesInventoryClassifier`, which actually attempts resolution for `PreferAOverB` matches
— see the two non-obvious decisions above for the real field names verified, the real wrinkle
(`apply_patch`'s string-shaped arguments) the corpus check caught, and why the live corpus's own
`PreferAOverB` rule (`supahfly27/UpFront`'s "Prefer querying codebase-memory-mcp over Glob/Grep/Read
for navigation") resolves one operand for real but still renders `CheckableNotYetBuilt` overall — no
single real tool or role is named "Glob/Grep/Read".

Piece 3's second slice (`BannedToolCheck.cs`, `Rules/CLAUDE.md`) closed the `ToolIsBanned` gap this
paragraph once named: turning a ban into a real verdict did not need a `ToolRole` after all, once the
check was actually designed rather than assumed to reuse `ToolVocabularyMismatchCheck`'s own
`RuleToolMention` shape (`Rules/CLAUDE.md`'s two new non-obvious-decision entries). `Rules
InventoryClassifier` now watches a `ToolIsBanned` match whose single operand resolves, and
`Findings.BannedToolFinding` is wired into `GetDigest` as its seventh check — the real corpus has no
`ToolIsBanned`-shaped statement to exercise either against (this file's own `GetDigest` status note,
above), so both are proven at the unit level with a synthetic corpus instead, the same "the mechanism
is real, the live corpus just doesn't happen to exercise it yet" pattern the first slice's own
`PreferAOverB` finding already established.

`GET /api/sessions/{sessionId}` (`SessionEnvelope.cs`, `ApiHost.GetSession`, S-08, issue #15) is the
second real endpoint: FR-21's masthead and tape, read through `Data.Execution` and assembled by
`Findings.SessionRecording.Build`. `web/src/routes/SessionPage.tsx` is the client. Returns 404 for a
session id the store carries no `Session` row for; a session with rows but no steps still serves its
masthead with an empty `Steps` list. It now also serves `SessionEnvelope.Findings` — FR-21 part 2 of
3's chip row (S-52, issue #16), assembled from `Findings.SessionFindings.For`.

Mockup parity item #4 closed the "empty chip row" gap named just above: `GetSession` now reads every
session sharing this session's own `Session.Repository` and runs the identical ten check
orchestrators `GetDigest` runs for a whole repository — factored into a new private helper,
`ApiHost.BuildFindingsForScope`, so the two callers share one orchestration sequence rather than
each re-typing the same ten calls — then filters the combined result down to this one session via
`Findings.SessionFindings.For`. A session with a `null` `Repository` scopes to every other session
that also carries none, the same equality-based grouping `BuildRepositoryScope`'s own fallback uses
when no repository is recorded anywhere in the store. `SessionRouteTests` proves both the positive
case (a real hook-failure violation in this session's own repository serves a non-empty chip) and the
negative one (a violation in a different repository never leaks into this session's chip row), the
same real-filter guarantee `DigestRouteTests` already proves for `GetDigest`.

Mockup parity item #17 added `SessionTapeStepEnvelope.Findings` and `SessionTapeStepFindingLookup`:
a small flag on the exact tape row a finding is about, for the two finding shapes whose own evidence
names an identity (a tool name, a hook name) this session's own `ToolCall`/`Hook` rows can be matched
against exactly — see that file's own remarks below for the full scoping reasoning and which eight
finding-producing checks were deliberately left uncovered. `web/src/session/Tape.tsx` (`web/CLAUDE.md`)
is the real rendering consumer: a small `role="img"` flag on the matching row(s), naming every
flagging finding's own `headline`. Verified against the live 35-session reference corpus: a real
`GET /api/sessions/{id}` request for a session in the dominant repository served 20 real flagged
steps — the real `sessionStart` hook failure and every real failed `view`/`grep`/`glob` call in that
session — confirmed both in the raw JSON and in a real browser's own accessibility tree.

FR-21 part 3 of 3 (S-53, issue #17) added `Status` (`SessionRecordingStatusEnvelope`) to the same
envelope: `GetSession` now also runs the session's own RAW events through `Ingestion.
ExecutionRecordBuilder` for its `SpawnResolutionCheck` alone (see the non-obvious decision above),
so a session with an unresolved subagent spawn is served as `reconstructionFailed` and one with no
recorded end as `ingestIncomplete`, distinctly from the ordinary `complete` case.

`GET /api/sessions/{sessionId}/steps/{stepId}?kind=` (`StepEvidenceEnvelope.cs`,
`StepEvidenceLookup.cs`, `ApiHost.GetStepEvidence`, S-52, issue #16) is the third real endpoint:
FR-21 part 2 of 3's inspector — the Thinking and Raw tabs for one selected step, resolved straight
from the session's own `RawEvent`s. Unlike the session/digest endpoints, this one needs no
not-yet-wired caveat: `StepEvidenceLookup` reads `RawEvent` rows that already exist the moment a
session has been ingested, so this endpoint is fully live against any real store today. Returns 404
only when `sessionId` names no session at all; a step whose raw event cannot be found still answers
200 with `RawStepEventEnvelope.Skipped` (never a 404) — "shows that fact rather than an empty panel"
is this story's own edge case, and a 404 would have forced the client to guess why. The Detail tab
needs no endpoint of its own: `web/src/routes/SessionPage.tsx` renders it straight from the tape
step already in hand.

FR-23 (S-10, issue #19) widened the Thinking tab's `Unavailable` shape: an encrypted step's `Reason`
now names the model where the raw event carried one, and `ReadabilityByModel` carries the session's
own measured readable share per model (see the non-obvious decision above) — fully live against any
real store today, the same `RawEvent`-reading path `StepEvidenceLookup` already used. No `Data`/
`Findings` reference was added; the whole figure is computed from `RawEvent`s already in hand.

`SessionTapeStepEnvelope.PluginName`/`.PluginVersion` (FR-25, S-12, issue #21) carry a
`SessionTapeStepKind.Skill` step's plugin straight across from `Findings.SessionTapeStep` — no
resolution of their own, the same passthrough `From` already does for `Label`. Both are `null` for
every other step kind. No endpoint change: `GetSession`'s query already read `context.Skills`
(S-08), so this story only widened the wire shape two fields, plus one row in
`SessionTapeStepEnvelope.From`.

FR-22 (S-09, issue #18) added `SessionEnvelope.Lanes`: `GetSession` resolves one
`SessionAgentLaneEnvelope` per `Data.Execution.Agent` it already reads (S-08's own `agents` query,
unwidened), pairing each with `SubagentOutputLookup.Find(rawEvents, agent)` over the same
`rawEvents` list S-53 already reads for its `SpawnResolutionCheck` — a third narrow reuse of that
one read, not a fourth RAW query. Lanes are ordered by `StartedAt` then `AgentId` so the served list
is deterministic. `web/src/routes/SessionPage.tsx`'s `AgentLanes` is the client, rendering each
lane's identity, outcome and `SubagentOutputEnvelope` — never falling back to a `read_agent` tool
call's truncated result, since `SubagentOutputLookup` never reads one.

`MonitorComparisonEnvelope.cs` (S-35, issue #43, FR-39) is now served for real (piece 4):
`GET /api/monitor-comparison?before=&after=` (`ApiHost.GetMonitorComparison`) resolves the same
default repository `GetDigest`/`GetRulesInventory` already default to (`BuildRepositoryScope`), since
the wire contract carries only bare version hashes — no repository, no rule. It picks the first
`Rules.RuleShapeKind.PreferAOverB` match among the statements the `after` version's own carrying
sessions carried (the only shape `Findings.MonitorComparison.Compare` takes two operands for), scopes
a real `ToolInvocationShape` corpus to each side's own sessions separately (a new `SessionIdsCarrying`
helper, since neither `RuleSetVersioning.Compute` nor `RuleSetVersion` carries a version's full member
list, only its first/last session), and calls `Compare`. `null` (404) for no repository, no such
adjacent pair (`UnknownRuleSetVersionException`/`NonAdjacentRuleSetVersionsException`, caught), or no
`PreferAOverB` statement to compare — `MixedRuleSetVersionException` is not caught here because it is
unreachable through this caller: both ids are always constructed against the same selected repository.
`web/src/digest/MonitorComparisonBlock.tsx` and `web/src/api/monitor.ts` are the real consumers,
unchanged — no frontend edit was needed, the same "endpoint lands, contract already there" pattern
`GetDigest`/`GetRulesInventory` established. No route in `web/src/App.tsx` mounts the block yet
either, a separate, still-open gap (`web/CLAUDE.md`).

Verified against the live 35-session reference corpus, real hashes end to end: a real adjacent pair
(`2851DBD3…`/`7B9F2536…`, `supahfly27/UpFront`) answers 200 for real, its `PreferAOverB` statement
("prefer specific files over entire directories") both operands `unresolved` — a null percentage on
each side, honestly, since neither operand names anything `OperandResolver` can match against a real
tool. A non-adjacent real pair (one version apart) answers 404, matching
`NonAdjacentRuleSetVersionsException`'s own refusal. **A real defect surfaced by this exercise was
fixed as a follow-up, scoped and approved separately from this wiring**: `RuleSetVersionAdjacency.
RequireAdjacentPair`'s own ordering — `FirstSessionId` under ordinal string comparison — had no
relationship to chronological order once session ids are random UUIDs (this corpus' own shape), so
"adjacent" per that primitive was not the same as "chronologically next" in practice; querying the
corpus' own `availableVersions` (chronologically ordered) two entries apart in that list answered 404
(non-adjacent per the ordinal check) even though they were chronological neighbours. `Rules.
RuleSetVersion.FirstSessionStartedAt` (`Rules/CLAUDE.md`'s own remarks) is the fix: both
`RuleSetVersioning.Compute`'s own overall ordering and `RequireAdjacentPair`'s re-sort now use that
field instead of `FirstSessionId` text. Confirmed against the live corpus, before and after: of 22
real consecutive version pairs in the dominant repository, 17 flipped from wrongly-refused (404) to
correctly succeeding.

Mockup parity item #2 (the per-finding session strip) added `RepositoryScope.SessionIds`/
`RepositoryScopeEnvelope.SessionIds` — every session id in the currently selected repository,
chronologically ordered by `Session.StartedAt` (`ApiHost.BuildRepositoryScope`, the "`RepositoryScope
Envelope` mirrors..." remark above). This was a genuine, verified gap, not the pure-frontend change
the mockup-parity prioritisation doc's own effort estimate assumed: before this change, neither
`MastheadEnvelope` nor `DigestEnvelope` served any ordered, full session list, only a bare
`SessionCount` — there was no way for a client to know *which* positions in a strip of N sessions
should be lit. `GetDigest` now builds `scopedSessionIds` from `repositoryScope.SessionIds` directly
rather than re-deriving the same repository filter a second time, so the served strip and the
sessions every check ranks over are structurally guaranteed to agree. Verified against the live
35-session, two-repository reference corpus: a real browser renders a real chip bar (dominant
repository, `web/src/digest/SessionStrip.tsx`) whose cell count matches that repository's own session
count and whose lit positions match each finding's own `sessionsAffected` figure.

Mockup parity item #5 added `FindingEnvelope.Headline`, required on every shape and set identically
by all three factories (`From`/`FromAdherence`/`FromBaseRate`) from `Finding.Headline` — this project
computes nothing new, the same passthrough its own `Evidence`/`Recurrence`/`SessionsAffected` fields
already are. See `AecoPostMortem.Findings/CLAUDE.md`'s "`Headline` is `required`..." note for where
the sentence itself is built (all eleven `Finding`-producing files) and the real-corpus verification.

Mockup parity item #7 added `RuleViolationCountEnvelope` and `RulesInventoryRowEnvelope.ViolationCount`
— see the non-obvious decision above for the closed two-shape union, the join by `RuleShapeMatch.
Statement`, and why the count is computed corpus-wide rather than scoped to the selected version's own
carrying sessions. This landed as scoped: all four built check orchestrators (`BannedToolFinding`,
`NeverReadPathFinding`, `UseAAfterBFinding`, `AlwaysPassParamFinding`) are wired, and the fifth shape
(`PreferAOverB`, which has no orchestrator anywhere in this codebase yet) renders the honest
`NoBuiltCheck` absence rather than a fabricated number — nothing was deliberately left half-wired to
fit an effort estimate. Verified against the live 35-session reference corpus: a real
`GET /api/rules-inventory` request renders the dominant repository's one Watched row
(`NeverReadPath`) with a real `Counted` violation count (103, at the corpus' current size).
`web/src/routes/RulesInventoryPage.tsx` (`web/CLAUDE.md`) renders it as a new "Violations" column,
landed in the same change as this wire field.

Mockup parity item #14 added `SessionMastheadEnvelope.StartedAt`/`.EndedAt` — the real wall-clock
start→end range, served alongside `ElapsedMs`. No endpoint change: `GetSession`'s existing `Session`
read already carried both fields (`SessionRecording.Build` already parsed `StartedAt` to compute
`Elapsed` itself), so this is purely two more envelope fields passed through unchanged. Verified
against the live 35-session reference corpus via a real `GET /api/sessions/{sessionId}` request: a
completed session serves a real `startedAt`/`endedAt` pair matching its own `elapsedMs`, and a
still-recording session serves a real `startedAt` with `endedAt` honestly `null`.
`web/src/routes/SessionPage.tsx`'s `Masthead` renders it as a new "Wall clock" field.
