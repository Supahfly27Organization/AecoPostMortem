import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MonitorPage } from './MonitorPage'
import { RulesInventoryRoute, type RulesInventoryEnvelope } from '../api/rulesInventory'
import { MonitorComparisonRoute, type MonitorComparisonEnvelope } from '../api/monitor'

// One ascending timestamp per hash, the same real sort key `useMonitorComparison.ts`'s `isAdjacent`
// sorts by -- not merely relying on array order the way an earlier version of this fixture did.
const startedAt: Record<string, string> = {
  v1: '2026-04-01T09:00:00Z',
  v2: '2026-04-15T09:00:00Z',
  v3: '2026-05-01T09:00:00Z',
  v4: '2026-05-15T09:00:00Z',
  only: '2026-05-01T09:00:00Z',
}

function version(hash: string, sessionCount = 3) {
  return {
    repository: 'supahfly27/UpFront',
    hash,
    firstSessionId: `${hash}-first`,
    lastSessionId: `${hash}-last`,
    firstSessionStartedAt: startedAt[hash],
    sessionCount,
  }
}

const fourVersions = [version('v1'), version('v2'), version('v3'), version('v4')]

function inventoryWith(overrides: Partial<RulesInventoryEnvelope> = {}): RulesInventoryEnvelope {
  return {
    selectedVersion: fourVersions[fourVersions.length - 1],
    availableVersions: fourVersions,
    state: 'Listed',
    rows: [],
    statusCounts: { watched: 0, checkableNotYetBuilt: 0, notCheckable: 0, notARule: 0, total: 0 },
    ...overrides,
  }
}

const referenceComparison: MonitorComparisonEnvelope = {
  beforeVersion: version('v3'),
  afterVersion: version('v4'),
  before: {
    ruleVersion: { repository: 'supahfly27/UpFront', hash: 'v3' },
    adherent: { operandText: 'rg', layer: 'exactToolName', callCount: 23 },
    divergent: [{ operandText: 'grep', layer: 'exactToolName', callCount: 32 }],
    operands: [
      { operandText: 'rg', layer: 'exactToolName', callCount: 23 },
      { operandText: 'grep', layer: 'exactToolName', callCount: 32 },
    ],
    adherentCalls: 23,
    totalCalls: 55,
    percentage: 41.8,
  },
  after: {
    ruleVersion: { repository: 'supahfly27/UpFront', hash: 'v4' },
    adherent: { operandText: 'rg', layer: 'exactToolName', callCount: 76 },
    divergent: [{ operandText: 'grep', layer: 'exactToolName', callCount: 30 }],
    operands: [
      { operandText: 'rg', layer: 'exactToolName', callCount: 76 },
      { operandText: 'grep', layer: 'exactToolName', callCount: 30 },
    ],
    adherentCalls: 76,
    totalCalls: 106,
    percentage: 71.7,
  },
}

function stubFetch(handlers: {
  inventory?: RulesInventoryEnvelope
  comparisonStatus?: number
  comparisonBody?: MonitorComparisonEnvelope
  comparisonThrows?: boolean
}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()

      if (url.includes(RulesInventoryRoute)) {
        return new Response(JSON.stringify(handlers.inventory ?? inventoryWith()), { status: 200 })
      }

      if (url.includes(MonitorComparisonRoute)) {
        if (handlers.comparisonThrows) {
          throw new TypeError('Failed to fetch')
        }
        const status = handlers.comparisonStatus ?? 200
        const body = handlers.comparisonBody ?? referenceComparison
        return new Response(status === 200 ? JSON.stringify(body) : '', { status })
      }

      throw new Error(`Unexpected fetch: ${url}`)
    }),
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('MonitorPage (FR-39, S-35, issue #43)', () => {
  it('defaults to the two most recent versions and renders a real comparison', async () => {
    stubFetch({})

    render(<MonitorPage />)

    expect(await screen.findByText('41.8%')).toBeInTheDocument()
    expect(await screen.findByText('71.7%')).toBeInTheDocument()

    const beforeSelect = screen.getByRole('combobox', { name: 'Before' })
    const afterSelect = screen.getByRole('combobox', { name: 'After' })
    expect(beforeSelect).toHaveValue('v3')
    expect(afterSelect).toHaveValue('v4')
  })

  it('refuses a non-adjacent pair without a network request for the comparison, and never blanks the page', async () => {
    stubFetch({})

    render(<MonitorPage />)
    // Wait for the initial adjacent-pair comparison to finish loading first, so the count captured
    // below reflects only requests made *after* this point -- otherwise the initial (v3, v4) fetch,
    // still in flight, could race with the count and read as a spurious call caused by selecting v1.
    await screen.findByText('41.8%')

    const fetchMock = vi.mocked(fetch)
    const callsBefore = fetchMock.mock.calls.length

    const beforeSelect = screen.getByRole('combobox', { name: 'Before' })
    await userEvent.selectOptions(beforeSelect, 'v1')

    expect(await screen.findByText(/not adjacent/i)).toBeInTheDocument()

    const comparisonCalls = fetchMock.mock.calls
      .slice(callsBefore)
      .filter(([input]) => (typeof input === 'string' ? input : input.toString()).includes(MonitorComparisonRoute))
    expect(comparisonCalls).toHaveLength(0)
  })

  it('states the no-comparable-rule refusal distinctly for an adjacent pair the server still 404s', async () => {
    stubFetch({ comparisonStatus: 404 })

    render(<MonitorPage />)

    expect(await screen.findByText(/no comparable rule/i)).toBeInTheDocument()
    expect(screen.queryByText(/not adjacent/i)).not.toBeInTheDocument()
  })

  // The comparison fetch itself failing (a genuinely unreachable API mid-comparison, distinct from
  // both the initial inventory load and either designed refusal) is its own state -- this needed a
  // dedicated fixture, since the "API cannot be reached" test below 404s the inventory fetch first
  // and never reaches this branch at all.
  it('states plainly when the comparison request itself fails, distinctly from either refusal', async () => {
    stubFetch({ comparisonThrows: true })

    render(<MonitorPage />)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/could not reach the local api/i)
    expect(screen.queryByText(/no comparable rule/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/not adjacent/i)).not.toBeInTheDocument()
  })

  it('offers every version, numbered, in both selects without rendering more than one comparison at a time', async () => {
    stubFetch({})

    render(<MonitorPage />)
    await screen.findByText('41.8%')

    const beforeSelect = screen.getByRole('combobox', { name: 'Before' })
    const afterSelect = screen.getByRole('combobox', { name: 'After' })
    expect(within(beforeSelect).getAllByRole('option')).toHaveLength(4)
    expect(within(afterSelect).getAllByRole('option')).toHaveLength(4)

    // Numbered by chronological position, and the last option names itself the most recent --
    // the adjacency hint code review (round 2) asked for, since hashes alone give no way to tell
    // which pairs are next to each other.
    const options = within(afterSelect).getAllByRole('option')
    expect(options[0]).toHaveTextContent('1. v1')
    expect(options.at(-1)).toHaveTextContent('4. v4')
    expect(options.at(-1)).toHaveTextContent(/most recent/i)
  })

  it('states plainly when the repository has fewer than two versions to compare', async () => {
    stubFetch({ inventory: inventoryWith({ availableVersions: [version('only')], selectedVersion: version('only') }) })

    render(<MonitorPage />)

    expect(await screen.findByText(/not enough rule-set versions/i)).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Before' })).not.toBeInTheDocument()
  })

  // ApiHost.GetMonitorComparison refuses unconditionally, before checking adjacency or any rule,
  // when the whole store resolves no repository at all -- a real scope GetRulesInventory happily
  // serves (`repository: null`). Reaching this page's picker in that scope would otherwise produce a
  // false "no comparable rule" explanation for every pair, since that refusal reason is genuinely
  // unrelated to adjacency or the rule shape (code review, round 2).
  it('states plainly when no repository is recorded, rather than attempting a comparison', async () => {
    const noRepoVersion = { ...version('v1'), repository: null }
    stubFetch({
      inventory: inventoryWith({
        selectedVersion: noRepoVersion,
        availableVersions: [noRepoVersion, { ...version('v2'), repository: null }],
      }),
    })

    render(<MonitorPage />)

    expect(await screen.findByText(/no repository is recorded/i)).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Before' })).not.toBeInTheDocument()
    expect(screen.queryByText(/no comparable rule/i)).not.toBeInTheDocument()
  })

  it('renders its own message when the API cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })))

    render(<MonitorPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the local api/i)
  })
})
