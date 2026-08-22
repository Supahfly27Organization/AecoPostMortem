import { useEffect, useState } from 'react'
import { fetchMonitorComparison, type MonitorComparisonEnvelope } from './monitor'
import type { RuleSetVersionEnvelope } from './rulesInventory'

export type MonitorComparisonQuery =
  | { status: 'loading' }
  | { status: 'notAdjacent' }
  | { status: 'noComparableRule' }
  | { status: 'error' }
  | { status: 'loaded'; comparison: MonitorComparisonEnvelope }

/** The identical sort key and comparer `Rules.RuleSetVersionAdjacency.RequireAdjacentPair` itself
 * uses server-side: `FirstSessionStartedAt` (ordinal string comparison -- ISO-8601 timestamps sort
 * correctly as plain strings), tied-broken by `FirstSessionId` for a total order regardless of
 * arrival order. A real sort, not a trust that `availableVersions` already arrived in this order --
 * code review (round 2) flagged that the earlier version of this check only ever compared array
 * *position*, which was only as reliable as the wire's own happened-to-be-sorted order, an
 * assumption nothing enforced on the TypeScript side. */
function compareVersions(a: RuleSetVersionEnvelope, b: RuleSetVersionEnvelope): number {
  if (a.firstSessionStartedAt !== b.firstSessionStartedAt) {
    return a.firstSessionStartedAt < b.firstSessionStartedAt ? -1 : 1
  }
  if (a.firstSessionId !== b.firstSessionId) {
    return a.firstSessionId < b.firstSessionId ? -1 : 1
  }
  return 0
}

/** Adjacent means "immediately next to each other in the repository's own chronological order" --
 * sorted here by `compareVersions`, never trusted from the caller's own array order, so a pair this
 * check calls adjacent is exactly the pair `Rules.RuleSetVersionAdjacency.RequireAdjacentPair`
 * would also call adjacent. `availableVersions` is already scoped to one repository (the same
 * default `RulesInventory.MostRecentVersion` resolves, `useRulesInventory(null)`'s own fetch), so
 * this needs no repository filter of its own the way the server's own check does for a caller that
 * could in principle name two different repositories. */
function isAdjacent(
  availableVersions: readonly RuleSetVersionEnvelope[],
  beforeHash: string,
  afterHash: string,
): boolean {
  const chronological = [...availableVersions].sort(compareVersions)
  const beforeIndex = chronological.findIndex((version) => version.hash === beforeHash)
  const afterIndex = chronological.findIndex((version) => version.hash === afterHash)
  return beforeIndex >= 0 && afterIndex === beforeIndex + 1
}

/**
 * FR-39 (S-35, issue #43): fetches one Monitor comparison for a `(before, after)` rule-set-version
 * pair, re-fetching whenever either hash changes -- the same "a new request, not a filter over
 * what is already loaded" shape `useRulesInventory` established for switching a single version.
 *
 * `GET /api/monitor-comparison` answers a bare 404 with no distinguishing body for two structurally
 * different refusals: a non-adjacent pair (`Rules.NonAdjacentRuleSetVersionsException`, caught by
 * `ApiHost.GetMonitorComparison`), and two further, distinct 404 causes this hook does not attempt
 * to distinguish because they are unreachable through this UI (see below) -- an *adjacent* pair
 * whose `after` version carries no `RuleShapeKind.PreferAOverB` statement to compare
 * (`GetMonitorComparison`'s own early return), and no repository resolved for the whole store at
 * all (`repositoryScope.SelectedRepository is null`) — see `src/AecoPostMortem.Api/CLAUDE.md`'s
 * `GetMonitorComparison` remarks. This hook resolves the ambiguity on the client rather than
 * guessing: it checks adjacency locally, against a real re-sort of `availableVersions` (`isAdjacent`
 * above), *before* ever calling `fetchMonitorComparison`. A non-adjacent pair therefore never
 * reaches the network at all (`'notAdjacent'`) -- and a 404 for a pair this check already confirmed
 * adjacent is labelled `'noComparableRule'`, which is only a sound label because `MonitorPage.tsx`
 * separately refuses to reach this hook at all when `inventory.selectedVersion.repository === null`
 * (code review, round 2) -- that third 404 cause is otherwise reachable and would mislabel itself
 * as "no comparable rule" here, a false explanation this hook cannot itself verify or rule out.
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

  // Computed fresh every render, not memoised: `isAdjacent` is a cheap scan/sort over a small
  // array, and reducing it to one boolean here is what lets the effect below depend on primitives
  // (`adjacent`/`beforeHash`/`afterHash`) instead of `availableVersions`' own array identity --
  // code review (round 1) flagged the prior array-identity dependency as an exported foot-gun: any
  // future caller passing an inline `[]` or a freshly `.filter(...)`ed array would re-trigger the
  // exact infinite-render loop a stable-reference workaround (`NoVersions`) once had to paper over
  // in `MonitorPage.tsx`. A boolean dependency makes that class of bug structurally impossible
  // rather than avoided by a convention a future call site could forget.
  const adjacent =
    beforeHash !== null && afterHash !== null && isAdjacent(availableVersions, beforeHash, afterHash)

  useEffect(() => {
    if (beforeHash === null || afterHash === null) {
      setQuery({ status: 'loading' })
      return
    }

    if (!adjacent) {
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
  }, [adjacent, beforeHash, afterHash])

  return query
}
