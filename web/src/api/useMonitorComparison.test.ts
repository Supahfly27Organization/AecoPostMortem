import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useMonitorComparison } from './useMonitorComparison'
import { MonitorComparisonRoute, type MonitorComparisonEnvelope } from './monitor'
import type { RuleSetVersionEnvelope } from './rulesInventory'

// One ascending timestamp per hash -- real ISO-8601 values, distinct enough that ordinal string
// comparison alone (no need for the FirstSessionId tie-break) settles every pair's order.
const startedAt: Record<string, string> = {
  v1: '2026-04-01T09:00:00Z',
  v2: '2026-04-15T09:00:00Z',
  v3: '2026-05-01T09:00:00Z',
  v4: '2026-05-15T09:00:00Z',
}

function version(hash: string, overrides: Partial<RuleSetVersionEnvelope> = {}): RuleSetVersionEnvelope {
  return {
    repository: 'supahfly27/UpFront',
    hash,
    firstSessionId: `${hash}-first`,
    lastSessionId: `${hash}-last`,
    firstSessionStartedAt: startedAt[hash],
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
  // is computed locally against a real re-sort of the list, so this never reaches the network.
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

  // Code review (round 2): the earlier version of this check trusted the caller's own array order
  // rather than sorting by `firstSessionStartedAt` -- a real gap, since nothing on the TypeScript
  // side enforced that `availableVersions` always arrives pre-sorted. This test hands the hook the
  // same four versions in a deliberately scrambled array order (not the chronological order
  // `startedAt` implies) and confirms adjacency is still judged correctly by real timestamp, not by
  // array position: v2/v3 are still adjacent, v1/v3 are still not, even though v3 sits *before* v2
  // in this array.
  it('judges adjacency by real chronological order, not by the array order it is handed', async () => {
    const scrambled = [version('v3'), version('v1'), version('v4'), version('v2')]

    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify(referenceComparison), { status: 200 })),
    )
    const { result: adjacent } = renderHook(() => useMonitorComparison(scrambled, 'v2', 'v3'))
    await waitFor(() => expect(adjacent.current.status).toBe('loaded'))

    const fetchSpy = vi.fn()
    vi.stubGlobal('fetch', fetchSpy)
    const { result: nonAdjacent } = renderHook(() => useMonitorComparison(scrambled, 'v1', 'v3'))
    await waitFor(() => expect(nonAdjacent.current.status).toBe('notAdjacent'))
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  // A 404 for a pair this hook already confirmed adjacent can only be GetMonitorComparison's other
  // refusal reachable through this UI (no comparable PreferAOverB statement in the after version) --
  // never the adjacency exception, since the hook never sends a request it believes is non-adjacent.
  // (The endpoint's third 404 cause, no repository resolved at all, is ruled out by `MonitorPage.tsx`
  // itself before this hook is ever reached with a real selection -- see that file's own doc comment.)
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

  // A non-404 HTTP failure (e.g. the server itself erroring) is the other side of the `/status 404/`
  // discriminator `useMonitorComparison.ts` uses to tell a real refusal apart from every other
  // failure -- it must not be mistaken for "no comparable rule" just because it's a 4xx.
  it('reports a 500 response as a plain error, not the no-comparable-rule refusal', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 500 })))

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

  it('re-fetches for the new pair when the selected pair changes', async () => {
    const urls: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        urls.push(typeof input === 'string' ? input : input.toString())
        return new Response(JSON.stringify(referenceComparison), { status: 200 })
      }),
    )

    const { result, rerender } = renderHook(
      ({ before, after }: { before: string; after: string }) => useMonitorComparison(versions, before, after),
      { initialProps: { before: 'v1', after: 'v2' } },
    )

    await waitFor(() => expect(result.current.status).toBe('loaded'))
    expect(urls).toHaveLength(1)
    expect(urls[0]).toContain('before=v1')
    expect(urls[0]).toContain('after=v2')

    rerender({ before: 'v2', after: 'v3' })

    await waitFor(() => expect(urls).toHaveLength(2))
    expect(urls[1]).toContain('before=v2')
    expect(urls[1]).toContain('after=v3')
  })
})
