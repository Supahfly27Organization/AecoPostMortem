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
  comparisonResult?: unknown
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
        const body =
          handlers.comparisonResult ?? { kind: 'comparison', comparison: referenceComparison }
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

  // This test used to assert the opposite of what it asserts now, deliberately: the page once
  // refused a non-adjacent pair *without any network request*, because the hook re-implemented the
  // server's adjacency rule client-side to avoid an ambiguous 404. That duplication is gone, so the
  // request is made and the server states the reason. The trade is one request per non-adjacent
  // selection in exchange for a rule that exists in exactly one place -- and this page still never
  // blanks while it is in flight.
  it('renders the served non-adjacent refusal, naming what lies between, without blanking the page', async () => {
    stubFetch({
      comparisonResult: { kind: 'notAdjacent', intervening: [version('v2'), version('v3')] },
    })

    render(<MonitorPage />)

    const refusal = await screen.findByText(/not adjacent/i)
    expect(refusal).toBeInTheDocument()
    expect(refusal).toHaveTextContent(/2 other rule-set versions were in force between them/i)
    expect(screen.getByRole('combobox', { name: 'Before' })).toBeInTheDocument()
  })

  // Selecting a different version must re-request that pair. The old non-adjacent test covered this
  // incidentally (it selected v1 and asserted no request followed); now that the server owns the
  // adjacency rule, the interaction needs its own test or nothing would exercise the picker at all.
  it('re-requests the comparison for a newly selected version', async () => {
    stubFetch({})

    render(<MonitorPage />)
    await screen.findByText('41.8%')

    const requestedBefore = vi
      .mocked(fetch)
      .mock.calls.map(([input]) => (typeof input === 'string' ? input : input.toString()))
    expect(requestedBefore.some((url) => url.includes('before=v3'))).toBe(true)

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Before' }), 'v1')

    await vi.waitFor(() => {
      const urls = vi
        .mocked(fetch)
        .mock.calls.map(([input]) => (typeof input === 'string' ? input : input.toString()))
      expect(urls.some((url) => url.includes(MonitorComparisonRoute) && url.includes('before=v1'))).toBe(
        true,
      )
    })
  })

  it('states the no-comparable-rule refusal distinctly from the non-adjacent one', async () => {
    stubFetch({ comparisonResult: { kind: 'noComparableRule' } })

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

  // A store where no session resolves a repository at all is a real scope GetRulesInventory happily
  // serves (`repository: null`). This page used to have to detect it itself and refuse *before*
  // reaching the hook, purely so the endpoint's bodyless 404 could be labelled unambiguously. It is
  // now just another served reason, stated in the same place as the other two -- so the page asks
  // like it does for any other pair, and the pickers stay on screen.
  it('states the served no-repository reason distinctly from the other refusals', async () => {
    const noRepoVersion = { ...version('v1'), repository: null }
    stubFetch({
      inventory: inventoryWith({
        selectedVersion: noRepoVersion,
        availableVersions: [noRepoVersion, { ...version('v2'), repository: null }],
      }),
      comparisonResult: { kind: 'noRepository' },
    })

    render(<MonitorPage />)

    expect(await screen.findByText(/no repository is recorded/i)).toBeInTheDocument()
    expect(screen.queryByText(/no comparable rule/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/not adjacent/i)).not.toBeInTheDocument()
  })

  it('renders its own message when the API cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })))

    render(<MonitorPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the local api/i)
  })
})
