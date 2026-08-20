import { useEffect, useState } from 'react'
import { fetchDigest, type DigestEnvelope } from './digest'

export type DigestQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; digest: DigestEnvelope }

/** Fetches FR-41's digest once on mount, the same shape `useAppState` established for app-state:
 * loading renders nothing rather than a message that might not apply a moment later, and a failed
 * fetch (no live `/api/digest` yet — see `api/digest.ts`) is its own explicit state, distinct from a
 * loaded digest that has nothing to show (`DigestState.NotYetAnalyzed`/`Incomplete`, which the
 * digest itself already states honestly). */
export function useDigest(): DigestQuery {
  const [query, setQuery] = useState<DigestQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery({ status: 'loading' })

    fetchDigest(controller.signal)
      .then((digest) => setQuery({ status: 'loaded', digest }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [])

  return query
}
