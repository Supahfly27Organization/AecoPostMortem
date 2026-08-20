import { useState } from 'react'
import { useParams } from 'react-router-dom'
import type { RawStepEventEnvelope, SessionEnvelope, SessionFindingChip, SessionTapeStep, ThinkingEnvelope } from '../api/session'
import { useSession } from '../api/useSession'
import { useStepEvidence } from '../api/useStepEvidence'
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

function formatElapsed(elapsedMs: number | null): string {
  if (elapsedMs === null) {
    return 'unknown'
  }

  const totalMinutes = Math.round(elapsedMs / 60_000)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60

  return hours > 0 ? `${hours}h ${minutes}min` : `${minutes} min`
}

function formatOffset(offsetMs: number): string {
  return `${(offsetMs / 1000).toFixed(1)}s`
}

function formatContextSize(contextSize: SessionEnvelope['masthead']['contextSize']): string {
  if (contextSize.kind === 'notRecorded') {
    return 'not recorded'
  }

  return `${numberFormat.format(contextSize.inputTokens)} in / ${numberFormat.format(contextSize.outputTokens)} out`
}

/** FR-21 Scenario 1: session identity, repository, branch, CLI version, elapsed time, turns, tool
 * calls, subagents, skills, models and context size at end — all in one place, above the tape. */
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

/** FR-21 Scenario 2 (wall-clock order with offsets) and Scenario 3 (a session with no steps
 * states that plainly). The server has already ordered `steps` — this renders that order rather
 * than re-deriving it. Each row is a button (FR-21 part 2 of 3, S-52): selecting any step is how
 * the inspector gets its evidence. */
function Tape({
  steps,
  selectedStepId,
  onSelect,
}: {
  steps: SessionTapeStep[]
  selectedStepId: string | null
  onSelect: (step: SessionTapeStep) => void
}) {
  if (steps.length === 0) {
    return <p className="session-tape__empty">No steps were recorded for this session.</p>
  }

  return (
    <ul className="session-tape" aria-label="Tape">
      {steps.map((step) => (
        <li key={step.stepId} className="session-tape__step">
          <button
            type="button"
            className="session-tape__step-button"
            aria-pressed={step.stepId === selectedStepId}
            onClick={() => onSelect(step)}
          >
            <span className="session-tape__offset">{formatOffset(step.offsetMs)}</span>
            <span className="session-tape__kind">{KIND_LABEL[step.kind]}</span>
            <span className="session-tape__label">{step.label}</span>
          </button>
        </li>
      ))}
    </ul>
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
    </dl>
  )
}

function ThinkingPanel({ thinking }: { thinking: ThinkingEnvelope }) {
  if (thinking.kind === 'unavailable') {
    return <p className="inspector__unavailable">{thinking.reason}</p>
  }

  return <p className="inspector__thinking-text">{thinking.text}</p>
}

/** The Raw tab: "the provenance guarantee made clickable, not a debugging affordance" (the
 * story's own edge case) — it must never render blank, and a step whose raw event was skipped at
 * ingest states that fact instead. */
function RawPanel({ raw }: { raw: RawStepEventEnvelope }) {
  if (raw.kind === 'skipped') {
    return <p className="inspector__unavailable">{raw.reason}</p>
  }

  return (
    <div className="inspector__raw">
      <p className="inspector__raw-event-type">{raw.eventType}</p>
      <pre className="inspector__raw-payload">{raw.payload}</pre>
    </div>
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
        {evidenceQuery.status === 'loaded' && <RawPanel raw={evidenceQuery.evidence.raw} />}
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
      <FindingChips chips={envelope.findings} />
      <div className="session-page__body">
        <Tape steps={envelope.steps} selectedStepId={selectedStep?.stepId ?? null} onSelect={setSelectedStep} />
        <Inspector sessionId={sessionId} selectedStep={selectedStep} />
      </div>
    </div>
  )
}

/** The Session Flight Recorder (FR-21, part 1 of 3 — S-08 — and part 2 of 3 — S-52): the masthead,
 * the finding chips, the time-ordered tape and the inspector. `sessionId` comes from the route
 * (`/sessions/:sessionId`); the bare `/sessions` route carries none, since picking a session from a
 * list is a later story's job (part 3, S-53). */
export function SessionPage() {
  const { sessionId } = useParams<{ sessionId: string }>()

  if (!sessionId) {
    return (
      <div className="session-page__no-selection">
        <h2>Session Flight Recorder</h2>
        <p>No session selected. Choose a session from the digest to open its Flight Recorder.</p>
      </div>
    )
  }

  return <LoadedSession sessionId={sessionId} />
}
