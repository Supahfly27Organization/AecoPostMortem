import { useEffect, useState } from 'react'
import { fetchAppState, type AppStateReport } from './appState'
import { onStoreChanged } from './storeChangeEvents'

export type AppStateQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; report: AppStateReport }

/** Fetches S-48's app-state diagnosis once on mount, and again whenever the Settings page reports a
 * completed ingest or rebuild (`storeChangeEvents.ts`'s own remarks — this banner is mounted for the
 * whole SPA session, so a route change alone does not refresh it the way a page's own per-mount fetch
 * would). Loading renders nothing (see `AppStateBanner`) rather than a message that might not apply
 * a moment later; a failed fetch (the API host is not running) is its own explicit state, distinct
 * from either empty-data diagnosis the API itself can report. */
export function useAppState(): AppStateQuery {
  const [query, setQuery] = useState<AppStateQuery>({ status: 'loading' })
  const [refetchToken, setRefetchToken] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    setQuery({ status: 'loading' })

    fetchAppState(controller.signal)
      .then((report) => setQuery({ status: 'loaded', report }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
    // refetchToken only ever exists to trigger this effect again after a completed write
    // (`storeChangeEvents.ts`); it carries no data of its own.
  }, [refetchToken])

  useEffect(() => onStoreChanged(() => setRefetchToken((token) => token + 1)), [])

  return query
}
