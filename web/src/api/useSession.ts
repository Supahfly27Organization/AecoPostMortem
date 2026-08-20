import { useEffect, useState } from 'react'
import { fetchSession, type SessionEnvelope } from './session'

export type SessionQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; envelope: SessionEnvelope }

/** Fetches one session's masthead and tape (FR-21) once per `sessionId`. Loading renders nothing
 * (see `SessionPage`) rather than a message that might not apply a moment later — the same
 * discipline `useAppState` documents for its own fetch. A failed fetch covers both "this session
 * id does not exist" (a 404) and "the local API is unreachable": `SessionPage` renders one message
 * for both, since neither is a state a session recorder can partially render around. */
export function useSession(sessionId: string): SessionQuery {
  const [query, setQuery] = useState<SessionQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery({ status: 'loading' })

    fetchSession(sessionId, controller.signal)
      .then((envelope) => setQuery({ status: 'loaded', envelope }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [sessionId])

  return query
}
