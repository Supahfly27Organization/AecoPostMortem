import { useEffect, useState } from 'react'
import { fetchAppState, type AppStateReport } from './appState'

export type AppStateQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; report: AppStateReport }

/** Fetches S-48's app-state diagnosis once on mount. Loading renders nothing (see
 * `AppStateBanner`) rather than a message that might not apply a moment later; a failed fetch
 * (the API host is not running) is its own explicit state, distinct from either empty-data
 * diagnosis the API itself can report. */
export function useAppState(): AppStateQuery {
  const [query, setQuery] = useState<AppStateQuery>({ status: 'loading' })

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
  }, [])

  return query
}
