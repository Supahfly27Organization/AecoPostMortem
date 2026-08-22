import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useMonitorComparison } from './useMonitorComparison'
import { MonitorComparisonRoute, type MonitorComparisonEnvelope } from './monitor'
import type { RuleSetVersionEnvelope } from './rulesInventory'

function version(hash: string, overrides: Partial<RuleSetVersionEnvelope> = {}): RuleSetVersionEnvelope {
  return {
    repository: 'supahfly27/UpFront',
    hash,
    firstSessionId: `${hash}-first`,
    lastSessionId: `${hash}-last`,
    sessionCount: 3,
    ...overrides,
  }
}

// Chronological order, matching Rules.RuleSetVersionAdjacency.RequireAdjacentPair's own ordering.
const versions = [version('v1'), version('v2'), version('v3'), version('v4')]

const referenceComparison: MonitorComparisonEnvelope = {
  beforeVersion: version('v2'),
  afterVersion: version('v3'),
  before: {
    ruleVersion: { repository: 'supahfly27/UpFront', hash: 'v2' },
    adherent: { operandText: 'rg', layer: 'exactToolName', callCount: 23 },
    divergent: [],
    operands: [{ operandText: 'rg', layer: 'exactToolName', callCount: 23 }],
    adherentCalls: 23,
    totalCalls: 23,
    percentage: 100,
  },
  after: {
    ruleVersion: { repository: 'supahfly27/UpFront', hash: 'v3' },
    adherent: { operandText: 'rg', layer: 'exactToolName', callCount: 76 },
    divergent: [],
    operands: [{ operandText: 'rg', layer: 'exactToolName', callCount: 76 }],
    adherentCalls: 76,
    totalCalls: 76,
    percentage: 100,
  },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useMonitorComparison', () => {
  it('loads a comparison for an adjacent pair', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()
        expect(url).toContain(MonitorComparisonRoute)
        expect(url).toContain('before=v2')
        expect(url).toContain('after=v3')
        return new Response(JSON.stringify(referenceComparison), { status: 200 })
      }),
    )

    const { result } = renderHook(() => useMonitorComparison(versions, 'v2', 'v3'))

    expect(result.current).toEqual({ status: 'loading' })
    await waitFor(() => expect(result.current.status).toBe('loaded'))
    expect(result.current).toEqual({ status: 'loaded', comparison: referenceComparison })
  })

  // The two versions are real entries in `versions`, but two apart -- v1 and v3 skip v2. The check
  // is computed locally against the same ordered list the server itself sorts by, so this never
  // reaches the network at all.
  it('refuses a non-adjacent pair without ever calling the server', async () => {
    const fetchSpy = vi.fn()
    vi.stubGlobal('fetch', fetchSpy)

    const { result } = renderHook(() => useMonitorComparison(versions, 'v1', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('notAdjacent'))
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  // Reversed order (after before before, chronologically) is not adjacent either -- the check
  // requires after's index to be exactly one past before's, not merely "one apart".
  it('refuses a reversed pair the same way', async () => {
    const fetchSpy = vi.fn()
    vi.stubGlobal('fetch', fetchSpy)

    const { result } = renderHook(() => useMonitorComparison(versions, 'v3', 'v2'))

    await waitFor(() => expect(result.current.status).toBe('notAdjacent'))
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  // A 404 for a pair this hook already confirmed adjacent can only be GetMonitorComparison's other
  // refusal (no comparable PreferAOverB statement in the after version) -- never the adjacency
  // exception, since the hook never sends a request it believes is non-adjacent.
  it('reports the no-comparable-rule refusal for an adjacent pair the server still 404s', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })))

    const { result } = renderHook(() => useMonitorComparison(versions, 'v2', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('noComparableRule'))
  })

  it('reports a network failure distinctly from either refusal', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new TypeError('Failed to fetch')
      }),
    )

    const { result } = renderHook(() => useMonitorComparison(versions, 'v2', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('error'))
  })

  it('stays loading until both hashes are chosen', () => {
    const fetchSpy = vi.fn()
    vi.stubGlobal('fetch', fetchSpy)

    const { result } = renderHook(() => useMonitorComparison(versions, null, null))

    expect(result.current).toEqual({ status: 'loading' })
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('re-fetches when the selected pair changes', async () => {
    let call = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        call += 1
        return new Response(JSON.stringify(referenceComparison), { status: 200 })
      }),
    )

    const { result, rerender } = renderHook(
      ({ before, after }: { before: string; after: string }) => useMonitorComparison(versions, before, after),
      { initialProps: { before: 'v1', after: 'v2' } },
    )

    await waitFor(() => expect(result.current.status).toBe('loaded'))
    expect(call).toBe(1)

    rerender({ before: 'v2', after: 'v3' })

    await waitFor(() => expect(call).toBe(2))
  })
})
