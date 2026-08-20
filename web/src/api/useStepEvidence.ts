import { useEffect, useState } from 'react'
import { fetchStepEvidence, type SessionTapeStepKind, type StepEvidenceEnvelope } from './session'

export type StepEvidenceQuery =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'loaded'; evidence: StepEvidenceEnvelope }

/** Fetches one step's Thinking/Raw evidence (FR-21 part 2 of 3, S-52, issue #16) once per selected
 * step. Loading renders nothing, the same discipline `useSession` documents for its own fetch —
 * there is no `sessionId`/`stepId` to fetch for until a step is selected, which is exactly the
 * "nothing selected" designed state `SessionPage` renders without calling this hook at all. */
export function useStepEvidence(
  sessionId: string,
  stepId: string,
  kind: SessionTapeStepKind,
): StepEvidenceQuery {
  const [query, setQuery] = useState<StepEvidenceQuery>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    setQuery({ status: 'loading' })

    fetchStepEvidence(sessionId, stepId, kind, controller.signal)
      .then((evidence) => setQuery({ status: 'loaded', evidence }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setQuery({ status: 'error' })
        }
      })

    return () => controller.abort()
  }, [sessionId, stepId, kind])

  return query
}
