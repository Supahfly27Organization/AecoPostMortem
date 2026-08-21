import { Link } from 'react-router-dom'
import type { Recurrence } from '../api/digest'
import './RecurrenceStrip.css'

/** Scenario 2 (issue #45): names which sessions a finding touched, not only how many —
 * `Recurrence.occurrences` already carries every session id (FR-57's recurrence key), this only
 * renders it. Mockup parity item #21: each session id is a real link to `/sessions/:sessionId`,
 * the route that already renders it — previously plain text an operator had to copy by hand.
 * Digest session-naming (Slice 2): the link's own visible text is that session's own `sessionLabels`
 * entry when one resolved (the first five words of its earliest real prompt), falling back to the
 * raw session id otherwise — the full session id is always the link's `title` (tooltip), so it stays
 * one click/hover away from a reader who needs it verbatim (to paste into a URL, say). */
export function RecurrenceStrip({
  recurrence,
  sessionLabels = {},
}: {
  recurrence: Recurrence
  sessionLabels?: Record<string, string>
}) {
  return (
    <ul className="recurrence-strip" aria-label="Sessions touched">
      {recurrence.occurrences.map((occurrence) => (
        <li key={occurrence.sessionId} className="recurrence-strip__session">
          <Link to={`/sessions/${occurrence.sessionId}`} title={occurrence.sessionId}>
            {sessionLabels[occurrence.sessionId] ?? occurrence.sessionId}
          </Link>
        </li>
      ))}
    </ul>
  )
}
