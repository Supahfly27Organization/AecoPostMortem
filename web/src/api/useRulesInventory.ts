import { useEffect, useState } from 'react'
import { fetchRulesInventory, type RulesInventoryEnvelope } from './rulesInventory'

export type RulesInventoryQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; inventory: RulesInventoryEnvelope }

/** Fetches FR-40's inventory for one rule-set version, re-fetching whenever `versionHash` changes —
 * the mechanism behind Scenario 6's "one version at a time": switching versions asks the server for
 * that version, it never merges a second version into a response already rendered.
 *
 * `null` requests the default version (the repository's most recent, in which nothing is retired).
 * Loading renders nothing rather than a message that might not apply a moment later, and a failed
 * fetch is its own explicit state — the same shape `useAppState` and `useDigest` established. */
export function useRulesInventory(versionHash: string | null): RulesInventoryQuery {
  const [query, setQuery] = useState<RulesInventoryQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery({ status: 'loading' })

    // Guarded on both paths, not just the rejection: a response that settles after the version
    // changed would otherwise overwrite the new request's `loading` with the previous version's
    // inventory — briefly showing one version's rows under another version's name, which is the one
    // thing Scenario 6 forbids.
    fetchRulesInventory(versionHash, controller.signal)
      .then((inventory) => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'loaded', inventory })
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [versionHash])

  return query
}
