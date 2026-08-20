import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { AppStateRoute, type AppStateReport } from './api/appState'

function respondWith(report: AppStateReport) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      expect(url).toContain(AppStateRoute)
      return new Response(JSON.stringify(report), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    }),
  )
}

function renderApp() {
  render(
    <MemoryRouter initialEntries={['/']}>
      <App />
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Before the first ingest, the app says so (Scenario 2)', () => {
  beforeEach(() => {
    respondWith({
      kind: 'emptyStore',
      message: 'Nothing has been ingested yet.',
      fixCommand: 'aecopostmortem ingest',
    })
  })

  it('states that nothing has been ingested and names the command that would fix it', async () => {
    renderApp()

    const banner = await screen.findByRole('status')
    expect(banner).toHaveTextContent('Nothing has been ingested yet.')
    expect(banner).toHaveTextContent('aecopostmortem ingest')
  })
})

describe('With no Copilot directory, the app says that instead (Scenario 3)', () => {
  beforeEach(() => {
    respondWith({
      kind: 'noSourceFound',
      message: 'No source was found: no Copilot session-state directory exists on this machine.',
      fixCommand: null,
    })
  })

  it('states that no source was found, distinctly from an empty store', async () => {
    renderApp()

    const banner = await screen.findByRole('status')
    expect(banner).toHaveTextContent('No source was found')
    expect(banner).not.toHaveTextContent('Nothing has been ingested yet.')
    expect(banner).not.toHaveTextContent('aecopostmortem ingest')
  })
})

describe('Once the app is ready, the diagnosis banner steps aside', () => {
  it('renders no banner once the store has been ingested and a source exists', async () => {
    respondWith({ kind: 'ready', message: 'Ready.', fixCommand: null })
    renderApp()

    await waitFor(() => {
      expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })
  })
})

describe('The API host is unreachable', () => {
  it('reports that distinctly, rather than silently showing nothing', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new TypeError('Failed to fetch')
      }),
    )
    renderApp()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('aecopostmortem serve')
  })
})
