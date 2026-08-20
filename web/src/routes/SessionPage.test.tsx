import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionEnvelope } from '../api/session'
import { SessionPage } from './SessionPage'

function respondWith(envelope: SessionEnvelope) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      expect(url).toContain('/api/sessions/')
      return new Response(JSON.stringify(envelope), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    }),
  )
}

function renderAtSession(sessionId: string | null) {
  const path = sessionId === null ? '/sessions' : `/sessions/${sessionId}`
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/sessions" element={<SessionPage />} />
        <Route path="/sessions/:sessionId" element={<SessionPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

/** Scenario 1: the masthead states session identity, repository, branch, CLI version, elapsed
 * time, turns, tool calls, subagents, skills, models and context size at end. */
describe('The masthead states what this session was', () => {
  beforeEach(() => {
    respondWith({
      masthead: {
        sessionId: 'session-1',
        repository: 'Supahfly27Organization/AecoPostMortem',
        branch: 'main',
        copilotVersion: '0.0.339',
        elapsedMs: 30 * 60 * 1000,
        turnCount: 4,
        toolCallCount: 12,
        subagentCount: 2,
        skillCount: 3,
        modelCount: 2,
        contextSize: { kind: 'observed', inputTokens: 12_345, outputTokens: 6_789, cacheReadTokens: null, cacheWriteTokens: null, reasoningTokens: null, modelCount: 2 },
      },
      steps: [],
      status: { kind: 'complete' },
    })
  })

  it('shows identity, repository, branch, CLI version, elapsed time and the population counts', async () => {
    renderAtSession('session-1')

    const masthead = await screen.findByRole('region', { name: 'Masthead' })
    expect(masthead).toHaveTextContent('session-1')
    expect(masthead).toHaveTextContent('Supahfly27Organization/AecoPostMortem')
    expect(masthead).toHaveTextContent('main')
    expect(masthead).toHaveTextContent('0.0.339')
    expect(masthead).toHaveTextContent('30')
    expect(masthead).toHaveTextContent('4')
    expect(masthead).toHaveTextContent('12')
    expect(masthead).toHaveTextContent('2')
    expect(masthead).toHaveTextContent('3')
    expect(masthead).toHaveTextContent('12,345')
    expect(masthead).toHaveTextContent('6,789')
  })
})

/** Scenario 2: steps appear in wall-clock order with their offset from session start. */
describe('The tape is ordered by real time', () => {
  beforeEach(() => {
    respondWith({
      masthead: {
        sessionId: 'session-1',
        repository: null,
        branch: null,
        copilotVersion: '0.0.339',
        elapsedMs: null,
        turnCount: 1,
        toolCallCount: 2,
        subagentCount: 0,
        skillCount: 1,
        modelCount: null,
        contextSize: { kind: 'notRecorded' },
      },
      steps: [
        { kind: 'prompt', stepId: 't1', label: 'Completed', timestamp: '2026-08-16T10:00:00Z', offsetMs: 0, ownerKind: 'main', agentId: null },
        { kind: 'hook', stepId: 'h1', label: 'pre-commit', timestamp: '2026-08-16T10:00:02Z', offsetMs: 2_000, ownerKind: 'main', agentId: null },
        { kind: 'skill', stepId: 'sk1', label: 'code-review', timestamp: '2026-08-16T10:00:03Z', offsetMs: 3_000, ownerKind: 'main', agentId: null },
        { kind: 'mcpCall', stepId: 'tc2', label: 'search_graph', timestamp: '2026-08-16T10:00:04Z', offsetMs: 4_000, ownerKind: 'main', agentId: null },
        { kind: 'toolCall', stepId: 'tc1', label: 'view', timestamp: '2026-08-16T10:00:05Z', offsetMs: 5_000, ownerKind: 'main', agentId: null },
      ],
      status: { kind: 'complete' },
    })
  })

  it('renders every step kind in the order the server supplied, each with its offset', async () => {
    renderAtSession('session-1')

    const tape = await screen.findByRole('list', { name: 'Tape' })
    const rows = await screen.findAllByRole('listitem')
    expect(tape).toBeInTheDocument()

    const labels = rows.map((row) => row.textContent)
    expect(labels[0]).toContain('Completed')
    expect(labels[1]).toContain('pre-commit')
    expect(labels[2]).toContain('code-review')
    expect(labels[3]).toContain('search_graph')
    expect(labels[4]).toContain('view')

    // Offsets from session start are rendered on each step.
    expect(rows[4]).toHaveTextContent('5')
  })
})

/** Scenario 3: one of the measured 2 of 35 sessions that made no tool call still renders — the
 * masthead renders and the tape states that no steps were recorded. */
describe('A session with no tool calls still renders', () => {
  beforeEach(() => {
    respondWith({
      masthead: {
        sessionId: 'session-empty',
        repository: null,
        branch: null,
        copilotVersion: '0.0.339',
        elapsedMs: 60_000,
        turnCount: 0,
        toolCallCount: 0,
        subagentCount: 0,
        skillCount: 0,
        modelCount: null,
        contextSize: { kind: 'notRecorded' },
      },
      steps: [],
      status: { kind: 'complete' },
    })
  })

  it('renders the masthead and states that no steps were recorded', async () => {
    renderAtSession('session-empty')

    const masthead = await screen.findByRole('region', { name: 'Masthead' })
    expect(masthead).toHaveTextContent('session-empty')

    expect(await screen.findByText(/no steps were recorded/i)).toBeInTheDocument()
  })
})

describe('No session is selected', () => {
  it('states that plainly rather than showing a blank page', () => {
    renderAtSession(null)

    expect(screen.getByText(/no session selected/i)).toBeInTheDocument()
  })
})

describe('The session cannot be loaded', () => {
  it('reports that distinctly, rather than showing a blank page', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 404 })),
    )
    renderAtSession('no-such-session')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/could not load/i)
  })
})

/** FR-21, part 3 of 3 (S-53, issue #17), Scenario 3: a session whose ingest has not completed
 * states that plainly rather than rendering whatever partial tape has arrived so far as final. */
describe('A session still ingesting says so', () => {
  it('states that the session is incomplete rather than rendering a partial tape as final', async () => {
    respondWith({
      masthead: {
        sessionId: 'session-mid-ingest',
        repository: null,
        branch: null,
        copilotVersion: '0.0.339',
        elapsedMs: null,
        turnCount: 3,
        toolCallCount: 5,
        subagentCount: 0,
        skillCount: 0,
        modelCount: null,
        contextSize: { kind: 'notRecorded' },
      },
      steps: [
        { kind: 'prompt', stepId: 't1', label: 'Completed', timestamp: '2026-08-16T10:00:00Z', offsetMs: 0, ownerKind: 'main', agentId: null },
      ],
      status: { kind: 'ingestIncomplete' },
    })

    renderAtSession('session-mid-ingest')

    expect(await screen.findByText(/still ingesting|not (yet )?complete/i)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Tape' })).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

/** Scenario 4: a session whose reconstruction failed states why, and what was skipped — a
 * distinct message from both the generic load error and the incomplete-ingest state. */
describe('A session that failed to reconstruct says why', () => {
  it('states that reconstruction failed and names what was skipped', async () => {
    respondWith({
      masthead: {
        sessionId: 'session-broken',
        repository: null,
        branch: null,
        copilotVersion: '0.0.339',
        elapsedMs: 600_000,
        turnCount: 3,
        toolCallCount: 5,
        subagentCount: 1,
        skillCount: 0,
        modelCount: null,
        contextSize: { kind: 'notRecorded' },
      },
      steps: [],
      status: {
        kind: 'reconstructionFailed',
        skipped: ['2 of 5 subagent spawn(s) could not be resolved to their originating tool call'],
      },
    })

    renderAtSession('session-broken')

    expect(await screen.findByText(/reconstruction failed/i)).toBeInTheDocument()
    expect(await screen.findByText(/could not be resolved/i)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Tape' })).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
