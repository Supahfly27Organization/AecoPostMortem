import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useMonitorComparison } from './useMonitorComparison'
import { MonitorComparisonRoute, type MonitorComparisonEnvelope } from './monitor'
import type { RuleSetVersionEnvelope } from './rulesInventory'

// This file used to spend most of its length on adjacency: the hook re-implemented
// `Rules.RuleSetVersionAdjacency.RequireAdjacentPair`'s sort in TypeScript, so it needed its own
// tests for chronological ordering, a scrambled input array and the tie-break. That logic is gone --
// the endpoint states which refusal applies (`MonitorComparisonResultEnvelope`) and the hook reads
// it. Adjacency is tested where it is now implemented once, server-side, in
// `RuleSetVersionAdjacencyTests` and `MonitorComparisonRouteTests`; what is left to test here is
// this hook's own job: turning each served arm into a state, and not letting a stale response win.

const startedAt: Record<string, string> = {
  v2: '2026-04-15T09:00:00Z',
  v3: '2026-05-01T09:00:00Z',
}

function version(hash: string, overrides: Partial<RuleSetVersionEnvelope> = {}): RuleSetVersionEnvelope {
  return {
    repository: 'supahfly27/UpFront',
    hash,
    firstSessionId: `${hash}-first`,
    lastSessionId: `${hash}-last`,
    firstSessionStartedAt: startedAt[hash] ?? '2026-04-01T09:00:00Z',
    sessionCount: 3,
    ...overrides,
  }
}

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

function respondWith(body: unknown, status = 200) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      expect(url).toContain(MonitorComparisonRoute)
      return new Response(JSON.stringify(body), { status })
    }),
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useMonitorComparison', () => {
  it('loads a comparison, requesting the selected pair', async () => {
    const requested: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        requested.push(typeof input === 'string' ? input : input.toString())
        return new Response(
          JSON.stringify({ kind: 'comparison', comparison: referenceComparison }),
          { status: 200 },
        )
      }),
    )

    const { result } = renderHook(() => useMonitorComparison('v2', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('loaded'))
    expect(requested[0]).toContain('before=v2')
    expect(requested[0]).toContain('after=v3')
  })

  // The three refusals the endpoint used to collapse into one bodyless 404. Each is served 200 with
  // its own `kind` now, so this hook reports it rather than inferring it.
  it('reports a non-adjacent pair, carrying what the server says lies between them', async () => {
    respondWith({ kind: 'notAdjacent', intervening: [version('v2')] })

    const { result } = renderHook(() => useMonitorComparison('v1', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('notAdjacent'))
    expect(result.current).toEqual({ status: 'notAdjacent', intervening: [version('v2')] })
  })

  it('reports an adjacent pair with no comparable rule', async () => {
    respondWith({ kind: 'noComparableRule' })

    const { result } = renderHook(() => useMonitorComparison('v2', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('noComparableRule'))
  })

  it('reports a store with no repository recorded anywhere', async () => {
    respondWith({ kind: 'noRepository' })

    const { result } = renderHook(() => useMonitorComparison('v2', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('noRepository'))
  })

  it('reports a genuine failure as an error, distinctly from every stated refusal', async () => {
    respondWith({}, 500)

    const { result } = renderHook(() => useMonitorComparison('v2', 'v3'))

    await waitFor(() => expect(result.current.status).toBe('error'))
  })

  it('stays loading, and fires no request, until both hashes are selected', async () => {
    const fetchSpy = vi.fn()
    vi.stubGlobal('fetch', fetchSpy)

    const { result } = renderHook(() => useMonitorComparison(null, 'v3'))

    expect(result.current.status).toBe('loading')
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('never lets a response that settles after the pair changed overwrite the newer state', async () => {
    // The stale-response guard: the first request resolves only after the hook has been re-rendered
    // with a different pair, so its result must be discarded rather than replacing the new one.
    let resolveFirst: (response: Response) => void = () => {}
    let call = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        call += 1
        if (call === 1) {
          return new Promise<Response>((resolve) => (resolveFirst = resolve))
        }
        return new Response(JSON.stringify({ kind: 'noComparableRule' }), { status: 200 })
      }),
    )

    const { result, rerender } = renderHook(
      ({ before, after }: { before: string; after: string }) => useMonitorComparison(before, after),
      { initialProps: { before: 'v1', after: 'v2' } },
    )

    rerender({ before: 'v2', after: 'v3' })
    await waitFor(() => expect(result.current.status).toBe('noComparableRule'))

    resolveFirst(
      new Response(JSON.stringify({ kind: 'comparison', comparison: referenceComparison }), {
        status: 200,
      }),
    )

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(result.current.status).toBe('noComparableRule')
  })
  // A superseded request rejects with AbortError once the newer selection aborts it; that rejection
  // must never surface as the error state, which is what an operator reads as "the API is down".
  // Written while chasing a real "Could not reach the local API" seen in a browser -- this path
  // turned out to be sound (the real cause was a pre-existing store-concurrency 500, see the PR),
  // but the guard it pins had no test of its own, since the sibling test above covers a late
  // *success* rather than a late rejection.
  it('never reports an aborted, superseded request as a failure', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        void input
        return await new Promise<Response>((resolve, reject) => {
          const timer = setTimeout(
            () => resolve(new Response(JSON.stringify({ kind: 'noComparableRule' }), { status: 200 })),
            30,
          )
          init?.signal?.addEventListener('abort', () => {
            clearTimeout(timer)
            reject(new DOMException('The operation was aborted.', 'AbortError'))
          })
        })
      }),
    )

    const { result, rerender } = renderHook(
      ({ before, after }: { before: string; after: string }) => useMonitorComparison(before, after),
      { initialProps: { before: 'v1', after: 'v4' } },
    )

    rerender({ before: 'v2', after: 'v3' })

    await waitFor(() => expect(result.current.status).not.toBe('loading'))
    expect(result.current.status).toBe('noComparableRule')
  })
})
