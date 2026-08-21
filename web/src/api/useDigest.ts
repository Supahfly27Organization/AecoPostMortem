import { useEffect, useState } from 'react'
import { fetchDigest, type DigestEnvelope } from './digest'

export type DigestQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; digest: DigestEnvelope; isRefetching: boolean }

/** Fetches FR-41's digest, the same loading/error/loaded shape `useAppState` established: loading
 * renders nothing rather than a message that might not apply a moment later, and a failed fetch is
 * its own explicit state, distinct from a loaded digest that has nothing to show
 * (`DigestState.NotYetAnalyzed`/`Incomplete`, which the digest itself already states honestly).
 *
 * Re-fetches whenever `from`/`to` change — the date-range filter task's own design decision (see
 * `AecoPostMortem.Api/CLAUDE.md`'s "A date-range filter re-scopes the whole analysis"): a filter
 * change re-scopes every count/recurrence/rank server-side, the same "a new request, not a filter
 * over what is already loaded" shape `useRulesInventory` already established for switching rule-set
 * versions, not `RepositorySelector`'s own display-only seam. Both `null` (the default) behaves
 * exactly as this hook did before the filter existed — one fetch on mount.
 *
 * Two plain scalar parameters, not a `{from, to}` object: `DigestPage` used to hold that pair in one
 * `range` object, which meant this hook's own `useEffect` had to spell out `range.from`/`range.to`
 * in its dependency array rather than the object itself (an object literal is a new reference every
 * render, so depending on it directly would re-fetch every render) — a real lint warning
 * (`react-hooks/exhaustive-deps`) code review caught. Two scalars sidestep it structurally.
 *
 * Code review Important #4: `status: 'loading'` used to be returned for every fetch, including a
 * re-fetch triggered by a changed `from`/`to` — `DigestPage`'s loading branch renders nothing but a
 * bare heading, so applying a filter blanked the masthead, the selector and the filter control
 * itself while the new request was in flight, a "does this work?" dead spot mid-interaction. Once a
 * digest has loaded at least once, a subsequent fetch instead keeps `status: 'loaded'` with the
 * previous `digest` still attached and `isRefetching: true`, so `DigestPage` can keep the whole page
 * rendered and show a small "updating" status instead of blanking everything. Only the very first
 * fetch (no previous digest to keep showing) still reports bare `status: 'loading'`. */
export function useDigest(from: string | null = null, to: string | null = null): DigestQuery {
  const [query, setQuery] = useState<DigestQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery((previous) =>
      previous.status === 'loaded' ? { ...previous, isRefetching: true } : { status: 'loading' },
    )

    // Guarded on the resolution path too, not just the rejection: a response that settles after
    // from/to changed would otherwise overwrite the new request's state with the previous range's
    // digest — the same guard `useRulesInventory` applies for the identical reason.
    fetchDigest({ from, to }, controller.signal)
      .then((digest) => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'loaded', digest, isRefetching: false })
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [from, to])

  return query
}
