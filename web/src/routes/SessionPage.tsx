import { useCallback, useState } from 'react'
import { useParams } from 'react-router-dom'
import type { HookTriggerEnvelope, ModelReasoningReadability, RawStepEventEnvelope, SessionAgentLane, SessionEnvelope, SessionFindingChip, SessionTapeStep, SubagentOutputEnvelope, ThinkingEnvelope } from '../api/session'
import { useSession } from '../api/useSession'
import { useStepEvidence } from '../api/useStepEvidence'
import { MethodologyFooter } from '../session/MethodologyFooter'
import { Tape } from '../session/Tape'
import { TapeMinimap, type MinimapViewport } from '../session/TapeMinimap'
import './SessionPage.css'

const numberFormat = new Intl.NumberFormat('en-US')

const KIND_LABEL: Record<SessionTapeStep['kind'], string> = {
  prompt: 'Prompt',
  hook: 'Hook',
  skill: 'Skill',
  toolCall: 'Tool call',
  mcpCall: 'MCP call',
}

type InspectorTab = 'detail' | 'thinking' | 'raw'

function formatOffset(offsetMs: number): string {
  return `${(offsetMs / 1000).toFixed(1)}s`
}

const wallClockDateTimeFormat = new Intl.DateTimeFormat('en-US', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
  timeZone: 'UTC',
})

const wallClockTimeFormat = new Intl.DateTimeFormat('en-US', {
  hour: 'numeric',
  minute: '2-digit',
  timeZone: 'UTC',
})

const wallClockDateFormat = new Intl.DateTimeFormat('en-US', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
})

/** Mockup parity item #14: the masthead's real wall-clock start→end range, alongside
 * `formatElapsed`'s own duration figure — a single session is typically minutes to hours, so
 * (unlike the Digest's own `Masthead.tsx`/`formatSpan`, which shows dates only because a corpus
 * spans months) this needs real time-of-day, not just a date. `endedAt === null` is a real state
 * (an ingest-incomplete session, per `SessionRecordingStatusEnvelope` — the masthead still renders
 * for one), stated honestly rather than left blank or shown as a dash. The end time is shown as a
 * bare time-of-day when it falls on the same UTC day as the start (the common case), and as a full
 * date and time otherwise, so a session that happens to cross midnight still reads unambiguously. */
function formatWallClockRange(startedAt: string, endedAt: string | null): string {
  const start = new Date(startedAt)
  const startText = wallClockDateTimeFormat.format(start)

  if (endedAt === null) {
    return `${startText} – still running`
  }

  const end = new Date(endedAt)
  const sameDay = wallClockDateFormat.format(start) === wallClockDateFormat.format(end)
  const endText = sameDay ? wallClockTimeFormat.format(end) : wallClockDateTimeFormat.format(end)

  return `${startText} – ${endText}`
}

function formatElapsed(elapsedMs: number | null): string {
  if (elapsedMs === null) {
    return 'unknown'
  }

  const totalMinutes = Math.round(elapsedMs / 60_000)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60

  return hours > 0 ? `${hours}h ${minutes}min` : `${minutes} min`
}

function formatContextSize(contextSize: SessionEnvelope['masthead']['contextSize']): string {
  if (contextSize.kind === 'notRecorded') {
    return 'not recorded'
  }

  return `${numberFormat.format(contextSize.inputTokens)} in / ${numberFormat.format(contextSize.outputTokens)} out`
}

/** FR-21 Scenario 1: session identity, repository, branch, CLI version, elapsed time, turns, tool
 * calls, subagents, skills, models and context size at end — all in one place, above the tape.
 * Mockup parity item #14 added the real wall-clock start→end range (`formatWallClockRange`),
 * alongside — not instead of — `Elapsed`'s own duration figure. */
function Masthead({ masthead }: { masthead: SessionEnvelope['masthead'] }) {
  return (
    <section className="session-masthead" role="region" aria-label="Masthead">
      <dl>
        <div className="session-masthead__field">
          <dt>Session</dt>
          <dd>{masthead.sessionId}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Repository</dt>
          <dd>{masthead.repository ?? '—'}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Branch</dt>
          <dd>{masthead.branch ?? '—'}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>CLI version</dt>
          <dd>{masthead.copilotVersion}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Wall clock</dt>
          <dd>{formatWallClockRange(masthead.startedAt, masthead.endedAt)}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Elapsed</dt>
          <dd>{formatElapsed(masthead.elapsedMs)}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Turns</dt>
          <dd>{masthead.turnCount}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Tool calls</dt>
          <dd>{masthead.toolCallCount}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Subagents</dt>
          <dd>{masthead.subagentCount}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Skills</dt>
          <dd>{masthead.skillCount}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Models</dt>
          <dd>{masthead.modelCount ?? '—'}</dd>
        </div>
        <div className="session-masthead__field">
          <dt>Context size at end</dt>
          <dd>{formatContextSize(masthead.contextSize)}</dd>
        </div>
      </dl>
    </section>
  )
}

/** FR-21, part 3 of 3 (S-53, issue #17), Scenarios 3 and 4: a session whose masthead and tape are
 * not yet final states that plainly, in its own words — never the generic load-failure message
 * (`role="alert"`, above), and never the other non-final state's wording either. Both render in
 * place of the masthead and tape: today's counts and steps would otherwise read as the session's
 * finished picture when they are provisional or partly unrecoverable. */
function NonFinalState({ status }: { status: Extract<SessionEnvelope['status'], { kind: 'ingestIncomplete' | 'reconstructionFailed' }> }) {
  if (status.kind === 'ingestIncomplete') {
    return (
      <div className="session-page__incomplete">
        <p>This session is still ingesting — it has not recorded its own end yet, so today&rsquo;s figures are not final.</p>
      </div>
    )
  }

  return (
    <div className="session-page__reconstruction-failed">
      <p>Reconstruction failed for this session.</p>
      <ul>
        {status.skipped.map((reason) => (
          <li key={reason}>{reason}</li>
        ))}
      </ul>
    </div>
  )
}

/** FR-21 part 2 of 3, Scenario 3 (S-52, issue #16): "a chip row states each finding affecting this
 * session with its count." An empty chip row is the designed "no findings" state, not a blank
 * area — rendered explicitly rather than omitting the row entirely. */
function FindingChips({ chips }: { chips: SessionFindingChip[] }) {
  if (chips.length === 0) {
    return <p className="session-chips__empty">No findings affect this session.</p>
  }

  return (
    <ul className="session-chips" aria-label="Findings">
      {chips.map((chip) => (
        <li key={chip.finding.recurrence.key} className="session-chips__chip" data-provenance={chip.finding.provenance}>
          <b>{chip.sessionsAffected}×</b>
          <span>{chip.finding.recurrence.key}</span>
        </li>
      ))}
    </ul>
  )
}

const OUTCOME_LABEL: Record<SessionAgentLane['outcome'], string> = {
  running: 'Running',
  completed: 'Completed',
  completedCostUnknown: 'Completed (cost unknown)',
  failed: 'Failed',
}

/** FR-22 (S-09, issue #18): the report a subagent's lane actually shows — its own closed
 * three-shape union (present/notRecorded/failed), the same "a stated value, never inferred from
 * which fields happen to be present" discipline `ThinkingEnvelope` already renders with. `failed`
 * never falls back to the parent's stub, and neither does `notRecorded` — the two are rendered
 * distinctly so a subagent that simply produced nothing reads differently from one that failed. */
function SubagentOutputPanel({ output }: { output: SubagentOutputEnvelope }) {
  if (output.kind === 'present') {
    return <p className="agent-lane__output-text">{output.text}</p>
  }

  if (output.kind === 'failed') {
    return (
      <p className="agent-lane__output-failed" role="alert">
        {output.error}
      </p>
    )
  }

  return <p className="agent-lane__output-empty">{output.reason}</p>
}

/** FR-22 (S-09, issue #18): one entry per subagent this session spawned, each with the report it
 * actually produced — "so I can judge what a subagent did rather than reading the stub its parent
 * recorded" (the story's own goal). A session with no subagents renders no section at all, the same
 * "nothing to show" discipline `FindingChips` renders explicitly instead — here there is no chip
 * row equivalent worth stating, since `masthead.subagentCount` already states the zero. */
function AgentLanes({ lanes }: { lanes: SessionAgentLane[] }) {
  if (lanes.length === 0) {
    return null
  }

  return (
    <section className="agent-lanes" role="region" aria-label="Subagent lanes">
      <h3 className="agent-lanes__heading">Subagent lanes</h3>
      <ul className="agent-lanes__list">
        {lanes.map((lane) => (
          <li key={lane.agentId} className="agent-lane" data-outcome={lane.outcome} data-agent-id={lane.agentId}>
            <div className="agent-lane__header">
              <span className="agent-lane__name">{lane.displayName}</span>
              <span className="agent-lane__outcome">{OUTCOME_LABEL[lane.outcome]}</span>
            </div>
            <SubagentOutputPanel output={lane.output} />
          </li>
        ))}
      </ul>
    </section>
  )
}

/** The Detail tab: every field already on the selected step's own `SessionTapeStepEnvelope` — no
 * fetch needed, it travelled with the tape. */
function DetailPanel({ step }: { step: SessionTapeStep }) {
  return (
    <dl className="inspector__detail">
      <div className="inspector__detail-field">
        <dt>Kind</dt>
        <dd>{KIND_LABEL[step.kind]}</dd>
      </div>
      <div className="inspector__detail-field">
        <dt>Step id</dt>
        <dd>{step.stepId}</dd>
      </div>
      <div className="inspector__detail-field">
        <dt>Label</dt>
        <dd>{step.label}</dd>
      </div>
      <div className="inspector__detail-field">
        <dt>Timestamp</dt>
        <dd>{step.timestamp}</dd>
      </div>
      <div className="inspector__detail-field">
        <dt>Offset</dt>
        <dd>{formatOffset(step.offsetMs)}</dd>
      </div>
      <div className="inspector__detail-field">
        <dt>Owner</dt>
        <dd>{step.ownerKind === 'agent' ? `Subagent (${step.agentId ?? 'unknown'})` : 'Main thread'}</dd>
      </div>
      {step.kind === 'hook' && (
        <div className="inspector__detail-field">
          <dt>Triggered by</dt>
          <dd>{step.triggeredBy ?? 'No tool trigger resolved — see Raw tab for detail.'}</dd>
        </div>
      )}
    </dl>
  )
}

const readableSharePercentFormat = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1, minimumFractionDigits: 1 })

/** FR-23 (S-10, issue #19), Scenario 2: when `reason` states the reasoning is provider-encrypted,
 * `readabilityByModel` carries this session's own measured readable share, one row per model it
 * actually used — a session using two models renders two rows, never an average of the two. */
function ReadabilityByModel({ readabilityByModel }: { readabilityByModel: ModelReasoningReadability[] }) {
  if (readabilityByModel.length === 0) {
    return null
  }

  return (
    <ul className="inspector__thinking-readability" aria-label="Readable reasoning share by model">
      {readabilityByModel.map((figure) => (
        <li key={figure.model}>
          <b>{figure.model}</b>: {readableSharePercentFormat.format(figure.readableSharePercent)}% readable
          ({figure.readableCount} of {figure.totalCount} reasoning-bearing messages, this session)
        </li>
      ))}
    </ul>
  )
}

function ThinkingPanel({ thinking }: { thinking: ThinkingEnvelope }) {
  if (thinking.kind === 'unavailable') {
    return (
      <div className="inspector__unavailable">
        <p>{thinking.reason}</p>
        {thinking.readabilityByModel != null && <ReadabilityByModel readabilityByModel={thinking.readabilityByModel} />}
      </div>
    )
  }

  return <p className="inspector__thinking-text">{thinking.text}</p>
}

/** One labeled raw-event block: the literal payload when present, or a stated reason when skipped
 * — never a blank area. Shared by the call and its result, the identical two-state shape both
 * carry. */
function RawEventBlock({ label, event }: { label: string; event: RawStepEventEnvelope }) {
  return (
    <div className="inspector__raw-block">
      <p className="inspector__raw-block-label">{label}</p>
      {event.kind === 'skipped' ? (
        <p className="inspector__unavailable">{event.reason}</p>
      ) : (
        <div className="inspector__raw">
          <p className="inspector__raw-event-type">{event.eventType}</p>
          <pre className="inspector__raw-payload">{event.payload}</pre>
        </div>
      )}
    </div>
  )
}

/** A hook row said a hook ran, never what it ran in response to — a third, separately labeled Raw
 * tab block, reusing the identical `inspector__raw`/`inspector__raw-payload` rendering (and its own
 * `max-height`/`overflow-y` bound) `RawEventBlock` already gives the call and its result, since a
 * trigger's own arguments/result are the identical "the literal payload, or a stated absence"
 * question, asked of a hook's own trigger rather than a second event. */
function TriggerBlock({ trigger }: { trigger: HookTriggerEnvelope }) {
  return (
    <div className="inspector__raw-block">
      <p className="inspector__raw-block-label">Trigger</p>
      {trigger.kind === 'absent' ? (
        <p className="inspector__unavailable">{trigger.reason}</p>
      ) : (
        <div className="inspector__raw">
          <p className="inspector__raw-event-type">{trigger.toolName}</p>
          <pre className="inspector__raw-payload">{trigger.arguments.raw}</pre>
          {trigger.result != null ? (
            <pre className="inspector__raw-payload">{trigger.result}</pre>
          ) : (
            <p className="inspector__unavailable">No result was recorded for this hook's trigger.</p>
          )}
        </div>
      )}
    </div>
  )
}

/** The Raw tab: "the provenance guarantee made clickable, not a debugging affordance" (the
 * story's own edge case) — it must never render blank, and a step whose raw event was skipped at
 * ingest states that fact instead. A tool call's own result is a second, separately labeled block
 * below the call — an operator previously saw the call going out and never what came back.
 *
 * A hook's own trigger is a third block, but — unlike Call/Result, which apply (or state "not
 * applicable") for every step kind — it is rendered only for a `'hook'` step (code review): a
 * trigger is meaningful for exactly one step kind, and showing "Trigger — Only a hook step has a
 * trigger; this step kind does not." on every Prompt/ToolCall/McpCall/Skill step in every session
 * would be structural noise on the large majority of steps, the same "no section unless there is a
 * real reason for one" discipline `Pager`/`StepFlag`/`CleanChecks` already follow elsewhere in this
 * app (`web/CLAUDE.md`). The server still serves the not-applicable state for every other kind
 * (`HookTriggerEnvelope.Absent`) — this is a frontend-only rendering choice, not a contract change. */
function RawPanel({
  raw,
  result,
  trigger,
  showTrigger,
}: {
  raw: RawStepEventEnvelope
  result: RawStepEventEnvelope
  trigger: HookTriggerEnvelope
  showTrigger: boolean
}) {
  return (
    <>
      <RawEventBlock label="Call" event={raw} />
      <RawEventBlock label="Result" event={result} />
      {showTrigger && <TriggerBlock trigger={trigger} />}
    </>
  )
}

/** The inspector body once a step is selected: fetches Thinking/Raw evidence for that step and
 * renders whichever tab is active. Detail needs no fetch — it already has `step`. */
function SelectedStepInspector({ sessionId, step }: { sessionId: string; step: SessionTapeStep }) {
  const [tab, setTab] = useState<InspectorTab>('detail')
  const evidenceQuery = useStepEvidence(sessionId, step.stepId, step.kind)

  return (
    <>
      <div className="inspector__tabs" role="tablist">
        {(['detail', 'thinking', 'raw'] as const).map((candidate) => (
          <button
            key={candidate}
            type="button"
            role="tab"
            aria-selected={tab === candidate}
            className="inspector__tab"
            onClick={() => setTab(candidate)}
          >
            {candidate === 'detail' ? 'Detail' : candidate === 'thinking' ? 'Thinking' : 'Raw'}
          </button>
        ))}
      </div>

      <div className="inspector__panel" hidden={tab !== 'detail'}>
        <DetailPanel step={step} />
      </div>

      <div className="inspector__panel" hidden={tab !== 'thinking'}>
        {evidenceQuery.status === 'loading' && null}
        {evidenceQuery.status === 'error' && (
          <p className="inspector__unavailable">Could not load this step's evidence.</p>
        )}
        {evidenceQuery.status === 'loaded' && <ThinkingPanel thinking={evidenceQuery.evidence.thinking} />}
      </div>

      <div className="inspector__panel" hidden={tab !== 'raw'}>
        {evidenceQuery.status === 'loading' && null}
        {evidenceQuery.status === 'error' && (
          <p className="inspector__unavailable">Could not load this step's evidence.</p>
        )}
        {evidenceQuery.status === 'loaded' && (
          <RawPanel
            raw={evidenceQuery.evidence.raw}
            result={evidenceQuery.evidence.result}
            trigger={evidenceQuery.evidence.trigger}
            showTrigger={step.kind === 'hook'}
          />
        )}
      </div>
    </>
  )
}

/** FR-21 part 2 of 3 (S-52, issue #16): the inspector panel — Detail, Thinking and Raw tabs once a
 * step is selected; Scenario 4's own designed "nothing selected" state otherwise, never blank
 * panels. */
function Inspector({ sessionId, selectedStep }: { sessionId: string; selectedStep: SessionTapeStep | null }) {
  return (
    <section className="inspector" role="region" aria-label="Inspector">
      {selectedStep === null ? (
        <p className="inspector__empty">Pick a step on the tape to see its detail, reasoning and the raw event that produced it.</p>
      ) : (
        <SelectedStepInspector sessionId={sessionId} step={selectedStep} />
      )}
    </section>
  )
}

function LoadedSession({ sessionId }: { sessionId: string }) {
  const query = useSession(sessionId)
  const [selectedStep, setSelectedStep] = useState<SessionTapeStep | null>(null)
  // Mockup parity item #16: the one summary value lifted out of `Tape.tsx` (via its additive
  // `onViewportChange` prop) so `TapeMinimap` can stay scroll-synced — `scrollTop` itself stays
  // owned entirely inside `Tape.tsx`, per that file's own remarks on its `onViewportChange` prop.
  const [tapeViewport, setTapeViewport] = useState<MinimapViewport | null>(null)
  const handleTapeViewportChange = useCallback((firstVisibleIndex: number, visibleCount: number) => {
    setTapeViewport({ firstVisibleIndex, visibleCount })
  }, [])

  if (query.status === 'loading') {
    return null
  }

  if (query.status === 'error') {
    return (
      <div className="session-page__alert" role="alert">
        <p>Could not load this session. It may not exist, or the local API may be unreachable.</p>
      </div>
    )
  }

  const { envelope } = query

  return (
    <div className="session-page">
      <Masthead masthead={envelope.masthead} />
      {envelope.status.kind === 'complete' ? (
        <>
          <FindingChips chips={envelope.findings} />
          <AgentLanes lanes={envelope.lanes} />
          <div className="session-page__body">
            <div className="session-tape-group">
              <TapeMinimap steps={envelope.steps} viewport={tapeViewport} />
              <Tape steps={envelope.steps} onSelectStep={setSelectedStep} onViewportChange={handleTapeViewportChange} />
            </div>
            <Inspector sessionId={sessionId} selectedStep={selectedStep} />
          </div>
        </>
      ) : (
        <NonFinalState status={envelope.status} />
      )}
      <MethodologyFooter masthead={envelope.masthead} />
    </div>
  )
}

/** The Session Flight Recorder (FR-21: part 1 of 3 — S-08 — part 2 of 3 — S-52 — and part 3 of 3
 * — S-53): the masthead, the finding chips, the virtualised and keyboard-navigable tape and the
 * inspector, or one of two non-final states in place of the tape and chips (`NonFinalState`).
 * `sessionId` comes from the route (`/sessions/:sessionId`). The bare `/sessions` route was
 * removed as a dead end — the digest now provides real, clickable entry points to sessions. */
export function SessionPage() {
  const { sessionId } = useParams<{ sessionId: string }>()

  // `/sessions/:sessionId` is now the only route that mounts this page, so the param is always
  // present at runtime — but `useParams` still types every param optional, and there is no way to
  // tell React Router otherwise. This guard is the type narrowing, not a designed UI state: it is
  // unreachable through the router as registered, and says so rather than asserting non-null with
  // `!`, so a future route registration that forgets the param fails visibly instead of rendering
  // a session view against `undefined`.
  if (!sessionId) {
    return <p role="alert">This page needs a session id in its URL.</p>
  }

  return <LoadedSession sessionId={sessionId} />
}
