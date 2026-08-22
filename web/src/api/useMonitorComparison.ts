import { useEffect, useState } from 'react'
import { fetchMonitorComparison, type MonitorComparisonEnvelope } from './monitor'
import type { RuleSetVersionEnvelope } from './rulesInventory'

export type MonitorComparisonQuery =
  | { status: 'loading' }
  | { status: 'notAdjacent' }
  | { status: 'noComparableRule' }
  | { status: 'error' }
  | { status: 'loaded'; comparison: MonitorComparisonEnvelope }

/** Adjacent means "immediately next to each other in `availableVersions`' own chronological
 * order" -- the identical ordering `Rules.RuleSetVersionAdjacency.RequireAdjacentPair` itself
 * sorts by (`RuleSetVersioning.Compute`'s `(Repository, FirstSessionStartedAt)` order, the PR
 * #108 fix `AecoPostMortem.Rules/CLAUDE.md` documents). Computed here from the array's own order,
 * never trusted from a caller, so a pair this check calls adjacent is exactly the pair the server
 * would also call adjacent. */
function isAdjacent(
  availableVersions: readonly RuleSetVersionEnvelope[],
  beforeHash: string,
  afterHash: string,
): boolean {
  const beforeIndex = availableVersions.findIndex((version) => version.hash === beforeHash)
  const afterIndex = availableVersions.findIndex((version) => version.hash === afterHash)
  return beforeIndex >= 0 && afterIndex === beforeIndex + 1
}

/**
 * FR-39 (S-35, issue #43): fetches one Monitor comparison for a `(before, after)` rule-set-version
 * pair, re-fetching whenever either hash changes -- the same "a new request, not a filter over
 * what is already loaded" shape `useRulesInventory` established for switching a single version.
 *
 * `GET /api/monitor-comparison` answers a bare 404 with no distinguishing body for two structurally
 * different refusals: a non-adjacent pair (`Rules.NonAdjacentRuleSetVersionsException`, caught by
 * `ApiHost.GetMonitorComparison`) and an *adjacent* pair whose `after` version carries no
 * `RuleShapeKind.PreferAOverB` statement to compare (`GetMonitorComparison`'s own early return) --
 * see `src/AecoPostMortem.Api/CLAUDE.md`'s `GetMonitorComparison` remarks. This hook resolves the
 * ambiguity on the client rather than guessing: it checks adjacency locally, against the identical
 * ordered `availableVersions` list the server itself sorts by, *before* ever calling
 * `fetchMonitorComparison`. A non-adjacent pair therefore never reaches the network at all
 * (`'notAdjacent'`) -- and a 404 for a pair this check already confirmed adjacent can only be the
 * second refusal (`'noComparableRule'`), since the request that would have produced the first kind
 * of 404 was never sent.
 *
 * `null` for either hash (no selection made yet, e.g. before the version list itself has loaded)
 * stays `'loading'` without firing a request -- the same "loading renders nothing rather than a
 * message that might not apply a moment later" discipline `useAppState`/`useDigest`/
 * `useRulesInventory` all follow.
 */
export function useMonitorComparison(
  availableVersions: readonly RuleSetVersionEnvelope[],
  beforeHash: string | null,
  afterHash: string | null,
): MonitorComparisonQuery {
  const [query, setQuery] = useState<MonitorComparisonQuery>({ status: 'loading' })

  useEffect(() => {
    if (beforeHash === null || afterHash === null) {
      setQuery({ status: 'loading' })
      return
    }

    if (!isAdjacent(availableVersions, beforeHash, afterHash)) {
      setQuery({ status: 'notAdjacent' })
      return
    }

    const controller = new AbortController()
    setQuery({ status: 'loading' })

    // Guarded on both paths, not just the rejection -- the same reason `useRulesInventory` guards
    // both: a response settling after the selected pair changed must never overwrite the new
    // pair's `loading` with the previous pair's result.
    fetchMonitorComparison(beforeHash, afterHash, controller.signal)
      .then((comparison) => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'loaded', comparison })
        }
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return

        // `fetchMonitorComparison` throws a generic Error naming the status for any non-2xx
        // response; a 404 here is unambiguous, because a pair this hook believed non-adjacent
        // never reached `fetchMonitorComparison` at all.
        if (error instanceof Error && /status 404/.test(error.message)) {
          setQuery({ status: 'noComparableRule' })
        } else {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [availableVersions, beforeHash, afterHash])

  return query
}
