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
        { kind: 'prompt', stepId: 't1', label: 'Completed', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:00Z', offsetMs: 0, ownerKind: 'main', agentId: null },
        { kind: 'hook', stepId: 'h1', label: 'pre-commit', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:02Z', offsetMs: 2_000, ownerKind: 'main', agentId: null },
        { kind: 'skill', stepId: 'sk1', label: 'code-review', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:03Z', offsetMs: 3_000, ownerKind: 'main', agentId: null },
        { kind: 'mcpCall', stepId: 'tc2', label: 'search_graph', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:04Z', offsetMs: 4_000, ownerKind: 'main', agentId: null },
        { kind: 'toolCall', stepId: 'tc1', label: 'view', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:05Z', offsetMs: 5_000, ownerKind: 'main', agentId: null },
      ],
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
    })
  })

  it('renders the masthead and states that no steps were recorded', async () => {
    renderAtSession('session-empty')

    const masthead = await screen.findByRole('region', { name: 'Masthead' })
    expect(masthead).toHaveTextContent('session-empty')

    expect(await screen.findByText(/no steps were recorded/i)).toBeInTheDocument()
  })
})

/** S-12, Scenario 1 (FR-25, issue #21): a skill invocation appears as its own step carrying its
 * name, plugin and plugin version. */
describe('A skill invocation is its own step', () => {
  beforeEach(() => {
    respondWith({
      masthead: {
        sessionId: 'session-1',
        repository: null,
        branch: null,
        copilotVersion: '0.0.339',
        elapsedMs: null,
        turnCount: 0,
        toolCallCount: 0,
        subagentCount: 0,
        skillCount: 1,
        modelCount: null,
        contextSize: { kind: 'notRecorded' },
      },
      steps: [
        {
          kind: 'skill',
          stepId: 'sk1',
          label: 'code-review',
          pluginName: 'superpowers',
          pluginVersion: '6.3.0',
          timestamp: '2026-08-16T10:00:03Z',
          offsetMs: 3_000,
          ownerKind: 'main',
          agentId: null,
        },
      ],
    })
  })

  it('renders the skill name, plugin and plugin version', async () => {
    renderAtSession('session-1')

    const rows = await screen.findAllByRole('listitem')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveTextContent('code-review')
    expect(rows[0]).toHaveTextContent('superpowers')
    expect(rows[0]).toHaveTextContent('6.3.0')
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
