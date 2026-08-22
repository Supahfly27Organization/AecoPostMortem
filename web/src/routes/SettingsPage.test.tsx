import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SettingsPage } from './SettingsPage'
import { IngestRoute, RebuildRoute, SettingsRoute, type SettingsEnvelope } from '../api/settings'
import { StoreChangedEventName } from '../api/storeChangeEvents'

const baseSettings: SettingsEnvelope = {
  storePath: 'C:\\Users\\operator\\AppData\\Local\\AecoPostMortem\\store.db',
  storeExists: true,
  storeSizeBytes: 248_815_616,
  copilotSourceRoot: 'C:\\Users\\operator\\.copilot\\session-state',
  copilotSourceFound: true,
  excludedRoots: ['F:\\git\\AecoPostMortem'],
}

function stubFetch(handlers: {
  settings?: () => Response | Promise<Response>
  ingest?: () => Response | Promise<Response>
  rebuild?: () => Response | Promise<Response>
}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const method = init?.method ?? 'GET'

      if (method === 'POST' && url.includes(IngestRoute) && handlers.ingest) {
        return handlers.ingest()
      }

      if (method === 'POST' && url.includes(RebuildRoute) && handlers.rebuild) {
        return handlers.rebuild()
      }

      if (url.includes(SettingsRoute) && handlers.settings) {
        return handlers.settings()
      }

      throw new Error(`Unexpected fetch: ${method} ${url}`)
    }),
  )
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Part A: the read-only configuration', () => {
  it('serves the store path, size, Copilot source and exclusions as real facts', async () => {
    stubFetch({ settings: () => jsonResponse(baseSettings) })
    render(<SettingsPage />)

    const config = await screen.findByRole('group', { name: 'Configuration' })
    expect(config).toHaveTextContent(baseSettings.storePath)
    expect(config).toHaveTextContent('Exists')
    expect(config).toHaveTextContent(baseSettings.copilotSourceRoot)
    expect(config).toHaveTextContent('Found')
    expect(config).toHaveTextContent('F:\\git\\AecoPostMortem')
  })

  it('states a store that does not exist yet honestly, never a guessed zero size', async () => {
    stubFetch({
      settings: () =>
        jsonResponse({ ...baseSettings, storeExists: false, storeSizeBytes: 0 }),
    })
    render(<SettingsPage />)

    const config = await screen.findByRole('group', { name: 'Configuration' })
    expect(config).toHaveTextContent('Does not exist yet')
    expect(config).not.toHaveTextContent('0 bytes')
  })

  it('states plainly when no roots are configured for exclusion', async () => {
    stubFetch({ settings: () => jsonResponse({ ...baseSettings, excludedRoots: [] }) })
    render(<SettingsPage />)

    expect(await screen.findByText('No roots are configured for exclusion.')).toBeInTheDocument()
  })

  it('reports an unreachable API distinctly', async () => {
    stubFetch({ settings: () => new Response('', { status: 500 }) })
    render(<SettingsPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not reach the local API')
  })
})

describe('Part B: ingest and rebuild as buttons', () => {
  it('states that ingest is running, disables both buttons, and shows the real coverage report on success', async () => {
    const user = userEvent.setup()
    let resolveIngest: (response: Response) => void = () => {}
    stubFetch({
      settings: () => jsonResponse(baseSettings),
      ingest: () => new Promise<Response>((resolve) => (resolveIngest = resolve)),
    })
    render(<SettingsPage />)

    const ingestButton = await screen.findByRole('button', { name: 'Run ingest' })
    const rebuildButton = screen.getByRole('button', { name: 'Run rebuild' })

    await user.click(ingestButton)

    expect(await screen.findByText('Running ingest…')).toBeInTheDocument()
    expect(ingestButton).toBeDisabled()
    expect(rebuildButton).toBeDisabled()

    resolveIngest(
      jsonResponse({
        sessionsFound: 35,
        sessionsIngested: 0,
        sessionsExcluded: [],
        linesParsed: 56138,
        linesSkipped: 0,
        eventsByType: {},
        durationSeconds: 16.0,
      }),
    )

    expect(await screen.findByText(/Found 35 sessions, ingested 0, excluded 0/)).toBeInTheDocument()
    await waitFor(() => expect(ingestButton).not.toBeDisabled())
    expect(rebuildButton).not.toBeDisabled()
  })

  it('shows the real rebuild summary on success', async () => {
    const user = userEvent.setup()
    stubFetch({
      settings: () => jsonResponse(baseSettings),
      rebuild: () =>
        jsonResponse({ rawEventCount: 56138, sessionCount: 35, durationSeconds: 6.8 }),
    })
    render(<SettingsPage />)

    await user.click(await screen.findByRole('button', { name: 'Run rebuild' }))

    expect(
      await screen.findByText(/Rebuilt from 56138 RAW events across 35 sessions/),
    ).toBeInTheDocument()
  })

  it('shows what failed, not a swallowed or generic error, when ingest fails', async () => {
    const user = userEvent.setup()
    stubFetch({
      settings: () => jsonResponse(baseSettings),
      ingest: () =>
        jsonResponse({ detail: 'The Copilot session-state root could not be read.' }, 500),
    })
    render(<SettingsPage />)

    await user.click(await screen.findByRole('button', { name: 'Run ingest' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The Copilot session-state root could not be read.',
    )
  })

  it('shows a distinct message when the server refuses a concurrent write with 409', async () => {
    const user = userEvent.setup()
    stubFetch({
      settings: () => jsonResponse(baseSettings),
      rebuild: () => new Response('', { status: 409 }),
    })
    render(<SettingsPage />)

    await user.click(await screen.findByRole('button', { name: 'Run rebuild' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('already running')
  })

  it('dispatches the store-changed event after a successful write, so the global app-state banner can refresh', async () => {
    const user = userEvent.setup()
    stubFetch({
      settings: () => jsonResponse(baseSettings),
      rebuild: () => jsonResponse({ rawEventCount: 1, sessionCount: 1, durationSeconds: 0.1 }),
    })

    const listener = vi.fn()
    window.addEventListener(StoreChangedEventName, listener)

    render(<SettingsPage />)
    await user.click(await screen.findByRole('button', { name: 'Run rebuild' }))

    await waitFor(() => expect(listener).toHaveBeenCalledTimes(1))
    window.removeEventListener(StoreChangedEventName, listener)
  })
})
