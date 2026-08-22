import { useEffect, useState } from 'react'
import { fetchMonitorComparison, type MonitorComparisonEnvelope } from './monitor'
import type { RuleSetVersionEnvelope } from './rulesInventory'

export type MonitorComparisonQuery =
  | { status: 'loading' }
  | { status: 'notAdjacent'; intervening: RuleSetVersionEnvelope[] }
  | { status: 'noComparableRule' }
  | { status: 'noRepository' }
  | { status: 'error' }
  | { status: 'loaded'; comparison: MonitorComparisonEnvelope }

/**
 * FR-39 (S-35, issue #43): fetches one Monitor comparison for a `(before, after)` rule-set-version
 * pair, re-fetching whenever either hash changes -- the same "a new request, not a filter over what
 * is already loaded" shape `useRulesInventory` established for switching a single version.
 *
 * Every state this hook reports is now read straight off the served union
 * (`AecoPostMortem.Api.MonitorComparisonResultEnvelope`). It previously had to *derive* one of them:
 * `/api/monitor-comparison` answered a bare, bodyless 404 for three structurally different refusals,
 * so this hook re-implemented `Rules.RuleSetVersionAdjacency.RequireAdjacentPair`'s own
 * sort-and-index logic in TypeScript to rule out the non-adjacent case before calling, and
 * `MonitorPage.tsx` carried a second workaround (refusing to reach this hook at all when no
 * repository resolved) so the remaining 404 could be labelled unambiguously. Both are gone: the
 * server states the reason, so nothing here re-derives a rule that lives on the other side.
 *
 * That also removed this hook's `availableVersions` parameter, and with it the array-identity
 * dependency an earlier code-review round had to work around -- there is no longer anything for the
 * effect to depend on but the two hashes.
 *
 * `null` for either hash (no selection made yet, e.g. before the version list itself has loaded)
 * stays `'loading'` without firing a request -- the same "loading renders nothing rather than a
 * message that might not apply a moment later" discipline `useAppState`/`useDigest`/
 * `useRulesInventory` all follow.
 */
export function useMonitorComparison(
  beforeHash: string | null,
  afterHash: string | null,
): MonitorComparisonQuery {
  const [query, setQuery] = useState<MonitorComparisonQuery>({ status: 'loading' })

  useEffect(() => {
    if (beforeHash === null || afterHash === null) {
      setQuery({ status: 'loading' })
      return
    }

    const controller = new AbortController()
    setQuery({ status: 'loading' })

    // Guarded on both paths, not just the rejection -- the same reason `useRulesInventory` guards
    // both: a response settling after the selected pair changed must never overwrite the new pair's
    // `loading` with the previous pair's result.
    fetchMonitorComparison(beforeHash, afterHash, controller.signal)
      .then((result) => {
        if (controller.signal.aborted) return

        switch (result.kind) {
          case 'comparison':
            setQuery({ status: 'loaded', comparison: result.comparison })
            break
          case 'notAdjacent':
            setQuery({ status: 'notAdjacent', intervening: result.intervening })
            break
          case 'noComparableRule':
            setQuery({ status: 'noComparableRule' })
            break
          case 'noRepository':
            setQuery({ status: 'noRepository' })
            break
        }
      })
      .catch((error: unknown) => {
        // Every refusal is a 200 now, so a thrown error is a genuine failure: an unreachable API, or
        // the one remaining 404 (a version hash no session ever carried), which this UI cannot
        // produce because it only offers hashes the inventory itself served.
        if (!controller.signal.aborted) {
          void error
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [beforeHash, afterHash])

  return query
}
