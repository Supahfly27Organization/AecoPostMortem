import { useEffect, useState } from 'react'
import { fetchSettings, type SettingsEnvelope } from './settings'

export type SettingsQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; settings: SettingsEnvelope }

/** Fetches the Settings surface's read-only configuration (Part A), re-fetching whenever
 * `refetchToken` changes — `SettingsPage` bumps it after a successful ingest or rebuild, so the
 * store-existence/size figures shown reflect what the write just did rather than what was true when
 * the page first loaded. Loading renders nothing and a failed fetch is its own explicit state, the
 * same shape `useAppState`/`useRulesInventory` already establish. */
export function useSettings(refetchToken: number): SettingsQuery {
  const [query, setQuery] = useState<SettingsQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery({ status: 'loading' })

    fetchSettings(controller.signal)
      .then((settings) => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'loaded', settings })
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
