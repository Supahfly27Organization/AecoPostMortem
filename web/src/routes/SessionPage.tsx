import { useParams } from 'react-router-dom'
import type { SessionEnvelope, SessionTapeStep } from '../api/session'
import { useSession } from '../api/useSession'
import './SessionPage.css'

const numberFormat = new Intl.NumberFormat('en-US')

const KIND_LABEL: Record<SessionTapeStep['kind'], string> = {
  prompt: 'Prompt',
  hook: 'Hook',
  skill: 'Skill',
  toolCall: 'Tool call',
  mcpCall: 'MCP call',
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

/** FR-21 Scenario 2 (wall-clock order with offsets) and Scenario 3 (a session with no steps
 * states that plainly). The server has already ordered `steps` — this renders that order rather
 * than re-deriving it. */
function Tape({ steps }: { steps: SessionTapeStep[] }) {
  if (steps.length === 0) {
    return <p className="session-tape__empty">No steps were recorded for this session.</p>
  }

  return (
    <ul className="session-tape" aria-label="Tape">
      {steps.map((step) => (
        <li key={step.stepId} className="session-tape__step">
          <span className="session-tape__offset">{formatOffset(step.offsetMs)}</span>
          <span className="session-tape__kind">{KIND_LABEL[step.kind]}</span>
          <span className="session-tape__label">{step.label}</span>
        </li>
      ))}
    </ul>
  )
}

function LoadedSession({ sessionId }: { sessionId: string }) {
  const query = useSession(sessionId)

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
      <Tape steps={envelope.steps} />
    </div>
  )
}

/** The Session Flight Recorder (FR-21, part 1 of 3 — S-08): the masthead and the time-ordered
 * tape. `sessionId` comes from the route (`/sessions/:sessionId`); the bare `/sessions` route
 * carries none, since picking a session from a list is a later story's job (the digest's finding
 * chips, not yet built). */
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
