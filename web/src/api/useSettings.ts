import { useEffect, useState } from 'react'
import { fetchSettings, type SettingsEnvelope } from './settings'

export type SettingsQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; settings: SettingsEnvelope; isRefetching: boolean }

/** Fetches the Settings surface's read-only configuration (Part A), re-fetching whenever
 * `refetchToken` changes — `SettingsPage` bumps it after a successful ingest or rebuild, so the
 * store-existence/size figures shown reflect what the write just did rather than what was true when
 * the page first loaded. A failed fetch is its own explicit state, the same shape
 * `useAppState`/`useRulesInventory` already establish.
 *
 * `isRefetching` mirrors `useDigest`'s own convention (code review, Minor): unlike
 * `useRulesInventory` — where a version switch is a genuinely new request whose old rows would be
 * actively wrong to keep showing — a post-write refresh here is a background update of facts that
 * mostly did not change (the store path, the Copilot source root); blanking the whole configuration
 * block to bare `'loading'` on every refetch would make the page flicker for no reason a write's own
 * real feedback (the write card's own "Running …"/result text) doesn't already cover. The very first
 * fetch (nothing loaded yet) still reports bare `'loading'`. */
export function useSettings(refetchToken: number): SettingsQuery {
  const [query, setQuery] = useState<SettingsQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery((previous) =>
      previous.status === 'loaded' ? { ...previous, isRefetching: true } : { status: 'loading' },
    )

    fetchSettings(controller.signal)
      .then((settings) => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'loaded', settings, isRefetching: false })
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [refetchToken])

  return query
}
