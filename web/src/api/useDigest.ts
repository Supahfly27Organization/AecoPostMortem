import { useEffect, useState } from 'react'
import { fetchDigest, type DateRange, type DigestEnvelope } from './digest'

export type DigestQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; digest: DigestEnvelope }

/** Fetches FR-41's digest, the same loading/error/loaded shape `useAppState` established: loading
 * renders nothing rather than a message that might not apply a moment later, and a failed fetch is
 * its own explicit state, distinct from a loaded digest that has nothing to show
 * (`DigestState.NotYetAnalyzed`/`Incomplete`, which the digest itself already states honestly).
 *
 * Re-fetches whenever `range` changes — the date-range filter task's own design decision (see
 * `AecoPostMortem.Api/CLAUDE.md`'s "A date-range filter re-scopes the whole analysis"): a filter
 * change re-scopes every count/recurrence/rank server-side, the same "a new request, not a filter
 * over what is already loaded" shape `useRulesInventory` already established for switching rule-set
 * versions, not `RepositorySelector`'s own display-only seam. `{ from: null, to: null }` (the
 * default) behaves exactly as this hook did before the filter existed — one fetch on mount. */
export function useDigest(range: DateRange = { from: null, to: null }): DigestQuery {
  const [query, setQuery] = useState<DigestQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery({ status: 'loading' })

    // Guarded on the resolution path too, not just the rejection: a response that settles after the
    // range changed would otherwise overwrite the new request's `loading` with the previous range's
    // digest — the same guard `useRulesInventory` applies for the identical reason.
    fetchDigest(range, controller.signal)
      .then((digest) => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'loaded', digest })
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [range.from, range.to])

  return query
}
