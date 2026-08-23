# AecoPostMortem.Api

Endpoints for the three surfaces, and the host that serves them.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General`, `FindingEnvelope.Adherence` and `FindingEnvelope.BaseRate` (FR-44, issue #41), and the `From`/`FromAdherence`/`FromBaseRate` factories that assemble them from a `Finding`. FR-48 (issue #52, S-42) added `ProvenanceLabel`, required on every shape; FR-41 (issue #44, S-36) added `SessionsAffected`, the served ranking key; FR-33 (issue #38, S-24) replaced the adherence shape's `Resolution`/`RuleVersion` pair with one `required AdherenceFigure Figure`. Mockup parity item #5 added `Headline`, required on every shape — `Findings.Finding.Headline` passed straight through, unchanged, the same passthrough `Evidence`/`Recurrence` already are |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |
| `SilentCheckEnvelope.cs` | FR-42's "checks that found nothing" surface — `SilentCheckEnvelope.From(CheckRegistry)` projects only the entries that ran clean. Mockup parity item #6 added `Provenance`/`ProvenanceLabel`, projected straight from `CheckRegistryEntry.Provenance` (below) so a clean-check card can carry the same badge a finding does |
| `DigestEnvelope.cs` | FR-41 part 1 (issue #44, S-36): `MastheadEnvelope` and `DigestEnvelope` — the served corpus masthead and the findings already ranked by sessions affected; FR-41 part 2 (issue #45, S-54): `RepositoryScopeEnvelope`, carried on `MastheadEnvelope`. FR-48 (issue #52, S-42) added `InferredFindings`, served separately from `RankedFindings`. Mockup parity item #2 added `RepositoryScopeEnvelope.SessionIds`, the ordered session list a per-finding session strip needs. Mockup parity item #6 added `SilentChecks` (`SilentCheckEnvelope.From(digest.CheckRegistry)`), threading FR-42's surface through the same fetch. Mockup parity item #15 added `RuleCoverageStatusEnvelope` (`notYetAnalyzed`/`analyzed`, the closed wire shape for `Findings.RuleCoverageStatus`) and changed `MastheadEnvelope.RuleCoverage` from a bare enum to that type — `AnalyzedCoverage.Counts` reuses `RulesInventoryStatusCountsEnvelope` (`RulesInventoryEnvelope.cs`) verbatim rather than a second four-int shape. Digest session-naming Slice 2 added `RepositoryScopeEnvelope.SessionLabels` and a matching optional `sessionLabels` parameter threaded through `MastheadEnvelope.From`/`DigestEnvelope.From` |
| `AppStateReport.cs` | S-48's zero-data diagnosis — `AppStateKind` (`NoSourceFound` / `EmptyStore` / `Ready`) and `AppStateReport.Diagnose`, the two-empty-states-are-different-fixes rule as one pure function over two booleans |
| `ApiHost.cs` | builds the ASP.NET Core host: `GET /api/app-state` (`AppStateRoute`), `GET /api/digest?from=&to=` (`DigestRoute`, `FromParameter`/`ToParameter` — the pager & date-range filter task's optional `DateOnly` bounds, both omittable, a caller error on `from > to` answering 400), `GET /api/rules-inventory?version=` (`RulesInventoryRoute`, `VersionParameter`), `GET /api/sessions/{sessionId}` (`SessionRouteTemplate`), `GET /api/sessions/{sessionId}/steps/{stepId}?kind=` (`StepEvidenceRouteTemplate`, S-52, issue #16), `GET /api/settings` (`SettingsRoute`, the Settings surface's Part A), `POST /api/ingest` and `POST /api/rebuild` (`IngestRoute`/`RebuildRoute`, the Settings surface's Part B and this codebase's first two write endpoints), `POST /api/purge` (`PurgeRoute`, Part C — the only destructive endpoint, gated additionally by `ConfirmationHeader`/`PurgeConfirmation` via `IsConfirmed`), and, when a built web app is available, the static files that serve it from the same process; `DiagnoseAppState`, `GetDigest`, `GetRulesInventory`, `GetSession`, `GetStepEvidence`, `GetSettings`, `RunIngest`, `RunRebuild` and `RunPurge` are the same nine without a listener |
| `SettingsEnvelope.cs` | the Settings surface's read-only contract (Part A): store path/existence/size, whether that path is FR-11's documented default location (`StoreIsAtDefaultLocation` — see its own remarks for why this answers "is this where the store belongs?" rather than reporting *how* the path was chosen), the Copilot source root and whether it was found, and the configured exclusion list — every field a real, already-resolved fact, never guessed or zero-filled for a store that does not exist yet |
| `IngestResultEnvelope.cs` | `POST /api/ingest`'s response contract (Part B): FR-14's `Ingestion.CoverageReport` carried onto the wire verbatim (via `ExcludedSessionEnvelope`), plus a server-measured `DurationSeconds` — the same report the CLI's own `ingest` prints to stdout, never reduced to a bare "OK" |
| `PurgeResultEnvelope.cs` | `POST /api/purge`'s response contract (Part C): which files were actually deleted and how many bytes that reclaimed — `Data.LocalStore.PurgeOutcome` carried onto the wire verbatim. `DeletedAnything` is served explicitly rather than inferred from an empty `DeletedFiles`, so "there was no store to purge" stays a distinct, stated outcome instead of a success claiming a deletion that never happened. No `DurationSeconds`, unlike the other two write routes — deleting a file is not work an operator waits on |
| `RebuildResultEnvelope.cs` | `POST /api/rebuild`'s response contract (Part B): how many RAW events and sessions the derived layer was just re-derived from, plus a server-measured `DurationSeconds` |
| `HookFailureEventLookup.cs` | FR-17's error text (issue #27): resolves failed `hook.start`/`hook.end` pairs straight from a session's own RAW events into `Findings.HookFailureEvent` — `Data.Execution.Hook` carries no error column, so `GetDigest` cannot read it any other way |
| `HookTriggerNameLookup.cs` | What triggered a hook, sibling task: `FindForHookSteps` resolves a `Hook` step's own trigger tool name eagerly (batched, keyed by `StepId`), the same "additive, no-fetch" shape `PromptTextLookup` established for a Prompt step's own text — deliberately a separate, narrower reader from `StepEvidenceLookup`'s own `FindTrigger` (below), which resolves the fuller, on-demand trigger evidence once a step is selected. See the non-obvious decision below for why these stay two readers rather than one |
| `DeclaredIntentLookup.cs` | FR-19's not-yet-wired gap (issue #29), closed: resolves `report_intent` tool calls' own `arguments.intent` straight from RAW into `Rules.DeclaredIntent`, ordering by the call's own timestamp read as Unix milliseconds (`Data.Execution.ToolCall` carries no field for it, and `RawEvent.Sequence` only orders within one session) — the one place in the codebase allowed to name `report_intent` |
| `SessionRuleSetLookup.cs` | FR-27's own not-yet-wired gap, closed: `SessionRuleSetLookup.BuildAll` resolves a whole store's `RawEvent`s into one `Rules.SessionRuleSet` per `Data.Execution.Session` row, calling `Ingestion.SessionRuleExtractor.Extract` per session — the corpus-wide walk nothing did before this landed |
| `ToolInvocationShapeLookup.cs` | The real `Rules.ToolInvocationShape` corpus (piece 3), closed: `BuildAll` reads `HasPath`/`McpServerName` straight off `Data.Execution.ToolCall` (already real columns) and `SpawnsAgent` off `Data.Execution.Agent.SpawningToolCallId` (already structural) — no new RAW parsing for any of the three — and reads `HasPattern`/`HasReplacement`/`HasFileText`/`HasCommand` from each call's own RAW `tool.execution_start.data.arguments`, field names verified against the live 35-session reference corpus: `pattern` (`rg`/`grep`/`glob`), `old_str`/`new_str` (`edit`), `file_text` (`create`), `command` (`powershell`). `apply_patch`'s own `arguments` is a JSON string (the whole patch body), not an object — a real wrinkle the corpus check caught — so all four are `false` for a string-shaped call rather than guessed at. The public `BuildAll(rawEvents)` overload parses RAW itself; an `internal BuildAll(argumentsByCall)` overload (piece 3's fifth slice, code review) takes an already-built dictionary instead, so `GetDigest` can share one parse pass with `ParamCarryingCallLookup` rather than each lookup parsing the same payloads separately |
| `RawToolArguments.cs` | Piece 3's fifth slice: `ByCall` — the RAW-parsing pass factored out of `ToolInvocationShapeLookup` so `ParamCarryingCallLookup` (below) can reuse the identical `tool.execution_start` → `ToolArguments` read rather than walking `rawEvents` a second time for the same question |
| `ParamCarryingCallLookup.cs` | Piece 3's fifth and final slice: the real `Rules.ParamCarryingCall` corpus `Rules.AlwaysPassParamCheck` resolves its mentions against. `SpawnsAgent` reuses `Agent.SpawningToolCallId` the same structural way `ToolInvocationShapeLookup` does; `ArgumentKeys` reads every field name a call's own RAW arguments carried (`Ingestion.ToolArguments.PropertyNames`, new this slice) rather than one fixed set, since the parameter a rule names is arbitrary — unlike `ToolInvocationShapeLookup`'s four closed booleans. `ArgumentsRecorded` (code review) is `true` only when a call's own arguments were object-shaped, so "no record at all" never collapses into "recorded with no keys". The public `BuildAll(rawEvents)` and an `internal BuildAll(argumentsByCall)` overload mirror `ToolInvocationShapeLookup`'s own split (below) |
| `RulesInventoryClassifier.cs` | FR-40's caller-supplied classify function (`Rules.RulesInventory.Build`'s own contract): `RulesInventoryClassifier.BuildClassifier` maps `Rules.RuleShapeCatalogue.MatchAll`'s output onto `RuleStatementStatus`, taking the real `ToolInvocationShapeLookup` corpus — a `PreferAOverB` or `UseAAfterB` match whose both operands resolve against it (`Rules.OperandResolver.ResolveTwoOperands`) is `Watched` (piece 3's fourth slice added `UseAAfterB` to this branch); a `ToolIsBanned` match whose single operand resolves (`Rules.OperandResolver.Resolve`, no `ToolRole` involved) is also `Watched`; a `NeverReadPath` or `AlwaysPassParam` match is `Watched` unconditionally, no resolution involved (piece 3's third and fifth slices — neither operand is a tool name); every other matched shape stays `CheckableNotYetBuilt`. Mockup parity item #18 gave the caller-supplied `NotCheckable(reason)` its first real constructor: an unmatched, directive statement (`UnmatchedStatementDisposition.CheckableNotBuilt`) gated on whether an action was *needed*/*necessary*/*relevant* to "the task" (`TaskRelevanceObligation`) is `NotCheckable`, everything else in that disposition stays `CheckableNotYetBuilt` |
| `RulesInventoryEnvelope.cs` | FR-40's served inventory (S-22, issue #35): `RuleStatementStatusEnvelope` (four closed shapes, `"watched"`/`"checkableNotYetBuilt"`/`"notCheckable"`/`"notARule"`), `RuleRetirementEnvelope` (`"inForce"`/`"retired"`), `RuleSetVersionEnvelope`, `RulesInventoryRowEnvelope`, `RulesInventoryStatusCountsEnvelope` and `RulesInventoryEnvelope.From` — one rule-set version's statements, never a union across versions. Mockup parity item #7 added `RuleViolationCountEnvelope` (`"counted"`/`"notAvailable"`) and `RulesInventoryRowEnvelope.ViolationCount` — a Watched row's own violation count, `null` for every other status. The Monitor comparison's missing-door task (code review round 2) added `RuleSetVersionEnvelope.FirstSessionStartedAt` — `Rules.RuleSetVersion` already carried it (the PR #108/#112 chronology fix), it simply never travelled onto the wire; `web/src/api/useMonitorComparison.ts`'s client-side adjacency check needed it to be a real port of `Rules.RuleSetVersionAdjacency.RequireAdjacentPair`'s own sort rather than a trust in `availableVersions`' array order |
| `SessionEnvelope.cs` | FR-21, part 1 of 3 (S-08, issue #15): `SessionTokenFiguresEnvelope`, `SessionMastheadEnvelope`, `SessionTapeStepEnvelope`, `SessionEnvelope` — the served masthead and tape, assembled from `Findings.SessionRecording`. FR-21 part 2 of 3 (S-52, issue #16) added `SessionFindingChipEnvelope` and `SessionEnvelope.Findings`, assembled from `Findings.SessionFindings`; FR-21 part 3 of 3 (S-53, issue #17) added `SessionRecordingStatusEnvelope` (`Complete`/`IngestIncomplete`/`ReconstructionFailed`) and the required `SessionEnvelope.Status` field; FR-22 (S-09, issue #18) added `SessionAgentLaneEnvelope` and the required `SessionEnvelope.Lanes` field (an optional `lanes` parameter on `From`, defaulting to an empty list — every existing call site still compiles). Mockup parity item #14 added `SessionMastheadEnvelope.StartedAt` (`required DateTimeOffset`) and `.EndedAt` (`DateTimeOffset?`), passed through unchanged from `Findings.SessionMasthead`. Mockup parity item #17 added `SessionTapeStepEnvelope.Findings` (`required IReadOnlyList<FindingEnvelope>`, defaulting to `[]` via a new optional `findings` parameter on `From`) and a matching optional `stepFindings` parameter on `SessionEnvelope.From` — see `SessionTapeStepFindingLookup.cs` below for what populates it. What triggered a hook (sibling task) added `SessionTapeStepEnvelope.TriggeredBy` (a `Hook` step's own trigger tool name, `string?`, `null` for every other kind) and a matching optional `triggeredByToolNameByStepId` parameter on `SessionEnvelope.From`, the identical additive shape `promptTextByStepId` already established — see `HookTriggerNameLookup.cs` above for what populates it |
| `SessionTapeStepFindingLookup.cs` | Mockup parity item #17: attaches a finding to the specific tape step(s) it is unambiguously about, for the narrow set of finding shapes whose own `Finding.Evidence` names an identity (a tool name, a hook name) a session's own `ToolCall`/`Hook` rows can be matched against exactly — `Build(sessionFindings, toolCalls, hooks)` returns a `(SessionTapeStepKind, StepId)`-keyed map. Covers exactly two shapes today, matched by the marker `EvidenceItem.Field` name(s) each orchestrator already writes (the same technique `RulesInventoryEnvelope.cs`'s own `BuildViolationCounts` already uses to join a served count back to its source check, applied here to a new question): a `toolIdentity` field (`FailedToolCallsFinding`/`ToolFailureClusterFinding`) matches every failed `ToolCall` of that exact tool identity in the session — every failing call, not a guessed "first" or "most recent" one, since the finding's own evidence is an aggregate rate over all of them; a `data.success`/`data.error` field pair (`HookFailureFinding`) matches every failed `Hook` row whose `Name` equals the finding's own `Recurrence.Key`. Every other finding-producing check (`RepeatedFileReadFindingCheck`, `AbortedTurnFinding`, `InterruptionLoadFinding`, `PhaseChurnFinding`, `BannedToolFinding`, `NeverReadPathFinding`, `UseAAfterBFinding`, `AlwaysPassParamFinding`) is deliberately left uncovered — see the non-obvious decision below for why each one doesn't fit |
| `StepEvidenceEnvelope.cs` | FR-21 part 2 of 3 (S-52, issue #16): `ThinkingEnvelope` (`Present`/`Unavailable`), `RawStepEventEnvelope` (`Present`/`Skipped`), `StepEvidenceEnvelope` — the inspector's Thinking and Raw tab contracts. No Detail contract exists here: every field the Detail tab needs already travels on `SessionTapeStepEnvelope`. FR-23 (S-10, issue #19) added `ModelReasoningReadability` and `ThinkingEnvelope.Unavailable.ReadabilityByModel` — the session's own measured readable-reasoning share, one entry per model, populated only for the provider-encryption reason. A tool call's own result added `StepEvidenceEnvelope.Result`, reusing `RawStepEventEnvelope` for a second event (`tool.execution_complete`) rather than a new type — see the non-obvious decision below. What triggered a hook (sibling task) added `HookTriggerEnvelope` (`ToolInvocation`/`Absent`) and `HookTriggerArguments`, plus the required `StepEvidenceEnvelope.Trigger` field — see the non-obvious decision below for the full shape and the real corpus verification |
| `StepEvidenceLookup.cs` | FR-21 part 2 of 3 (S-52, issue #16): `StepEvidenceLookup.Find` — resolves a step's raw event and (for a prompt step) its readable reasoning straight from a session's own `RawEvent`s, reading envelopes the same way `AecoPostMortem.Ingestion.ExecutionRecordBuilder` does. FR-23 (S-10, issue #19) added `StepEvidenceLookup.ReasoningReadabilityByModel`, scanning the whole session's own main-thread `assistant.message` events (not just the current turn) to build the per-model readable share. A tool call's own result added `StepEvidenceLookup.FindResult`, joining `tool.execution_complete` by the same `toolCallId` field the call itself is already joined by. What triggered a hook (sibling task) added `StepEvidenceLookup.FindTrigger`, reading `hook.start.data.input` off the identical anchor envelope `Find`'s own `Raw` field already resolved for a `Hook` step — never a second raw-event scan |
| `PromptTextLookup.cs` | A real gap in a stale doc comment, closed: `Findings.SessionTapeStep.Label` for a `Prompt` step is the turn's own `Outcome`, because `Findings.SessionRecording.cs`'s own comment claimed Copilot's event log "carries no separate prompt entity" — verified wrong against the live corpus, `user.message.data.content` is the literal prompt text, joined by `interactionId` to the same `assistant.turn_start` event a `Prompt` step's `StepId` (`Turn.EventId`, that event's own envelope id) already resolves from. `FindForPromptSteps` mirrors `StepEvidenceLookup.FindThinkingForPromptSteps`'s exact batch shape, including its keying: both are keyed by the `turn_start` envelope id, never by `data.turnId` — see the non-obvious decision below for the collision that once made this a real, measured defect |
| `SessionLabelLookup.cs` | Digest session-naming, Slice 2: a session's own display label — the first five words of its earliest real prompt, so a Digest session link reads as something other than a bare GUID. Simpler than `PromptTextLookup` — a session's first prompt needs no `turn_start`/`interactionId` join at all, only the earliest `user.message` event by `RawEvent.Sequence`. Truncation happens here, server-side, not in the browser (`Truncate`) |
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

Digest session-naming Slice 2 added `SessionLabels` the same additive way: a fourth field, keyed by
session id, populated by `GetDigest` over the identical `scopedRawEvents` grouped by session
`HookFailureEventLookup`/`DeclaredIntentLookup` already group there — no new store read. Unlike
`SelectedRepository`/`AvailableRepositories`/`SessionIds`, this one field is not a straight
`Findings.RepositoryScope` passthrough: `Findings` has no RAW access, so `SessionLabelLookup`'s
result is resolved here, at the `Api` layer, and threaded through `RepositoryScopeEnvelope.From`'s
own new optional parameter (defaulting to an empty dictionary) — the identical additive-parameter
shape `SessionTapeStepEnvelope.PromptText`/`Thinking` already established for the tape.
`RecurrenceStrip.tsx` (`web/CLAUDE.md`) renders a session's own resolved label as the link text, with
the raw session id kept as the link's `title` (tooltip) so it stays reachable.

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
need the since-deleted `ToolVocabularyMismatchCheck`'s own `RuleToolMention` shape (`Rules/CLAUDE.md`'s
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
step's source: a turn's, skill's or hook's own envelope `id`, or a tool call's
`tool.execution_start.data.toolCallId`. This is a lookup by an identity that already exists, not a
second scheme invented for this story. (A turn was originally matched on `data.turnId` instead — a
display counter, not an identity; see "A `Prompt` step is matched on its `turn_start` envelope id"
below for the measured collision that removed.)

### A `Prompt` step is matched on its `turn_start` envelope id, not on `data.turnId`

Both prompt-step lookups in this project (`StepEvidenceLookup.Find`/`.FindThinkingForPromptSteps`,
`PromptTextLookup.FindForPromptSteps`) once matched a step against
`assistant.turn_start.data.turnId`, because that is what `Findings.SessionTapeStep.StepId` carried.
`data.turnId` is Copilot's own cycling display counter, not an identity — the very field
`Data.Execution.Turn` had already been re-keyed away from (`AecoPostMortem.Data/CLAUDE.md`) — so
both lookups resolved every colliding step to whichever turn carried that counter *first*, and every
tape row under that `StepId` showed that first turn's prompt text and that first turn's reasoning
window.

`Findings.SessionRecording` now builds a `Prompt` step's `StepId` from `Turn.EventId`
(`Findings/CLAUDE.md`'s own entry carries the before/after table), so all three lookups here match
on the `turn_start` envelope's own `id` instead: the two `StepEvidenceLookup` sites through
`FindByEnvelopeId` (the identical helper a `Skill`/`Hook` step already used), and `PromptTextLookup`
by keying its own `interactionId` dictionary on that same field — one identity, not a fourth scheme.
`FindByDataField` survives for its one remaining honest use: a tool call's `data.toolCallId`, which
*is* a natural id Copilot writes for the thing itself.

All three sites also refuse an *empty* envelope id (code review). `Ingestion.EventEnvelopeReader.
TryRead` rejects a missing or non-string `id` but accepts `"id":""`, so an unguarded `==` would
collapse every such event onto one empty step id and resolve them all to whichever came first —
literally the same identity failure this entry is about, one level down. Guarding is cheaper than
reasoning about whether it can occur, and `StepEvidenceLookupTests.
An_empty_envelope_id_matches_nothing_rather_than_colliding_on_the_first_such_event` pins it.

An empty `StepId` was also *more* reachable before this change than after it, which is worth stating
because it is the opposite of the usual "new key, new edge case" worry: `Ingestion.
ExecutionRecordBuilder` writes `Turn.TurnId` as `GetString(..., "turnId") ?? string.Empty` (a real,
tested case — a `turn_start` carrying no `turnId` must still close and persist), so a `Prompt` step's
`StepId` was genuinely empty for such a turn and the step could not be addressed by the route at all.
`Turn.EventId` has no such fallback — a `turn_start` whose envelope will not read never opens a turn
in the first place — so those steps are now ordinarily addressable.

The blast radius turned out to be far smaller than this file's own earlier note (written when the
fix was deferred) predicted, and it is worth recording why: **every consumer already treated
`StepId` as opaque**. The wire route (`GET /api/sessions/{sessionId}/steps/{stepId}?kind=`) passes
it through verbatim; `SessionTapeStepEnvelope.StepId` is a passthrough field; and `web/`'s DOM ids
(`tape-step-${stepId}`), React keys and `useStepEvidence` cache keys are all built from whatever the
server sends. So the frontend needed **no change at all** — 162 web tests passed untouched — and the
change is four production edits — the three lookups here, plus `SessionRecording.Build`'s own field
choice over in `Findings` — and their tests. The earlier note's estimate of "a materially larger
blast radius" was the pessimism this repository has now mis-estimated in the same direction several
times (see the `UseAAfterB` "no existing check shape carries timing" note above, and piece 2's "five
unconfirmed fields"): the honest lesson is to *measure* a blast radius by grepping the consumers
before calling a fix too large, not to infer it from the number of layers a field passes through.

Two second-order effects worth knowing:

- **`Thinking` was fixed by the same change, not separately.** It carried the identical exposure
  since S-52 and needed no fix of its own once `StepId` was collision-free — measured, this is what
  took PR #130's inline readable-reasoning prose from resolving on **0** of 1,878 real prompt steps
  to **35**.
- **`PromptText` coverage legitimately *fell*, from 1,878 of 1,878 to 1,381.** That is the collision
  being removed, not a regression: under the old key every prompt step resolved *some* text (at most
  586 of them could have been the right one, since that was the distinct-`StepId` count). Verified
  straight from RAW with a throwaway SQLite probe, not inferred: exactly **497**
  `assistant.turn_start` events corpus-wide carry an `interactionId` that no `user.message` with
  `data.content` covers (137 distinct interaction ids; 0 turn_starts carry no `interactionId` at
  all) — Copilot opens several turns under one interaction, and some interactions have no recorded
  user prompt. Those 497 steps now render the outcome label instead of another turn's prompt, which
  is `PromptTextLookup`'s own documented "absence in, absence out" discipline doing its job.

### A step's Raw tab answers 200 with `Skipped`, never a 404 — the edge case's own words

`StepEvidenceLookup.Find` cannot fail to find *a* result — when no raw event matches, it returns
`RawStepEventEnvelope.Skipped` and `ThinkingEnvelope.Unavailable`, both with a stated reason, rather
than `null`. `ApiHost.GetStepEvidence` returns `null` (→ 404) only when the *session* does not
exist; a step id that matches nothing within a real session still gets 200 with the skipped state.
This mirrors `SilentCheckEnvelope`'s own discipline of never synthesising an entry, in the opposite
direction: here the type always produces *something*, because "shows that fact rather than an empty
panel" (the story's own edge case) requires an explicit value for a client to render, not an HTTP
status a client would have to interpret.

### `StepEvidenceEnvelope.Result` reuses `RawStepEventEnvelope` — a tool call's own result is the same "literal payload, or a stated absence" question asked of a second event

The Raw tab showed a tool call going out (`tool.execution_start`) but never what came back — a real
gap, not a guess: confirmed against the live 35-session reference corpus (`~/.copilot/session-state/`,
16,076 `tool.execution_complete` events across 35 sessions) before writing any code, following this
project's own cautionary precedent for guessing a field name (`Ingestion/CLAUDE.md`'s
`EventEnvelopeParserV1` incident). `tool.execution_complete` carries `data.toolCallId` — the identical
field `tool.execution_start` carries, joined by `StepEvidenceLookup.FindByDataField` the same way the
call itself already is — for *every* tool call, MCP or not (a real MCP sample,
`codebase-memory-mcp-list_projects`, confirmed the same shape). On success (15,703 of 16,076 measured
events) `data.result` is always an object, `{content: string, detailedContent: string}` — never the
bare-string shape this project's own `ToolArguments.cs` precedent exists for (that precedent is
`tool.execution_start.data.arguments`, a distinct field, confirmed distinct rather than assumed); the
real `apply_patch` string-shaped case in this corpus is that field, not the result. On failure (373 of
16,076) there is no `result` key at all, only `data.error: {message, code}` — still a real, present
`tool.execution_complete` event, so `StepEvidenceLookup.FindResult` serves it as `Present` with the
error payload, never `Skipped` — the union has no third `Absent` shape, only `Present`/`Skipped`, the
same two `RawStepEventEnvelope` always had. Max measured payload size was 42,976 characters (~43 KB) —
nowhere near "very large" — so `Result.Payload` follows the exact precedent `Raw.Payload` already set:
served whole, verbatim, never truncated (`RawStepEventEnvelope.Present.Payload`'s own doc comment,
"byte-exact ... a pass-through, never a re-serialisation"). Code review flagged that an unbounded
~43 KB block could still push the tape off-screen in the browser even though the server should never
truncate it — `web/CLAUDE.md`'s matching entry covers the display-side `max-height`/`overflow-y`
bound this added, a client-side concern with no server-side counterpart.

`Result` reuses `RawStepEventEnvelope` (`Present`/`Skipped`) rather than a new type: this is not a
re-parsed `content`/`detailedContent` shape, it is the identical "the literal event payload, or a
stated absence" question `Raw` already answers, asked of a second event — inventing a
`ToolResultEnvelope` would have duplicated that exact two-state union for no behavioural difference.
`RawStepEventEnvelope.Skipped.Reason` distinguishes three real, different causes, each a named
`const string` on `StepEvidenceLookup` rather than an inline literal, so a caller (and a test) can
tell them apart on their own terms, not merely on all being non-empty strings (code review): a step
kind that never produces a tool result at all (`ResultNotApplicableReason`, for `Prompt`/`Skill`/
`Hook`); a `ToolCall`/`McpCall` step whose own `tool.execution_complete` was never recorded — still
running when the log was captured, or the session ended mid-call (`NoRecordedCompletionReason`) — a
distinct, stated state per the task's own requirement, never an empty string read as "the result was
empty"; and the pre-existing `NoRawEventFoundReason` a step with no matching raw event at all already
used for `Thinking`/`Raw`, now shared by `Result` too rather than a fourth copy of the same string
literal. `StepEvidenceLookupTests` pins all these states with substance assertions
(`Assert.Contains`/`Assert.DoesNotContain` on each reason's own distinguishing text, not bare
`NotEmpty`), plus a real result, a failed call's error payload (still `Present`), an MCP call
resolving the same way a plain tool call does, two completions sharing one `toolCallId` resolving to
the *last* one (matching `Ingestion.ExecutionRecordBuilder.BuildToolCalls`'s own overwrite-on-
duplicate `completions` dictionary semantics — `FindByDataField` was changed from first-match to
last-match for this reason, code review), and a missing `tool.execution_start` that does not suppress
a real, present `tool.execution_complete` (the early-return branch for "no anchor found" now calls
`FindResult` instead of hardcoding a third `Skipped` copy, code review).

Verified against the live 35-session reference corpus via a real `GET /api/sessions/{id}/steps/{stepId}
?kind=toolCall` request and a real browser: of this project's own real tool calls, every one with a
recorded `tool.execution_complete` now serves its real result payload on the Raw tab's new "Result"
block, and a call the session ended mid-execution correctly serves the stated absence rather than a
blank panel.

### What triggered a hook: an eager tiny fact on the tape plus a fuller, on-demand evidence field — never a tape-row label change

A hook row said a hook ran, but not what it ran in response to. Real `hook.start` payloads confirmed
against the live 35-session reference corpus before writing any code (this project's own cautionary
`EventEnvelopeParserV1` incident, `Ingestion/CLAUDE.md`): `data.input` carries `toolName`/`toolArgs`/
`toolResult` for a `postToolUse` hook (2,992 of 3,027 real `hook.start` events), and `initialPrompt`/
`source`/`cwd` — never `toolName` — for a `sessionStart` hook (35 of 3,027, none with a `toolName`).
No third `hookType` exists in this corpus.

**Where it surfaces, and why not the tape row itself.** Three surfaces were weighed: the tape row's
own label, the Detail tab, and the Raw/evidence tab. The tape row was rejected — `session/Tape.tsx`'s
virtualisation math is deliberately fixed-height and simple (`web/CLAUDE.md`'s own "driven by fixed
constants, never by measuring the real DOM" entry), and this task's own brief says not to destabilise
it; a trigger's own tool name is also not the kind of fact that reads well packed into one row's
existing `label` text without a second visual element the row doesn't have room for. Two surfaces
were built instead, splitting the eager/small half from the richer/on-demand half the same way
`PromptText` (eager, tiny) and `Thinking` (fuller, fetched) already split for a Prompt step:

- **Detail tab, eager, no fetch** — `SessionTapeStepEnvelope.TriggeredBy` (`string?`), a `Hook`
  step's own trigger tool name, resolved once per session (`HookTriggerNameLookup.FindForHookSteps`)
  the identical "additive, eager, no-fetch" shape `PromptTextLookup` established. Small and
  scannable, costs nothing extra to load since the tape is already in hand the moment a step is
  selected (`web/CLAUDE.md`'s "the inspector fetches only Thinking/Raw; Detail needs no request of
  its own" entry stays true).
- **Raw tab, on demand, fetched** — `StepEvidenceEnvelope.Trigger` (`HookTriggerEnvelope`), the full
  tool name, arguments and result, resolved by `StepEvidenceLookup.FindTrigger` from the identical
  `hook.start` anchor envelope `Find` already resolved for that step's own `Raw` field — never a
  second raw-event scan. This is where the potentially-large payload lives, bounded the same way
  `Result` already is (below).

**"No trigger" is a distinct, stated value at both layers, never an empty string.** `TriggeredBy` is
`null` for every non-`Hook` step, for a `sessionStart` hook (structurally no tool trigger), and for a
hook whose own `hook.start` cannot be found — but the *reason* lives only on the richer `Trigger`
field, a closed two-shape union behind a private constructor (the same `RawStepEventEnvelope`/
`ThinkingEnvelope`/`SuggestionEnvelope` mechanism): `ToolInvocation` for a real `postToolUse` trigger,
`Absent { Reason }` for every other case, naming which — "Only a hook step has a trigger; this step
kind does not," "The sessionStart hook carries no tool trigger; it did not fire in response to a tool
call," or the pre-existing `NoRawEventFoundReason` `Result`/`Raw` already use. `web/src/routes/
SessionPage.tsx`'s Detail-tab fallback text ("No tool trigger resolved — see Raw tab for detail.")
points the operator at the fuller, reasoned answer rather than trying to compress three distinct
causes into one eager field's own short text.

**`toolArgs` is parsed the identical polymorphic way `Ingestion.ToolArguments` parses
`tool.execution_start.data.arguments` (FR-4), reused rather than re-implemented.** `HookTriggerArguments.Kind`
is `Ingestion.ToolArgumentKind` itself, not a second, parallel enum — the same "camelCase enum on the
wire, no per-property override needed" convention `Data.Execution.OwnerKind` already gets from the
global `JsonStringEnumConverter`. Verified against the live reference corpus rather than assumed: 840
of 2,992 real `postToolUse` `hook.start` events carry a string-shaped `toolArgs` (`apply_patch`'s own
whole patch body), never an object — `FindTrigger` calls `ToolArguments.Parse` directly (passing
`"null"` through the identical code path for a genuinely missing `toolArgs` key, rather than a special
case of its own), so this shape is recorded, never coerced, the same discipline `ToolInvocationShapeLookup`
already established for the sibling `tool.execution_start.data.arguments` field.

**`toolResult` is served whole, never truncated — measured, not guessed, from a number an order of
magnitude larger than the precedent this task was told to measure against.** `Present.Payload`
(a tool call's own result) measured a 42,976-character max; this field's own max, measured across
the same corpus, is 199,831 characters (~200 KB, a `github-mcp-server-get_file_contents` call) —
p50 1,311 characters, p99 39,602, only 22 of 2,992 real triggers exceed the 43 KB bound the prior
precedent already served whole. Given the precedent already accepted serving payloads an order of
magnitude smaller whole, and only a measured 22 real triggers exceed even that older bound, this field
follows the identical choice: served whole, verbatim, bounded only on the client
(`web/CLAUDE.md`'s matching `.inspector__raw-payload` `max-height`/`overflow-y` entry — reused
directly, no new CSS, since `TriggerBlock` renders through the identical class). Modelled as nullable
(`string?`) rather than assumed present, even though a measured 100% (2,992 of 2,992) of real
`postToolUse` triggers carry one — a trigger genuinely carrying none states that fact as
`null`/"No result was recorded for this hook's trigger," never an empty string.

**`HookTriggerNameLookup` and `StepEvidenceLookup.FindTrigger` stay two separate readers, not one
shared pass.** This mirrors `PromptTextLookup`/`StepEvidenceLookup.FindThinkingForPromptSteps`'s own
established split (`Api/CLAUDE.md`'s "References" section) rather than the `RawToolArguments.ByCall`
shared-dictionary precedent piece 3's fifth slice built: those two questions are asked at genuinely
different times over different-shaped answers — eagerly, for the whole tape, a short tool name; once,
for a selected step, the full arguments and result — so there is no single caller that would
otherwise parse the same payload twice the way `ToolInvocationShapeLookup`/`ParamCarryingCallLookup`
did. `HookTriggerNameLookup.FindForHookSteps` reads only `hook.start`'s own `data.input.toolName`;
`FindTrigger` reads `data.input` in full, from the identical anchor `Find` already resolved — so even
`FindTrigger` itself does not re-scan `sessionEvents`.

Verified against the live 35-session reference corpus via a real `GET /api/sessions/{sessionId}`
request and a real `GET /api/sessions/{sessionId}/steps/{stepId}?kind=hook` request, and a real
browser: see this project's own `Status` section for the exact session, step and counts inspected.

### A real, pre-existing defect this task's own real-data check found: a Hook step's Raw tab never resolved anything, because `StepEvidenceLookup.Find` matched the wrong field

Building this feature's real-corpus verification surfaced a genuine bug that predates this task,
unrelated to the trigger work itself but blocking every part of it from working against real data:
the very first real `GET /api/sessions/{sessionId}/steps/{stepId}?kind=hook` request this task made
(before this fix) answered `Raw: Skipped { NoRawEventFoundReason }` for a step id the tape itself had
just served — the Raw tab for *any* Hook step had never actually resolved on real data, only on this
project's own hand-written unit fixtures.

The cause: `Findings.SessionTapeStep.StepId` for a `Hook` step is `Data.Execution.Hook.EventId`, and
`Ingestion.HookBuilder.Build` deliberately sets `EventId = invocationId` — `data.hookInvocationId`,
the pair's own natural correlation key (`HookBuilder`'s own doc comment: "unlike Skill, neither
event's own envelope id ties the two together"), verified by `HookBuilderTests` asserting exactly
that. `StepEvidenceLookup.Find`'s Hook branch, however, called `FindByEnvelopeId(ordered, "hook.start",
stepId)` — matching the requested id against the **envelope's own `id`**, not `hookInvocationId`. A
real `hook.start` payload's envelope id and its own `hookInvocationId` are two different values
(confirmed against the live corpus, e.g. `id: "9bf37c76-…"` vs. `data.hookInvocationId:
"10a44d08-…"` on the very first `sessionStart` hook this task inspected), so the two never matched:
requesting a real Hook step's evidence by its real, served `StepId` resolved nothing, always. The
existing regression test for this (`A_hook_steps_raw_event_is_matched_by_the_envelopes_own_id`) never
caught it because its own fixture set the envelope id and the requested step id to the same literal
string with no `hookInvocationId` field at all — a synthetic shape no real `hook.start` payload has.

Confirmed with a direct measurement before writing the fix: a live query against the store scoped to
`SessionTapeStepKind.Hook` resolved **0 of 97** real hook steps in the session this task's own
end-to-end check used. The fix is narrow and structural, not a new mechanism: `StepEvidenceLookup.Find`'s
Hook branch now calls `FindByDataField(ordered, "hook.start", "hookInvocationId", stepId)` — the same
helper `ToolCall`/`McpCall` already use for their own `toolCallId` field, reused rather than a new
lookup shape — and `HookTriggerNameLookup.FindForHookSteps` (this task's own new file) keys its
dictionary by the identical `hookInvocationId` field rather than the envelope id it was first written
against. `StepEvidenceLookupTests`/`HookTriggerNameLookupTests` were both rewritten to key their own
fixtures by `hookInvocationId`, with two dedicated regression cases proving the fix holds in both
directions: a real hookInvocationId resolves, and the envelope id alone — the wrong field — resolves
nothing (`A_hook_step_requested_by_its_envelope_id_instead_of_its_hookInvocationId_resolves_nothing`).

This is the same lesson `Ingestion/CLAUDE.md`'s own `EventEnvelopeParserV1` cautionary tale states —
hand-picked fixtures had all independently guessed a field that does not describe the real payload,
and every one of them agreed with itself, so nothing caught it before a real request against a real
store did. Re-verified after the fix, corpus-wide: 2,992 of 3,027 real hook steps (every `postToolUse`
hook) now resolve a real trigger tool name, and the remaining 35 (every `sessionStart` hook) honestly
resolve the stated `Absent` case — see this file's own `Status` section for the exact numbers and the
real session/step this was confirmed against in a live browser, both before and after the fix.

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
check was actually designed rather than assumed to reuse the since-deleted `ToolVocabularyMismatchCheck`'s own
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

`PromptTextLookup.cs` closed a real gap `Findings.SessionRecording.cs`'s own stale doc comment
claimed was structural ("Copilot's event log carries no separate prompt entity"): a real
`user.message` event carries the literal prompt text (`data.content`), verified against the live
corpus, joined to a `Prompt` step by `interactionId`. `SessionTapeStepEnvelope.PromptText` (new,
nullable) carries it — `Label` stays the turn's own `Outcome`, unchanged. `GetSession` calls
`PromptTextLookup.FindForPromptSteps` alongside `StepEvidenceLookup.FindThinkingForPromptSteps`,
reusing the identical `rawEvents`/`promptStepIds` already resolved for that call — no new store read.
`web/src/session/Tape.tsx` renders `promptText` as a `Prompt` row's own label when present, falling
back to the outcome label otherwise. Verified against the live 35-session reference
corpus via a real `GET /api/sessions/{sessionId}` request and a real browser: a real session's tape
renders real prompt text ("run ef database update for both auth and regular projects") in place of
the bare outcome label it showed before.

The limitation this note used to record — that `PromptText` inherited `Thinking`'s own `StepId`
(`Turn.TurnId`) collision exposure — **is closed**: a `Prompt` step's `StepId` is now `Turn.EventId`,
and all three prompt-step lookups here match on the `turn_start` envelope's own `id`. See "A `Prompt`
step is matched on its `turn_start` envelope id" above for the before/after corpus measurements,
including why `PromptText`'s own coverage figure correctly *fell* while its correctness rose.

Digest session-naming Slice 2 (`SessionLabelLookup.cs`) closed the "name the session instead of a
GUID" gap for the Digest's own per-finding session links: `RepositoryScopeEnvelope.SessionLabels`
(new, additive) carries each in-scope session's own display label — the first five words of its
earliest real prompt, truncated server-side — resolved by `GetDigest` over the identical
`scopedRawEvents` grouped by session `HookFailureEventLookup`/`DeclaredIntentLookup` already group
there (no new store read, and unaffected by `PromptText`'s own `StepId`-collision limitation above —
a session's *first* prompt has nothing to collide with). `web/src/digest/RecurrenceStrip.tsx`
renders a session's own resolved label as its link text, with the raw session id kept as the link's
`title` (tooltip) so it stays reachable verbatim. Verified against the live 35-session reference
corpus and a real browser: the dominant repository's own 25 sessions all resolve a real label (e.g.
"run ef database update for…", "i have cors issues for…"), rendered on a real, expanded Digest
finding row in place of the bare session-id GUID it showed before.

The Settings surface's Part C closed the last gap in that surface: `POST /api/purge`
(`PurgeResultEnvelope.cs`, `ApiHost.RunPurge`), the only destructive endpoint this codebase serves,
and the first route with a gate of its own beyond the two every write route shares — see "A
destructive route needs a gate that proves intent, not only provenance" above.

Verified against the operator's real 35-session store (248,815,616 bytes), not only at the unit
level, and with the store backed up outside the store folder first. All four refusals were probed
against a real `serve --port 5120` before anything was deleted, each answering `403` with the store
still on disk afterwards: no confirmation header; a wrong confirmation value (`rebuild`); a
cross-origin `Origin` carrying a *valid* confirmation header (the header never overrides the origin
guard); and the full pre-#142 CSRF shape (`Origin: https://evil.example` + `Content-Type: text/plain`
+ a valid confirmation header), which is refused by the origin guard first and reports that reason.

Then end to end in a real browser: the confirmation field resolves a real accessible name in Chrome
("Type purge to confirm", read from the accessibility tree — the gap that bit `DateRangeFilter` is
not present here), a near-miss (`purg`) leaves the button genuinely disabled, and the real button
against the real store rendered "Deleted 1 file, reclaiming 237.3 MB." naming the real path. The
store file was then genuinely absent from disk, `GET /api/app-state` answered `emptyStore`, the
global banner flipped to "Nothing has been ingested yet.", and the configuration block re-read "Does
not exist yet — nothing has been ingested". Recovery was verified through the page itself rather than
from the backup: the same page's own Ingest button re-ingested 35 of 47 found sessions in 13.5s
(56,138 lines parsed, 0 skipped), after which `/api/digest` served the corpus's own familiar 35
sessions and 297 ranked findings and the app state returned to `ready` — which is the claim the purge
result's own sentence makes ("Run ingest to rebuild the store from your Copilot sessions"), now
measured rather than asserted.

### The repository selector re-scopes the analysis, like the date filter — and until it did, most of the corpus was unreachable

`BuildRepositoryScope(sessions)` took no caller input at all: it always picked whichever repository
carried the most sessions (ties ordinal), and `/api/digest` had no parameter through which a client
could say otherwise. The web app's `RepositorySelector` was built against that as a deliberate seam
(`web/CLAUDE.md`'s own former "seam, not a working cross-repository switch" note) — selecting a
different repository changed what the `<select>` displayed and nothing else. The consequence was not
cosmetic: on the live 35-session reference corpus, 25 sessions belong to the dominant repository, so
the **other repository's findings were unreachable through the entire product** — no surface could
rank them, behind a control that looked live. `/rules` and `/monitor` still reuse this same default
and have no selector at all; that is untouched here and remains open.

`RepositoryParameter` (`repository`) closes it for the Digest, deliberately mirroring the date
filter's own shape rather than inventing a second one:

- **It re-scopes, it does not display-filter.** `BuildRepositoryScope(sessions, requested)` resolves
  `SelectedRepository` before anything else runs, so `repositorySessionIds` → `scopedSessionIds` →
  `BuildFindingsForScope` → `servedRepositoryScope` all follow with no further change. Every count,
  recurrence and rank is computed over the requested repository's own sessions, exactly as they
  already were for the default one — the same (b)-not-(a) reasoning the date-filter entry below sets
  out at length. It also composes with `from`/`to`: repository picks the session set, the date range
  narrows it further.
- **An unknown value is a caller error, not a fallback.** Falling back to the default would serve the
  default repository's entire ranking under the name of a repository the operator asked for — "this
  repository has these findings" is a worse answer than a refusal. It throws `ArgumentException`,
  which the route's existing `catch` (built for the inverted range) already turns into a 400, so this
  needed no new error path.
- **`AvailableRepositories` is never narrowed to the selection.** It is what the selector offers, so
  narrowing it would collapse the control to one option after the first switch and strand the operator
  in the repository they just moved to. `A_requested_repository_still_serves_every_available_repository`
  pins it.
- **`MastheadCounters` stays corpus-wide; rule coverage follows the selection.** The same split the
  date filter already draws, for the same reason — the masthead states a fact about the store, while
  `BuildRuleCoverageStatus` resolves through `repositoryScope.SelectedRepository` and so describes
  whichever repository is actually being ranked. A coverage bar describing one repository's rules
  above a ranking of another's would be the same figure-contradicts-its-own-scope defect the
  methodology footer had to be corrected for once already.

### A date-range filter re-scopes the whole analysis, not merely which already-computed findings display

The pager & date-range filter task's own real design question, settled before writing any code:
`Finding` carries no date of its own — only `Recurrence.Occurrences` (`AecoPostMortem.Findings/
CLAUDE.md`) names the sessions a finding recurred in, and only `Session.StartedAt` is dated. Two
readings of "filter findings from X to Y" existed: (a) keep every count/rank exactly as already
computed and merely hide a finding whose occurrences all fall outside the window, or (b) narrow which
*sessions* every check runs over and recompute everything from there. (a) was rejected: it would
serve a `sessionsAffected` figure, a recurrence strip and a rank position that still silently counted
occurrences the operator explicitly excluded from view — this project's own repeated discipline
("never serve a number that could mislead," `MastheadCounters`'s "one served figure, never recounted
differently" rule, `RepositoryScope`'s own "the served strip and the sessions every check ranks over
are structurally guaranteed to agree") rules that out as dishonest by this codebase's own standard.
(b) was built instead: `GetDigest(store, DateOnly? from, DateOnly? to)` narrows `scopedSessionIds` —
already an intersection with the selected repository — by `Session.StartedAt` falling within
`[StartOfDayUtc(from), EndOfDayUtc(to)]` (both bounds independent, either or both omittable) *before*
calling `BuildFindingsForScope`, so every check re-runs over exactly the narrower session set the same
way it already does for repository selection — no `Findings`-layer change was needed at all, since
`RepositoryScope`/`ProcessDigest.Build` already treat their inputs as "already resolved, filter
upstream."

Two counters were then a real scope decision each, both resolved by the identical reasoning
`MastheadCounters`'s and rule coverage's existing "corpus-wide, ignores repository selection" behaviour
already establishes one dimension of (see "`GetDigest`'s rule-coverage figure..." above): a date range
is the same kind of ranking-scope lens repository selection already is, not a second corpus-wide fact
to also narrow. `MastheadCounters` (`BuildMastheadCounters`) and the served rule-coverage figure
(`BuildRuleCoverageStatus`) both still read the corpus-wide, repository-unfiltered inputs, unaffected
by `from`/`to` — the masthead states what the whole corpus/repository looks like regardless of which
window is currently being ranked within. `RepositoryScope.SessionIds`, by contrast, *follows* the
filter: its own documented contract ("the same set every check ran over," above) has to keep holding
whether or not a date filter narrowed that set further, so `GetDigest` re-derives it (chronologically
ordered, the same tie-break `BuildRepositoryScope` already uses) whenever the date-filtered set is a
proper subset of the repository-only set, and reuses the unfiltered `RepositoryScope` verbatim
otherwise (`from`/`to` both `null` behaves byte-for-byte as before this filter existed — no test in
`DigestRouteTests.cs` predating this change needed to change).

The pager was decided by the real corpus size, not by assumption: the live 35-session reference
corpus serves 297 ranked findings for its dominant repository, and the whole `DigestEnvelope` (already
fetched in one shot on every existing page load) is about 1.3 MB — small enough that a server-side
offset/limit wire contract (a new `total` field, a new pagination parameter pair, and a second way for
the served count to disagree with what a client renders) is not justified yet. `Pager` (`web/
digest/Pager.tsx`) is a client-side, dumb slice over `rankedFindings`, deliberately deferred to a
later story if a corpus's real scale ever demands it — see `web/CLAUDE.md`'s matching note.

An inverted range (`from > to`) is a caller error, not a designed empty state: `GetDigest` throws
`ArgumentException`, and the route handler answers `400 Bad Request` — the same "an honest refusal,
not a silently empty result" shape `MonitorComparisonRoute`'s own missing-parameter 400 already
established, rather than folding it into `DigestState.Analyzed`'s existing "no findings" wording, which
would read as "the range genuinely has nothing in it" instead of "the range itself doesn't make sense."

Verified against the live 35-session reference corpus (not only at the unit level): the dominant
repository's own 25 sessions span `2026-04-28` to `2026-05-31` — `from=2026-04-28&to=2026-05-31`
(covering that whole span) reproduces the identical unfiltered 297 findings / 25 sessions byte-for-byte
(a true superset match), `from=2026-04-28&to=2026-05-10` (half that span) narrows to 281 findings / 16
sessions with `masthead.sessionCount` still honestly 35, and `from=2026-05-31&to=2026-04-28` (inverted)
answers 400 — confirmed both via direct `GET /api/digest` requests and in a real browser exercising the
real `DateRangeFilter`/`Pager` controls end to end (the top-ranked finding's own count moved from "25
of 25 sessions" to "16 of 16 sessions" under the filter, and the silent-checks section's own population
moved from "24 checked" to "15 checked," both matching the served JSON exactly).

**Two independent code reviews (an opus subagent and the coordinator's own separate pass) both
caught the same real gap and one duplicate-validation smell, fixed in the same round as the initial
implementation, not a later story:**

A date range matching zero sessions in the selected repository is real, reachable behaviour (two
clicks against a non-empty store), and every one of the ten check orchestrators
(`BuildFindingsForScope`) sets `CheckRunStatus.Ran` unconditionally — including over a population of
zero — so `ProcessDigest.Build` still derives `DigestState.Analyzed` for it. Served as-is, this is
honest at the wire level (`SilentCheckEnvelope.From` is a pure filter, `Api/CLAUDE.md`'s own remarks
above — it does not synthesise; a `population: 0` entry is a real fact about a check that genuinely
ran over nothing), but the *client* rendering that fact without qualification would read as "clean",
exactly the "clean vs. never looked" conflation PRD §3.9 names — this repo's own "Checks that found
nothing" section states as much in its own copy. The fix landed at `DigestPage.tsx`, not by refusing
to project the entry at the API layer: `SilentCheckEnvelope.From`'s contract stays a pure filter for
every other caller (a `population: 0` entry is still meaningful data — "this check ran, over an empty
set" — that a different client might legitimately want), and `GetDigest` still serves the honest,
un-opinionated fact; `DigestPage` is where "was anything looked at" and "what to say about it" are
this app's own decision, the same place the other three designed `DigestState` sentences already
live. See `web/CLAUDE.md`'s matching entry for the render-side branch and its own real-corpus/real-
browser verification (`from=2026-01-01&to=2026-01-31`, zero matching sessions in the dominant
repository).

The duplicate `from > to` pre-check the route handler originally carried alongside `GetDigest`'s own
throw (both reviews caught this independently) is gone — `GetDigest` is now the one place this
validates, and the route handler's `catch (ArgumentException ex) when (ex is not ArgumentNullException)`
maps it to 400 instead. `ArgumentNullException` is explicitly excluded from that catch: a null
`store` is a genuine caller bug, not a client-supplied 400 the route should paper over.

`ParseTimestamp`'s own `DateTimeStyles.RoundtripKind` reads an offset-less timestamp as the parsing
machine's local time — harmless for the masthead span (a display value the client re-formats
`timeZone: 'UTC'` regardless) but a real determinism gap (PRD §3.8) for the date filter specifically,
since `IsWithinDateRange` compares against fixed UTC boundaries (`StartOfDayUtc`/`EndOfDayUtc`): a
local-time misread would shift which side of a boundary a session falls on depending on the server's
own machine timezone. Every real timestamp in the live reference corpus carries an explicit `Z`, so
this was latent, not observed — `ParseTimestampAsUtc` (`DateTimeStyles.AssumeUniversal |
AdjustToUniversal`), a new, dedicated parse used only by the date filter, closes it without touching
`ParseTimestamp` itself (still used, unchanged, by the masthead span — a display value with two other
call sites this filter's own correctness does not need to revisit). `internal`, with
`InternalsVisibleTo` added to `AecoPostMortem.Api.csproj` for `AecoPostMortem.Api.Tests` specifically
so the regression test (`DigestRouteTests.An_offsetless_timestamp_is_read_as_UTC_not_the_parsing_
machines_local_time`) can assert `DateTimeOffset.Offset == TimeSpan.Zero` directly — deterministic on
every machine, unlike a differential HTTP-level test built on a real timestamp, whose own pass/fail
would otherwise depend on the CI machine's ambient local offset (the same non-determinism PRD §3.8
exists to rule out, one layer up from the bug itself).

What triggered a hook (this task) closed a real gap: a hook row said a hook ran, never what it ran in
response to. `HookTriggerEnvelope` (`ToolInvocation`/`Absent`) and `HookTriggerArguments` are the new
wire contract, served two ways — eagerly, on the tape itself (`SessionTapeStepEnvelope.TriggeredBy`,
a `Hook` step's own trigger tool name, resolved by the new `HookTriggerNameLookup`) and fully, on
demand, once a step is selected (`StepEvidenceEnvelope.Trigger`, resolved by
`StepEvidenceLookup.FindTrigger` from the same `hook.start` anchor `Find` already resolved for the
Raw tab). Building this task's own mandatory real-corpus check surfaced and fixed a real, pre-existing
defect unrelated to the trigger work itself — a `Hook` step's Raw tab had never actually resolved
against real data at all, because `StepEvidenceLookup.Find`'s Hook branch matched the wrong field
(the envelope's own `id` instead of `data.hookInvocationId`, the field `Data.Execution.Hook.EventId`
— a Hook step's own `StepId` — is actually keyed by). See the non-obvious decision above for the full
story and the fix (`FindByDataField(ordered, "hook.start", "hookInvocationId", stepId)`).

Verified against the live 35-session reference corpus, corpus-wide, via the live API (not only at the
unit level): of 3,027 real hook steps, 2,992 (every real `postToolUse` hook) now resolve a real
trigger tool name and 35 (every real `sessionStart` hook) honestly resolve the stated `Absent` case —
before the Hook-identity fix, a direct measurement showed 0 of 97 real hook steps in one real session
resolved anything at all. `toolArgs` is parsed by reusing `Ingestion.ToolArguments.Parse` directly
(FR-4): a real `postToolUse` trigger with an object-shaped `toolArgs` (e.g. `skill` →
`{"skill":"using-superpowers"}`) and a real `apply_patch` trigger with a string-shaped `toolArgs` (its
whole patch body, never coerced into an object) were both confirmed via a real `GET /api/sessions/
{sessionId}/steps/{stepId}?kind=hook` request. `toolResult` is served whole — measured max 199,831
characters (~200 KB) across the corpus, an order of magnitude past the ~43 KB max the prior "a tool
call's own result" precedent measured, still served the identical way, bounded only on the client.

Verified in a real browser against the live store (session `03655527-e563-4df7-a73f-eea0903a1752`,
`supahfly27/UpFront`): the `sessionStart` hook step at 43.3s (step id `10a44d08-…`) renders "TRIGGERED
BY — No tool trigger resolved — see Raw tab for detail." on the Detail tab and "TRIGGER — The
sessionStart hook carries no tool trigger; it did not fire in response to a tool call." on the Raw
tab; the `postToolUse` hook step at 53.6s (step id `814db88c-…`, the `skill` tool) renders "TRIGGERED
BY — skill" on the Detail tab and the full "TRIGGER — skill — `{"skill":"using-superpowers"}` — ⟨the
real 18,574-character `toolResult`⟩" on the Raw tab, scrolling inside its own bounded block rather
than pushing the page down. `web/CLAUDE.md`'s matching Status entry names the same real check.

### The Monitor's refusals are served, not re-derived — and a pre-existing concurrency defect this uncovered

`GET /api/monitor-comparison` used to answer one bare, bodyless `404` for three structurally
different refusals. `web/CLAUDE.md`'s "The Monitor's two refusals are resolved on the client" recorded
the consequence and named the alternative as deferred: the one client that needed to tell them apart
re-implemented `Rules.RuleSetVersionAdjacency.RequireAdjacentPair`'s own sort-and-index logic in
TypeScript, with nothing pinning the two together, plus a second workaround in `MonitorPage.tsx` for
the third cause. `MonitorComparisonResultEnvelope` is that deferred fix: a closed union
(`comparison`/`notAdjacent`/`noComparableRule`/`noRepository`), all served `200`, and both client-side
workarounds are deleted.

**The ordering became a real decision the moment the reasons were told apart.** This method used to
look for a comparable rule *before* adjacency was ever checked (`Compare` checked it internally,
after). Both collapsed to the same 404, so the order was unobservable. It now checks adjacency first,
matching `Findings.MonitorComparison.Compare`'s own documented discipline ("the comparison refuses
before it resolves anything"): a non-adjacent pair whose `after` version also carries no comparable
rule is *primarily* not adjacent, since that is true regardless of any rule.

A version hash no session ever carried is still a `404` — that names something that does not exist,
the same split `GetRulesInventory` already draws. `notAdjacent` carries the intervening versions the
server already computes for `NonAdjacentRuleSetVersionsException`, so a client can say *why*.

**A real, pre-existing defect this task's own real-browser pass uncovered — since fixed, see
`Data/CLAUDE.md`'s "The foreign-file guard reads the header with full sharing": this host answered
`500` for concurrent requests.** Chasing a "Could not reach the local API" message in a real
browser (reproducible 3 of 3 by changing both version selects rapidly) led to a measurement against
the live host: 6 overlapping `/api/monitor-comparison` requests answered 20 × `500` and 4 × `200`,
while the identical requests issued sequentially all answered `200`. It is **not** specific to this
endpoint or to this change — `/api/app-state` and `/api/rules-inventory`, neither touched here, each
answered `500` for 1 of 6 concurrent requests on the same build. The shared factor is `store.Open()`
per request (`Data/CLAUDE.md`'s `Pooling=False`, plus `Database.Migrate()`/`DerivedSchema.
EnsureCurrent` on every open). The write gate (above) covers writer-vs-writer only; nothing serialises
concurrent *readers* opening the store.

This change made the Monitor page hit it more easily — every version change now issues a request,
where a non-adjacent selection previously issued none — which is what surfaced it. The cause was not
what the symptom suggested (nothing to do with SQLite locking, no read gate needed): `LocalStore`'s
own foreign-file guard opened the store with `FileShare.Read`, denying write sharing, so it threw
whenever any connection had the file open. After the fix, 30 overlapping requests across every read
endpoint answer `200`.

### The Settings surface: this project's first POST endpoints, and where their logic lives

The Settings page (`web/CLAUDE.md`) needed a read-only configuration view (Part A, `GET
/api/settings`) and two write actions (Part B, `POST /api/ingest`/`POST /api/rebuild`) — every POST
endpoint that exists anywhere in this codebase today. Two design questions had to be settled before
writing either route: what the API calls, and how concurrent writes are kept from corrupting the
store.

**What the API calls.** `Cli` references `Api` (`Cli/CLAUDE.md`'s own References section) — never
the other direction — so `ApiHost.RunIngest`/`RunRebuild` cannot call into `AecoPostMortem.Cli` at
all, and duplicating `CommandRunner.Ingest`/`Rebuild`'s own logic inline here was the only apparent
alternative. It turned out neither `Cli` method holds real business logic worth duplicating: `Ingest`
is a thin wrapper over `Ingestion.IngestionRun.Run` (already a direct, reusable entry point — the CLI
itself does no more than resolve which source root and exclusion list to pass it), and `Rebuild`'s own
drop-and-recreate-then-rederive sequence was inlined directly in `CommandRunner` before this task
(`Data.Execution.DerivedSchema.Rebuild` plus a loop over `Ingestion.NormalizedLayerWriter.Derive`) —
worth sharing, not duplicating. That sequence is now `Ingestion.NormalizedLayerWriter.RebuildAll`
(`Ingestion/CLAUDE.md`'s own remarks), the one place "what rebuild means" is defined; both
`CommandRunner.Rebuild` and `ApiHost.RunRebuild` call it. `RunIngest` and `RunRebuild` therefore call
`Ingestion`/`Data` directly — the same two projects `ApiHost` already references for every read
endpoint — rather than reaching into `Cli` or copying its dispatch logic. `RunIngest` has no request
body: the source directory served is always the one `Build` was given
(`copilotSessionStateRoot`, the same parameter `DiagnoseAppState`/`GetSettings` already read), unlike
the CLI's own optional positional path argument — an operator driving a browser has no terminal to
type an override into, and adding one would be unused surface for this slice.

**Concurrency.** Both write routes share one `SemaphoreSlim(1, 1)`, declared as a local inside `Build`
(so two hosts built in the same test process, or two builds against two different stores, never share
one gate) and captured by both route closures — `RunGated` is the seam: `gate.Wait(0)` never blocks
the request thread, so a second click, a second browser tab, or a rebuild fired while an ingest is
still running all get an immediate `409 Conflict` rather than a silently queued request or a
request racing the first one against the same SQLite connection (`Pooling=False`,
`Data/CLAUDE.md`). One gate for both routes, not one each — `ingest` and `rebuild` both open and
write through the identical store file, so a rebuild starting mid-ingest is exactly as unsafe as two
ingests overlapping. A caught exception is served as `Results.Problem(detail: ex.Message, ...)`, not
a bare unexplained 500 — "a failed ingest must show what failed" (the brief's own Scenario 2) — and
the gate is always released in a `finally`, proven by `RunGatedTests.
A_failed_run_still_releases_the_gate_and_reports_the_failure` so one failed write can never
permanently lock out every future one. `RunGatedTests` exercises this deterministically against a
manually-held `SemaphoreSlim` rather than by racing two real HTTP requests against each other — two
sub-millisecond in-memory operations are not reliably reproducible as a race over a real socket, so
the gate's own refusal/release logic is proven directly instead.

The gate covers writer-vs-writer only — a **reader** racing a concurrent `POST /api/rebuild` was a
real, separate gap the write gate does nothing about (code review, Important), closed instead in
`Ingestion.NormalizedLayerWriter.RebuildAll` itself (`Ingestion/CLAUDE.md`'s own remarks): the derived
tables are genuinely dropped and absent for a real window mid-rebuild, and a `GET /api/digest` or
`/api/app-state` from a second browser tab landing in that window, on its own SQLite connection, would
otherwise hit "no such table" — a hard error, not the retriable busy/timeout SQLite gives a reader
waiting on an open write transaction. `RebuildAll` now wraps its whole drop-then-repopulate sequence
in one transaction, so a concurrent reader sees either the pre-rebuild tables or the fully-repopulated
post-rebuild ones, never neither. This risk existed in kind before this task (running `aecopostmortem
rebuild` in a terminal while a separate `serve` process was up), but was newly, easily reachable once
`rebuild` became a route inside the same long-lived process serving those reads.

**The threat model this host assumes.** `ApiHost.Build` binds `127.0.0.1` only (`ApiHost.cs`'s
existing "not `localhost`" decision, above) — no remote machine can reach either write route. Within
that boundary this is a single-operator local tool with no authentication on any route.

### Origin and Host validation close a live simple-request CSRF path (code review, follow-up round)

The first version of this paragraph framed the cross-origin write risk as theoretical and
DNS-rebinding-only ("a hostile page could fire a blind POST whose response it can never read, so the
worst it can do is waste CPU"). That framing was wrong about how live the plain-CSRF case already
was, and the coordinator verified it directly against a running `serve --port 5111` instance:

```
POST /api/rebuild
Origin: https://evil.example
Content-Type: text/plain
```

returned `200` with a real, genuine rebuild — no rebinding needed. `Content-Type: text/plain` (or no
body at all) makes this a CORS **simple request**: the browser never sends a preflight `OPTIONS`
for it, and this host added no CORS policy of its own, so nothing before this round of the task
stopped the write from actually running. CORS only ever stopped the attacker's page from *reading*
the response — it never stopped the write itself, which is the side effect that matters for `ingest`/
`rebuild`. The default port (`CommandRunner.DefaultPort`) is a fixed, documented constant, so it is
trivially guessable by any page that wants to try it blind.

`ApiHost.IsAllowedWriteOrigin` is the fix, checked before either write route reaches `RunGated` (a
refusal never runs the command underneath it, answering `403` via `Results.Problem`):

1. **`Origin`, when present, must equal this host's own real origin exactly** — the load-bearing
   check. A browser adds `Origin` to every cross-origin request and to most same-origin
   state-changing requests too (the Fetch spec adds it for any non-GET/HEAD request, regardless of
   `Content-Type`), and a real attacker page's own `Origin` can never be spoofed to read as this
   host's. A request with **no** `Origin` header at all — curl, the CLI's own tests, this suite's own
   `HttpClient` (which never sets it) — is not refused on that basis alone: there is no
   browser-enforced guarantee to check for a caller that never sends one, and refusing it outright
   would break every non-browser caller these two routes also have to serve.
2. **`Host` must resolve to this same connection's own real, actually-bound loopback authority** —
   read per request from `HttpContext.Connection.LocalPort` (the real, OS-assigned port for *this*
   accepted connection), never the `port` parameter `Build` was originally given, which is `0` for
   every test using an ephemeral port and would never match a real bound port if trusted directly.
   This is what actually closes DNS rebinding, corrected from the prior framing: a rebound page's own
   `Origin` header still names its real origin (its hostname, not the IP the DNS answer was switched
   to), which check 1 above already refuses on its own — but the same attack could otherwise send
   `Host: evil.example:<port>` while physically connecting to `127.0.0.1`, and validating `Host` too
   closes that path independently of whatever `Origin` claims, rather than leaning on check 1 alone
   for a threat this file used to name as its whole justification for a `Host` check.

Both checks are permissive toward a non-browser caller by design (`WriteRouteOriginTests` proves the
absent-`Origin` case explicitly, alongside a cross-origin `Origin` refusal, a matching same-origin
`Origin` allowance, and a spoofed, non-loopback `Host` refusal with no `Origin` header present at
all — the DNS-rebinding shape). Re-verified against a real browser on the real served page after this
fix: same-origin `POST /api/ingest`/`POST /api/rebuild` from `http://127.0.0.1:5111/settings` still
succeed end to end (real coverage report / rebuild summary rendered), since the page's own `Origin`
and `Host` are both this host's real authority by construction.

**Why this reasoning did not extend to the `purge` endpoint** (written while `purge` was still
unbuilt; it has since landed, behind the extra gate this paragraph predicted it would need — see
"A destructive route needs a gate that proves intent, not only provenance" below). `ingest` and
`rebuild` were accepted as safe-to-trigger-uninvited even before this fix — `ingest` is idempotent by
construction (RAW's own `(source_file, byte_offset, content_hash)` conflict target, `Data/CLAUDE.md`)
and `rebuild` only re-derives already-stored RAW, never reads the source directory again — so an
uninvited trigger could waste CPU but never destroy data. That reasoning is now additionally backed
by a real guard rather than resting on "safe replay" alone, but it still does not extend to a
destructive endpoint: deleting the operator's whole store from an uninvited request would be a real,
destructive consequence no amount of "it was idempotent anyway" reasoning covers, which is exactly
why `purge` stays deliberately unbuilt here. The next person adding a POST that *can* destroy data
inherits a working Origin/Host guard to build on top of, not only a paragraph — but a destructive
route may still warrant a stricter gate on top of this one (a confirmation token or an
operator-supplied header), since `IsAllowedWriteOrigin` only proves a request came from this host's
own served page, not that the operator specifically intended *this* action.

### A destructive route needs a gate that proves intent, not only provenance

`POST /api/purge` deletes the operator's whole store. It is served behind everything the other two
write routes are — the shared `SemaphoreSlim` write gate (a purge landing mid-ingest is exactly as
unsafe as two ingests overlapping) and `IsAllowedWriteOrigin` — plus one gate neither of them has: a
required `X-AecoPostMortem-Confirm: purge` header (`ApiHost.IsConfirmed`), checked *after* the origin
guard and *before* `RunGated`, so a refusal never reaches `LocalStore.Purge`.

The reason the origin guard alone is not enough is stated in that guard's own paragraph above:
`IsAllowedWriteOrigin` proves a request came from this host's own served page; it cannot prove the
operator meant to destroy their store. Anything that page can be made to do — a stray script, a
mis-wired future component, a click on the wrong control — passes it. The header closes a second gap
too, and this one is browser-enforced rather than convention: a custom header makes a cross-origin
`fetch` a CORS **non-simple** request, so the browser preflights it and this host, which answers no
CORS policy at all, fails that preflight before the real request is ever sent. That is the exact
request shape (`Content-Type: text/plain`, no preflight) that made `POST /api/rebuild` reachable
cross-origin before the origin guard existed.

The value names the action rather than being a generic "yes", so a header a future destructive route
copies cannot authorise the wrong one, and it is compared ordinally: this is a machine token from
this app's own client, not operator input to be forgiving about. The operator's own confirmation is a
separate, client-side gate — a typed word, `web/CLAUDE.md`'s "A destructive button is armed by
typing, not by clicking" — and the two are deliberately different mechanisms rather than one repeated
twice: the header proves *which* action a request intends, the typed word proves *a person* intended
it.

`RunPurge` itself carries no confirmation check. The gate belongs at the route, where the request's
own headers are; a direct caller of the method (the CLI, a test) has already stated its intent by
calling it, and putting the check inside would mean either passing a fake header around or a second,
divergent notion of "confirmed".

### The write gate is a parameter, not a local — so "every write route is behind it" is tested, not asserted

`RunGatedTests` proves the gate's own refusal and release logic deterministically, and deliberately
does not race two real HTTP requests (its own remarks explain why). But that leaves the claim this
host actually depends on — *which routes consult that gate* — checked by nobody: a fourth write route
wired to its own `new SemaphoreSlim(1, 1)`, or to none at all, would pass every test in this project
while silently allowing a concurrent write.

`Build` therefore has an `internal` overload taking the `SemaphoreSlim`, with the public one
unchanged and still creating a fresh gate per host. A test holds the gate it passes in, then asserts
all three write routes answer `409` — `PurgeRouteTests.
Every_write_route_including_purge_is_served_behind_the_one_shared_gate`. It is deterministic by
construction rather than by timing: the gate is held before any request is sent, and `RunGated` uses
`Wait(0)`, so nothing blocks and no race has to be reproduced over a socket.

The test was mutation-checked rather than trusted for passing: rewiring `PurgeRoute` to
`RunGated(new SemaphoreSlim(1, 1), ...)` — a plausible copy-paste error, and exactly the defect this
guards — makes it fail. A test that only ever passed would have proven nothing here, since the
assertion it makes was previously unreachable at all.

### What an operator sees immediately after a purge — measured, and the opposite of what was predicted

This task's design pass predicted a wrinkle that turned out not to exist, which is worth recording so
nobody re-derives the wrong worry: a completed purge fires `notifyStoreChanged()`, which re-fetches
`GET /api/app-state`, and `DiagnoseAppState` opens the store — so the file was expected to be
recreated (empty, by migrations) within a second of being deleted, leaving Settings reporting a store
that "exists" right after saying it deleted one. It does not happen: `StoreHasBeenIngested` returns
`false` on `!store.Exists` *before* calling `store.Open()`, so nothing on the post-purge path
recreates the file. Verified against the real store, not reasoned about: after a real purge through
the real page, the store's own directory was empty, a direct `GET /api/app-state` answered
`emptyStore`, and the directory was still empty afterwards.

### Real timing measured, not assumed, before deciding this stays synchronous

No async job/queue infrastructure exists in this codebase, and PRD §3.1's "single local process"
shape gives no obvious place to add one. Whether that is actually safe for a synchronous HTTP handler
depends on real numbers, not on the shape of the architecture: measured against the live 35-session
reference corpus (56,138 RAW events) on the machine this task was built on, a full `ingest` run (CLI,
Release build, incremental — every session already stored, so this is the worst case for "found
nothing new to do but still walked every session") took **16.0s**, and a full `rebuild` took **6.8s**.
Both are comfortably inside any default HTTP client or Kestrel request timeout, and — this being a
single-operator local tool the brief explicitly rules async infrastructure out of scope for — a
synchronous `MapPost` handler that blocks the request thread for single-digit seconds is an accepted
trade-off, not an oversight. The frontend still treats the wait as real (`SettingsPage`'s own "Running
ingest…"/"Running rebuild…" `role="status"` text, `web/CLAUDE.md`) rather than assuming it is
instant, so a corpus large enough to take noticeably longer than this still reads as "working," not as
a hung page.

### `SilentCheckEnvelope.From` refuses on `CheckRegistry.SessionsInScope`, not on any individual entry's own zero population

PR #138 (the pager & date-range filter task) closed "a range matching zero sessions reads as ten
clean checks" only at the render layer — `DigestPage.tsx`'s own `noSessionsInRange` gate hides the
ranked list, pager, judgment calls and clean-checks sections together, all client-side. Verified
directly against the live corpus before this fix: `GET /api/digest?from=2026-01-01&to=2026-01-31` (a
window matching zero sessions) served all ten checks as clean, every one carrying `population: 0` —
honest per entry, misleading as a collection, and a second client reading this endpoint directly
(bypassing `DigestPage.tsx`) had no way to tell "ten checks ran clean" from "nothing was analysed".
FR-33's own precedent (`web/CLAUDE.md`'s "one component owns both halves of an adherence figure" —
"a second client bypassing the UI must be equally unable to get a bare figure, the server contract
handles that half") is the standard this fix applies to `SilentCheckEnvelope`.

The real design question this task had to settle: is a zero `CheckRegistryEntry.Population` always
"never looked"? Verified against the live 35-session reference corpus — the unfiltered digest serves
3 silent checks (`banned-tool-used`, `use-a-after-b`, `always-pass-param`), each with population 24,
never 0 for any of the ten checks while real sessions are in scope. But the codebase's own per-check
`Population` formulas (`Findings/CLAUDE.md`'s "checks that found nothing" section) show this is not
guaranteed to hold everywhere: most checks count *distinct sessions among their own candidate items*
(tool calls, turns, permissions+questions, declared intents) rather than the whole session scope —
`phase-churn`'s population is sessions with at least one declared intent, for instance, which could
genuinely be zero in a real, non-empty session scope where no session ever called `report_intent`.
Only `HookFailureFinding.Population` (`allSessionIds.Count`) is structurally pinned to the whole scope
size. That means a check's own zero population and "the whole analysis scope was empty" are genuinely
different situations that happen to share a number, and a blanket `Population == 0` filter on
`SilentCheckEnvelope.From` would suppress a real, honest clean result the narrower way (a check that
ran over real sessions and genuinely found no candidates of its own kind).

The fix: `Findings.CheckRegistry` gained a new required field, `SessionsInScope` — the size of the
whole analysis scope every entry in that registry was drawn from, set once by
`ApiHost.BuildFindingsForScope` to `scopedSessionIds.Count`, the same count `RepositoryScope.
SessionIds` already carries for this exact scope (`GetDigest`'s own remarks above, on
`servedRepositoryScope`). `SilentCheckEnvelope.From(CheckRegistry)` keeps its single-parameter,
"pure filter over one input" shape (this file's own pre-existing remarks on it) — it now refuses
structurally, returning `[]` unconditionally, when `registry.SessionsInScope == 0`, before ever
looking at any individual entry's own `Population`. When the scope is non-empty, the existing
per-entry filter (`Status == Ran && FindingCount == 0`) is untouched: a check whose own narrower
population happens to be zero within a real, non-empty scope still serves as a genuine clean result,
by design — `SilentCheckEnvelopeTests`'s own
`A_single_checks_own_zero_population_still_serves_as_clean_when_real_sessions_were_in_scope` proves
this distinction holds, alongside its sibling proving the zero-scope refusal
(`No_checks_are_served_as_clean_when_the_analysis_scope_itself_had_zero_sessions`).

Verified against the live corpus after the fix: the unfiltered digest still serves the identical 3
silent checks at population 24 each (unaffected — `SessionsInScope` is 25 there, unchanged), and
`GET /api/digest?from=2026-01-01&to=2026-01-31` now serves `silentChecks: []` instead of ten
misleadingly-clean entries — `DigestRouteTests.A_range_matching_zero_sessions_serves_an_empty_but_
honest_scope` is the regression test for exactly this. `DigestPage.tsx` needed no change: its own
`noSessionsInRange` gate already hid `<CleanChecks>` for this exact case, so the fix is invisible to
the one client that already handled it correctly.

**What this closes, precisely, and what it deliberately leaves alone (code review).** The refusal is
keyed on `SessionsInScope`, the whole analysis scope's own size — not on the date-range filter that
happened to be this bug's own reproduction — so it also, incidentally, closes the same conflation for
an unfiltered digest whose repository scope is itself empty (an empty store, or a repository with no
sessions at all): `web/CLAUDE.md`'s "The pager is client-side" section calls that "a different,
pre-existing case this task does not touch," written before this fix existed and now stale for the
`silentChecks` field specifically — `SessionsInScope == 0` in that case too, so `SilentCheckEnvelope.
From` refuses there as well, with no code change on either side needed. What this fix did *not* close at the time — **since closed**, see below:
`Findings.Digest.cs`'s `DigestState` was still derived only from `checkRegistry.Entries.Any(entry
=> entry.Status == CheckRunStatus.Ran)`, independent of `SessionsInScope`, so
`GET /api/digest?from=2026-01-01&to=2026-01-31` served `state: "Analyzed"` alongside its now-empty
`silentChecks`, and a second client reading only `state` and `silentChecks.length === 0` could still
misread "analysed, nothing to report" as "clean" rather than "nothing was in scope".

**`DigestState.NothingInScope` closes it.** The premise this paragraph rested on — "`ProcessDigest.
Build` has no scope-size input today" — was already false when it was written: #144's own
`CheckRegistry.SessionsInScope` is a required field on the registry `Build` receives, so the fact was
in hand and simply unread. `Build` now derives a fourth state from it, above its `Any(Ran)` check
(`Findings/CLAUDE.md`'s "Three designed 'nothing to show' states"), and the same zero-session range
above now serves `state: "NothingInScope"`. Verified against the live corpus, all three probes: the
unfiltered digest still serves `Analyzed` with 297 findings over 25 sessions and 3 silent checks;
`from=2026-01-01&to=2026-01-31` serves `NothingInScope` with an empty scope; and a real 16-session
sub-range (`from=2026-04-28&to=2026-05-10`) still serves `Analyzed` with 281 findings and 3 silent
checks — that third probe is the one proving it does not over-suppress, the same discipline #144's own
three-probe verification established.

One honest consequence, recorded rather than buried: **an empty store now serves `NothingInScope`
where it used to serve `Analyzed`** (`DigestRouteTests.An_empty_store_serves_nothing_in_scope_rather_
than_a_clean_analyzed_digest`, renamed from a name that asserted the old, wrong behaviour), and
`DigestState.NotYetAnalyzed` is confirmed to have no path through this endpoint at all — `BuildFindingsForScope`
registers every check as `Ran` unconditionally, which is exactly why the empty-store case was wrong
before.
