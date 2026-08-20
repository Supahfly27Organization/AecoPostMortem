import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { AppStateRoute, type AppStateReport } from './api/appState'
import { DigestRoute } from './api/digest'
import { RulesInventoryRoute, type RulesInventoryEnvelope } from './api/rulesInventory'

/** S-48, Scenario 1: "The three surfaces are routable." Every route resolves under a shared
 * shell. All three now have real content — the digest (S-36/S-54), the session view (S-08) and the
 * Rules Inventory (S-22). */
describe('App routing', () => {
  const ready: AppStateReport = { kind: 'ready', message: 'Ready.', fixCommand: null }

  const inventory: RulesInventoryEnvelope = {
    selectedVersion: {
      repository: 'supahfly27/UpFront',
      hash: 'hash-1',
      firstSessionId: 'session-1',
      lastSessionId: 'session-3',
      sessionCount: 3,
    },
    availableVersions: [
      {
        repository: 'supahfly27/UpFront',
        hash: 'hash-1',
        firstSessionId: 'session-1',
        lastSessionId: 'session-3',
        sessionCount: 3,
      },
    ],
    state: 'Listed',
    rows: [
      {
        sourceFile: 'CLAUDE.md',
        text: 'Prefer the index over a broad file search.',
        status: { status: 'watched', label: 'Watched' },
        sessionIds: ['session-1'],
        inForceFrom: '2026-05-01T09:00:00Z',
        inForceUntil: '2026-05-21T09:00:00Z',
        retirement: { state: 'inForce' },
        adherenceFrozenAt: null,
      },
    ],
    statusCounts: { watched: 1, checkableNotYetBuilt: 0, notCheckable: 0, notARule: 0, total: 1 },
  }

  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()

        if (url.includes(AppStateRoute)) {
          return new Response(JSON.stringify(ready), { status: 200 })
        }

        if (url.includes(RulesInventoryRoute)) {
          // No real /api/rules-inventory endpoint exists yet (web/src/api/rulesInventory.ts) —
          // routing only cares that RulesInventoryPage renders its own content.
          return new Response(JSON.stringify(inventory), { status: 200 })
        }

        if (url.includes(DigestRoute)) {
          // No real /api/digest endpoint exists yet (web/src/api/digest.ts) — routing only cares
          // that DigestPage renders its own content, not that a digest loads successfully here.
          return new Response('', { status: 404 })
        }

        throw new Error(`Unexpected fetch: ${url}`)
      }),
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reaches the Process Digest at the root route', () => {
    render(
      <MemoryRouter initialEntries={['/']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Process Digest' })).toBeInTheDocument()
  })

  it('reaches the session view with no session selected', () => {
    render(
      <MemoryRouter initialEntries={['/sessions']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Session Flight Recorder' })).toBeInTheDocument()
    expect(screen.getByText(/no session selected/i)).toBeInTheDocument()
  })

  it('reaches one session\'s Flight Recorder at /sessions/:sessionId (S-08)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()
        if (url.includes('/api/app-state')) {
          return new Response(JSON.stringify(ready), { status: 200 })
        }

        return new Response(
          JSON.stringify({
            masthead: {
              sessionId: 'session-1',
              repository: null,
              branch: null,
              copilotVersion: '0.0.339',
              elapsedMs: null,
              turnCount: 0,
              toolCallCount: 0,
              subagentCount: 0,
              skillCount: 0,
              modelCount: null,
              contextSize: { kind: 'notRecorded' },
            },
            steps: [],
            status: { kind: 'complete' },
            findings: [],
            lanes: [],
          }),
          { status: 200 },
        )
      }),
    )

    render(
      <MemoryRouter initialEntries={['/sessions/session-1']}>
        <App />
      </MemoryRouter>,
    )

    expect(await screen.findByRole('region', { name: 'Masthead' })).toHaveTextContent('session-1')
  })

  it('reaches the Rules Inventory, scoped to one named rule-set version (S-22)', async () => {
    render(
      <MemoryRouter initialEntries={['/rules']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Rules Inventory' })).toBeInTheDocument()
    expect(await screen.findByRole('region', { name: /rule-set version/i })).toHaveTextContent('hash-1')
  })

  it('exposes navigation to all three surfaces from every route', () => {
    render(
      <MemoryRouter initialEntries={['/rules']}>
        <App />
      </MemoryRouter>,
    )

    const nav = screen.getByRole('navigation', { name: 'Surfaces' })
    expect(nav).toHaveTextContent('Digest')
    expect(nav).toHaveTextContent('Session view')
    expect(nav).toHaveTextContent('Rules Inventory')
  })
})
