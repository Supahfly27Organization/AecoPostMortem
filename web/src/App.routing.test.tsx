import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { AppStateRoute, type AppStateReport } from './api/appState'
import { DigestRoute } from './api/digest'
import { RulesInventoryRoute, type RulesInventoryEnvelope } from './api/rulesInventory'

/** S-48, Scenario 1: "The three surfaces are routable." Every route resolves under a shared
 * shell. The Digest (S-36/S-54) and Rules Inventory (S-22) are reachable from the nav; the session
 * view (S-08) is reachable only from the digest, via a finding's session id or chip. */
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
        violationCount: { kind: 'notAvailable' },
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

  it('states a dead end, with the navigation still present, for an unrouted URL such as the retired bare /sessions', () => {
    render(
      <MemoryRouter initialEntries={['/sessions']}>
        <App />
      </MemoryRouter>,
    )

    // The bare `/sessions` route was removed with the "Session view" nav link. An unmatched URL
    // must still say so — React Router matches no route at all for one, so without the catch-all
    // even `AppShell`'s own navigation is absent and the operator gets a blank page with no way back.
    expect(screen.getByRole('alert')).toHaveTextContent('There is no page at this address.')
    expect(screen.getByRole('link', { name: 'Go to the Process Digest' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Digest' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Rules Inventory' })).toBeInTheDocument()
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
              startedAt: '2026-08-16T10:00:00Z',
              endedAt: null,
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

  it('exposes navigation to the Digest and Rules Inventory from every route', () => {
    render(
      <MemoryRouter initialEntries={['/rules']}>
        <App />
      </MemoryRouter>,
    )

    const nav = screen.getByRole('navigation', { name: 'Surfaces' })
    expect(nav).toHaveTextContent('Digest')
    expect(nav).toHaveTextContent('Rules Inventory')
    expect(nav).not.toHaveTextContent('Session view')
  })
})
