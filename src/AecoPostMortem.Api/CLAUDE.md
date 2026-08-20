# AecoPostMortem.Api

Endpoints for the three surfaces, and the host that serves them.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General`, `FindingEnvelope.Adherence` and `FindingEnvelope.BaseRate` (FR-44, issue #41), and the `From`/`FromAdherence`/`FromBaseRate` factories that assemble them from a `Finding`. FR-48 (issue #52, S-42) added `ProvenanceLabel`, required on every shape; FR-41 (issue #44, S-36) added `SessionsAffected`, the served ranking key; FR-33 (issue #38, S-24) replaced the adherence shape's `Resolution`/`RuleVersion` pair with one `required AdherenceFigure Figure` |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |
| `SilentCheckEnvelope.cs` | FR-42's "checks that found nothing" surface — `SilentCheckEnvelope.From(CheckRegistry)` projects only the entries that ran clean |
| `DigestEnvelope.cs` | FR-41 part 1 (issue #44, S-36): `MastheadEnvelope` and `DigestEnvelope` — the served corpus masthead and the findings already ranked by sessions affected; FR-41 part 2 (issue #45, S-54): `RepositoryScopeEnvelope`, carried on `MastheadEnvelope`. FR-48 (issue #52, S-42) added `InferredFindings`, served separately from `RankedFindings` |
| `AppStateReport.cs` | S-48's zero-data diagnosis — `AppStateKind` (`NoSourceFound` / `EmptyStore` / `Ready`) and `AppStateReport.Diagnose`, the two-empty-states-are-different-fixes rule as one pure function over two booleans |
| `ApiHost.cs` | builds the ASP.NET Core host: `GET /api/app-state` (`AppStateRoute`), `GET /api/digest` (`DigestRoute`), `GET /api/rules-inventory?version=` (`RulesInventoryRoute`, `VersionParameter`), `GET /api/sessions/{sessionId}` (`SessionRouteTemplate`), `GET /api/sessions/{sessionId}/steps/{stepId}?kind=` (`StepEvidenceRouteTemplate`, S-52, issue #16), and, when a built web app is available, the static files that serve it from the same process; `DiagnoseAppState`, `GetDigest`, `GetRulesInventory`, `GetSession` and `GetStepEvidence` are the same five without a listener |
| `HookFailureEventLookup.cs` | FR-17's error text (issue #27): resolves failed `hook.start`/`hook.end` pairs straight from a session's own RAW events into `Findings.HookFailureEvent` — `Data.Execution.Hook` carries no error column, so `GetDigest` cannot read it any other way |
| `DeclaredIntentLookup.cs` | FR-19's not-yet-wired gap (issue #29), closed: resolves `report_intent` tool calls' own `arguments.intent` straight from RAW into `Rules.DeclaredIntent`, ordering by the call's own timestamp read as Unix milliseconds (`Data.Execution.ToolCall` carries no field for it, and `RawEvent.Sequence` only orders within one session) — the one place in the codebase allowed to name `report_intent` |
| `SessionRuleSetLookup.cs` | FR-27's own not-yet-wired gap, closed: `SessionRuleSetLookup.BuildAll` resolves a whole store's `RawEvent`s into one `Rules.SessionRuleSet` per `Data.Execution.Session` row, calling `Ingestion.SessionRuleExtractor.Extract` per session — the corpus-wide walk nothing did before this landed |
| `ToolInvocationShapeLookup.cs` | The real `Rules.ToolInvocationShape` corpus (piece 3), closed: `BuildAll` reads `HasPath`/`McpServerName` straight off `Data.Execution.ToolCall` (already real columns) and `SpawnsAgent` off `Data.Execution.Agent.SpawningToolCallId` (already structural) — no new RAW parsing for any of the three — and reads `HasPattern`/`HasReplacement`/`HasFileText`/`HasCommand` from each call's own RAW `tool.execution_start.data.arguments`, field names verified against the live 35-session reference corpus: `pattern` (`rg`/`grep`/`glob`), `old_str`/`new_str` (`edit`), `file_text` (`create`), `command` (`powershell`). `apply_patch`'s own `arguments` is a JSON string (the whole patch body), not an object — a real wrinkle the corpus check caught — so all four are `false` for a string-shaped call rather than guessed at |
| `RulesInventoryClassifier.cs` | FR-40's caller-supplied classify function (`Rules.RulesInventory.Build`'s own contract): `RulesInventoryClassifier.BuildClassifier` maps `Rules.RuleShapeCatalogue.MatchAll`'s output onto `RuleStatementStatus`, now also taking the real `ToolInvocationShapeLookup` corpus — a `PreferAOverB` match whose both operands resolve against it (`Rules.OperandResolver.ResolveTwoOperands`) is `Watched`; every other matched shape stays `CheckableNotYetBuilt`, and the caller-supplied `NotCheckable(reason)` stays unreachable; see this file's own remarks below for why `ToolIsBanned` is deliberately excluded from this piece |
| `RulesInventoryEnvelope.cs` | FR-40's served inventory (S-22, issue #35): `RuleStatementStatusEnvelope` (four closed shapes, `"watched"`/`"checkableNotYetBuilt"`/`"notCheckable"`/`"notARule"`), `RuleRetirementEnvelope` (`"inForce"`/`"retired"`), `RuleSetVersionEnvelope`, `RulesInventoryRowEnvelope`, `RulesInventoryStatusCountsEnvelope` and `RulesInventoryEnvelope.From` — one rule-set version's statements, never a union across versions |
| `SessionEnvelope.cs` | FR-21, part 1 of 3 (S-08, issue #15): `SessionTokenFiguresEnvelope`, `SessionMastheadEnvelope`, `SessionTapeStepEnvelope`, `SessionEnvelope` — the served masthead and tape, assembled from `Findings.SessionRecording`. FR-21 part 2 of 3 (S-52, issue #16) added `SessionFindingChipEnvelope` and `SessionEnvelope.Findings`, assembled from `Findings.SessionFindings`; FR-21 part 3 of 3 (S-53, issue #17) added `SessionRecordingStatusEnvelope` (`Complete`/`IngestIncomplete`/`ReconstructionFailed`) and the required `SessionEnvelope.Status` field; FR-22 (S-09, issue #18) added `SessionAgentLaneEnvelope` and the required `SessionEnvelope.Lanes` field (an optional `lanes` parameter on `From`, defaulting to an empty list — every existing call site still compiles) |
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

`ApiHost.GetStepEvidence` (S-52, issue #16) reads `Data.RawEvent` directly instead — the inspector's
Raw and Thinking tabs are provenance over the *event*, not the derived row, and neither `Turn` nor
`ToolCall` carries a foreign key back to the `RawEvent` that produced it
(`AecoPostMortem.Data/CLAUDE.md`). `StepEvidenceLookup` (this project, not `Ingestion`) reuses the
existing `Ingestion` reference (S-48, above) to call `Ingestion.EventEnvelopeReader.TryRead` — the
same envelope parsing `Ingestion.ExecutionRecordBuilder` already does to build the tape's own rows —
rather than duplicating it a second time.

`ApiHost.GetDigest` (S-36, issue #44) widens the `Data`/`Ingestion`/`Findings` references a third way:
it reads `Session`/`RawEvent`/`ToolCall`/`Turn`/`Permission` corpus-wide, calls six of the seven waste/
missing-capability check orchestrators (`Findings.RepeatedFileReadFindingCheck`,
`FailedToolCallsFinding`, `AbortedTurnFinding`, `HookFailureFinding`, `InterruptionLoadFinding`,
`PhaseChurnFinding`), and — for the two check inputs no derived table carries yet —
`HookFailureEventLookup`/`DeclaredIntentLookup` (this project, reusing `Ingestion.EventEnvelopeReader`
and `Ingestion.ToolArguments` the same way `StepEvidenceLookup` reuses the reader). `Rules` gains its
second real caller here too: `ToToolCallOutcomes` builds `Rules.ToolCallOutcome` from `ToolCall`
directly, the query S-14's own remarks named as later work. `ToolFailureClusterFinding` is not run —
it needs a mandating rule, which real rule extraction at scale (S-20) does not populate yet.

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

### `RepositoryScopeEnvelope` mirrors `RepositoryScope` exactly — a plain projection, not a filter

FR-41 part 2 (issue #45, S-54): `RepositoryScopeEnvelope.From` copies `SelectedRepository` and
`AvailableRepositories` straight across, the same shape `MastheadEnvelope.From` already uses for
every other masthead field. It does not re-derive or re-filter anything — `DigestEnvelope.From`'s
`RankedFindings` mapping is untouched by which repository is selected, because the caller of
`ProcessDigest.Build` (`AecoPostMortem.Findings/CLAUDE.md`) already scoped `findings` to one
repository before this envelope is ever assembled.

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

### `RulesInventoryClassifier` watches `PreferAOverB` for real; `Watched` and `NotCheckable(reason)` stay otherwise unreachable

With a real `ToolInvocationShape` corpus in hand, `RulesInventoryClassifier.BuildClassifier` now
takes it as a second parameter and actually attempts resolution for one shape:
`RuleShapeKind.PreferAOverB` — `Rules.OperandResolver.ResolveTwoOperands` against the corpus, and
`RuleStatementStatus.Watched` only when *both* operands resolve to at least one real tool
(`OperandResolutionLayer` other than `Unresolved`). Verified against the live 35-session reference
corpus: `"Prefer querying codebase-memory-mcp over Glob/Grep/Read for navigation"` is a real
`PreferAOverB` match whose operand A ("codebase-memory-mcp") genuinely resolves through the
`McpServerField` layer — confirmed via a real browser rendering an `mcpCall` tape step for that same
server — while operand B ("Glob/Grep/Read", after `RuleOperandText`'s own "for"-clause stripping)
stays `Unresolved`: no single real tool or `ToolRole` is named that. The statement therefore renders
`CheckableNotYetBuilt`, honestly — the mechanism is real and resolving, the live corpus simply has no
`PreferAOverB` rule phrased narrowly enough on both sides to watch yet (proven separately at the unit
level: `RulesInventoryClassifierTests` constructs a synthetic corpus where both operands do resolve).

`RuleShapeKind.ToolIsBanned` is deliberately excluded from this piece even though it also names one
operand: turning a ban into a real verdict needs deciding which `ToolRole` a banned tool "targets" for
`ToolVocabularyMismatchCheck`'s own `RuleToolMention` shape (`Rules/CLAUDE.md`'s `RuleToolMention`
remarks — "one `RuleToolMention` per named tool"), and nothing in this codebase has ever decided that
mapping. `NeverReadPath`/`UseAAfterB`/`AlwaysPassParam` have no built check at all yet either way.
`RulesInventoryClassifier.BuildClassifier` therefore still classifies every non-`PreferAOverB` match,
and every unmatched statement carrying a normative marker (`UnmatchedStatementDisposition.
CheckableNotBuilt`), as `RuleStatementStatus.CheckableNotYetBuilt`; an unmatched statement carrying
none (`UnmatchedStatementDisposition.NotCheckable`) still classifies as `RuleStatementStatus.NotARule`.
The caller-supplied `NotCheckable(reason)` variant is never constructed anywhere in this file — no
shape's absence is attributed to what the logs cannot record, only to what no check yet watches.
Wiring `ToolVocabularyMismatchCheck` into `GetDigest` as an adherence finding, and the `ToolIsBanned`
role-mapping question above, are both later work.

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

### `DigestState` and `RuleCoverageStatus` serialise as their names, not ordinals

Both enums are declared in `Findings` with no serialisation attributes of their own — domain types
stay serialisation-agnostic, the same separation `FindingEnvelope`/`SuggestionEnvelope` already draw.
`MastheadEnvelope.RuleCoverage` and `DigestEnvelope.State` each carry their own
`[JsonConverter(typeof(JsonStringEnumConverter))]` here instead, so a client reads `"NotYetAnalyzed"`
rather than an opaque integer for a state whose entire point (S-36's Gherkin) is to be stated in
words.

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
losslessly and needs no format agreement of its own.

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
`GET /api/digest` (`ApiHost.GetDigest`) assembles a live `ProcessDigest` from the store — six of the
seven waste/missing-capability check orchestrators, `MastheadCounters` computed corpus-wide at
request time (not maintained at ingest — see `GetDigest`'s own remarks on why that is still inside
budget at this corpus' scale), and a `RepositoryScope` defaulting to whichever repository carries the
most sessions. Verified end to end against the live 35-session reference corpus: 295 ranked findings
across all six checks, including the real `sessionStart` hook failure (25 of 25 sessions, error text
read straight from RAW) and a real two-repository corpus exercising the scope's own filtering — and a
real browser renders `web/`'s `DigestPage` against it with no frontend change, the exact promise
`web/CLAUDE.md` recorded ahead of this wiring. `ToolFailureClusterFinding` is not run here — it needs
a mandating rule, which real rule extraction at scale (S-20) does not populate yet, so it stays a
documented gap alongside the Rules Inventory and Monitor comparison endpoints below. The app-state
endpoint and host (`AppStateReport`, `ApiHost`) that S-48 adds were the first real endpoint this
project shipped; `GetDigest` is the second.

FR-48 (issue #52, S-42) added `FindingEnvelope.ProvenanceLabel` (required on every shape) and
`DigestEnvelope.InferredFindings` (served separately from `RankedFindings`, mirroring
`ProcessDigest`'s own split) — both now live through `GetDigest`, and `web/src/digest/ProvenanceBadge.tsx`
(S-54, issue #45) is a real consumer of the shape against real data.

FR-33 (issue #38, S-24) made the adherence shape carry one `required AdherenceFigure Figure`. None of
the six checks `GetDigest` runs today produces an adherence finding — every served finding maps
through `FindingEnvelope.From` (the `General` shape) — so this remains contract-only in practice even
though a live route now exists: an adherence check needs real rule extraction at scale (S-20), the
same gap `ToolFailureClusterFinding` is blocked on above. Because the figure's percentage is computed
from its operands and the envelope member is `required`, the endpoint that eventually produces one
(including S-35's Monitor comparison) inherits the guarantee without opting into it.
`web/src/digest/AdherenceFigureBlock.tsx` is the real rendering consumer, reached through `FindingRow`
once a check produces one.

`GET /api/rules-inventory` (`RulesInventoryEnvelope.cs`, `ApiHost.GetRulesInventory`, S-22, issue
#35, FR-40) is now served for real: `SessionRuleSetLookup.BuildAll` resolves the whole store's
`RawEvent`s into `SessionRuleSet`s (the corpus-wide extraction run `Ingestion/CLAUDE.md`'s own status
note names as still missing), `RulesInventoryClassifier` classifies every distinct statement once,
and the selected repository defaults the same way `GetDigest`'s `BuildRepositoryScope` already does
— this surface has no repository selector of its own (`web/CLAUDE.md`). Verified end to end against
the live 35-session reference corpus: this repository's own `CLAUDE.md`/`AGENTS.md` rules render as
17 statements (0 watched, 7 checkable — not yet built, 0 not checkable, 10 not a rule), and a real
browser renders `web/`'s `RulesInventoryPage` against it with no frontend change — the same promise
`web/CLAUDE.md` recorded ahead of this wiring.

`ToolInvocationShapeLookup.cs` (piece 3, first slice) closed the missing-corpus gap this section once
documented: `GetRulesInventory` now also builds a real `ToolInvocationShape` corpus corpus-wide and
hands it to `RulesInventoryClassifier`, which actually attempts resolution for `PreferAOverB` matches
— see the two non-obvious decisions above for the real field names verified, the real wrinkle
(`apply_patch`'s string-shaped arguments) the corpus check caught, and why the live corpus's own
`PreferAOverB` rule (`supahfly27/UpFront`'s "Prefer querying codebase-memory-mcp over Glob/Grep/Read
for navigation") resolves one operand for real but still renders `CheckableNotYetBuilt` overall — no
single real tool or role is named "Glob/Grep/Read". `ToolIsBanned`'s role-mapping question and wiring
`ToolVocabularyMismatchCheck` into a check orchestrator the way `GetDigest`'s other six checks are
remain later work.

`GET /api/sessions/{sessionId}` (`SessionEnvelope.cs`, `ApiHost.GetSession`, S-08, issue #15) is the
second real endpoint: FR-21's masthead and tape, read through `Data.Execution` and assembled by
`Findings.SessionRecording.Build`. `web/src/routes/SessionPage.tsx` is the client. Returns 404 for a
session id the store carries no `Session` row for; a session with rows but no steps still serves its
masthead with an empty `Steps` list. It now also serves `SessionEnvelope.Findings` — FR-21 part 2 of
3's chip row (S-52, issue #16), assembled from `Findings.SessionFindings.For` — though
`ApiHost.GetSession` still passes an empty `Finding` list to it today, the same "not yet wired to a
live corpus" gap `/api/digest` documents above: a chip row is real, but a real browser sees an empty
one until a later story runs every check orchestrator against the live store.

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

`MonitorComparisonEnvelope.cs` (S-35, issue #43, FR-39) is contract-only in the same sense the
digest and Rules Inventory contracts were before their own live endpoints landed:
`web/src/digest/MonitorComparisonBlock.tsx` and `web/src/api/monitor.ts` are real consumers of the
shape, but `ApiHost.Build` does not `MapGet` `/api/monitor-comparison` — picking the two adjacent
versions and the operand pair to compare, and running `Findings.MonitorComparison.Compare` against
the live store, is wiring no story has done yet, though `SessionRuleSetLookup` (added for
`/api/rules-inventory`, above) already supplies the corpus-wide `SessionRuleSet` resolution this
endpoint would also need. The web component is built ahead of that wiring, the same seam
`AdherenceFigureBlock.tsx` established before any digest endpoint served a real `AdherenceFigure`.
