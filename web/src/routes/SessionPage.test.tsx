import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionEnvelope, StepEvidenceEnvelope } from '../api/session'
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
      findings: [],
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
      findings: [],
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
      findings: [],
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

const ONE_STEP_ENVELOPE: SessionEnvelope = {
  masthead: {
    sessionId: 'session-1',
    repository: null,
    branch: null,
    copilotVersion: '0.0.339',
    elapsedMs: null,
    turnCount: 0,
    toolCallCount: 1,
    subagentCount: 0,
    skillCount: 0,
    modelCount: null,
    contextSize: { kind: 'notRecorded' },
  },
  steps: [
    { kind: 'toolCall', stepId: 'tc1', label: 'view', timestamp: '2026-08-16T10:00:05Z', offsetMs: 5_000, ownerKind: 'main', agentId: null },
  ],
  findings: [
    {
      finding: {
        kind: 'general',
        class: 'waste',
        provenance: 'derived',
        evidence: [],
        recurrence: { key: 'path:/repo/a.cs', occurrences: [{ sessionId: 'session-1', ruleSetVersion: null }] },
        suggestion: { state: 'absent' },
        operatorResponse: 'ignored',
        sessionsAffected: 3,
      },
      sessionsAffected: 3,
    },
  ],
}

/** Sends a session/step-evidence-aware fetch mock: the session route resolves `sessionEnvelope`,
 * and any step-evidence route (`/steps/`) resolves `evidence`. */
function respondWithSessionAndEvidence(sessionEnvelope: SessionEnvelope, evidence: StepEvidenceEnvelope) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = url.includes('/steps/') ? evidence : sessionEnvelope
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    }),
  )
}

/** Scenario: the finding chips summarise the session (FR-21 part 2 of 3, S-52, issue #16). */
describe('The finding chips summarise the session', () => {
  it('states each finding affecting this session with its count', async () => {
    respondWithSessionAndEvidence(ONE_STEP_ENVELOPE, {
      thinking: { kind: 'unavailable', reason: 'Thinking is recorded per assistant message.' },
      raw: { kind: 'present', eventType: 'tool.execution_start', payload: '{}' },
    })
    renderAtSession('session-1')

    const chips = await screen.findByRole('list', { name: 'Findings' })
    expect(chips).toHaveTextContent('path:/repo/a.cs')
    expect(chips).toHaveTextContent('3')
  })
})

/** Scenario: nothing selected is a designed state. */
describe('Nothing selected is a designed state', () => {
  it('states that a step should be picked, rather than rendering blank panels', async () => {
    respondWithSessionAndEvidence(ONE_STEP_ENVELOPE, {
      thinking: { kind: 'unavailable', reason: 'irrelevant' },
      raw: { kind: 'present', eventType: 'tool.execution_start', payload: '{}' },
    })
    renderAtSession('session-1')

    const inspector = await screen.findByRole('region', { name: 'Inspector' })
    expect(inspector).toHaveTextContent(/pick a step/i)
  })
})

/** Scenario: selecting a step shows its evidence, and the inspector has three named tabs. */
describe('Selecting a step shows its evidence', () => {
  it('shows Detail, Thinking and Raw tabs, with Raw showing the event that produced the step', async () => {
    const user = userEvent.setup()
    respondWithSessionAndEvidence(ONE_STEP_ENVELOPE, {
      thinking: { kind: 'unavailable', reason: 'Thinking is recorded per assistant message; this step kind carries none of its own.' },
      raw: { kind: 'present', eventType: 'tool.execution_start', payload: '{"id":"e1","data":{"toolName":"view","toolCallId":"tc1"}}' },
    })
    renderAtSession('session-1')

    const step = await screen.findByRole('button', { name: /view/i })
    await user.click(step)

    expect(await screen.findByRole('tab', { name: 'Detail' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Thinking' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Raw' })).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Raw' }))

    expect(await screen.findByText(/toolCallId/)).toBeInTheDocument()
  })
})

/** Edge case: a step whose raw event was skipped at ingest shows that fact rather than an empty
 * panel — the Raw tab is "the provenance guarantee made clickable," never left blank. */
describe('A step whose raw event was skipped at ingest', () => {
  it('shows that fact in the Raw tab rather than an empty panel', async () => {
    const user = userEvent.setup()
    respondWithSessionAndEvidence(ONE_STEP_ENVELOPE, {
      thinking: { kind: 'unavailable', reason: 'No reasoning was recorded for this step.' },
      raw: { kind: 'skipped', reason: 'No raw event was found for this step; it may have been skipped at ingest.' },
    })
    renderAtSession('session-1')

    const step = await screen.findByRole('button', { name: /view/i })
    await user.click(step)
    await user.click(screen.getByRole('tab', { name: 'Raw' }))

    expect(await screen.findByText(/skipped at ingest/i)).toBeInTheDocument()
  })
})
