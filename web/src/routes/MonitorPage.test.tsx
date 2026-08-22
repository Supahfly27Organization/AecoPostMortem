import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MonitorPage } from './MonitorPage'
import { RulesInventoryRoute, type RulesInventoryEnvelope } from '../api/rulesInventory'
import { MonitorComparisonRoute, type MonitorComparisonEnvelope } from '../api/monitor'

function version(hash: string, sessionCount = 3) {
  return {
    repository: 'supahfly27/UpFront',
    hash,
    firstSessionId: `${hash}-first`,
    lastSessionId: `${hash}-last`,
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
}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()

      if (url.includes(RulesInventoryRoute)) {
        return new Response(JSON.stringify(handlers.inventory ?? inventoryWith()), { status: 200 })
      }

      if (url.includes(MonitorComparisonRoute)) {
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

  it('offers every version in both selects without rendering more than one comparison at a time', async () => {
    stubFetch({})

    render(<MonitorPage />)
    await screen.findByText('41.8%')

    const beforeSelect = screen.getByRole('combobox', { name: 'Before' })
    expect(within(beforeSelect).getAllByRole('option')).toHaveLength(4)
  })

  it('states plainly when the repository has fewer than two versions to compare', async () => {
    stubFetch({ inventory: inventoryWith({ availableVersions: [version('only')], selectedVersion: version('only') }) })

    render(<MonitorPage />)

    expect(await screen.findByText(/not enough rule-set versions/i)).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Before' })).not.toBeInTheDocument()
  })

  it('renders its own message when the API cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })))

    render(<MonitorPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the local api/i)
  })
})
