import { Link } from 'react-router-dom'
import type { Recurrence } from '../api/digest'
import './RecurrenceStrip.css'

/** Scenario 2 (issue #45): names which sessions a finding touched, not only how many —
 * `Recurrence.occurrences` already carries every session id (FR-57's recurrence key), this only
 * renders it. Mockup parity item #21: each session id is a real link to `/sessions/:sessionId`,
 * the route that already renders it — previously plain text an operator had to copy by hand. */
export function RecurrenceStrip({ recurrence }: { recurrence: Recurrence }) {
  return (
    <ul className="recurrence-strip" aria-label="Sessions touched">
      {recurrence.occurrences.map((occurrence) => (
        <li key={occurrence.sessionId} className="recurrence-strip__session">
          <Link to={`/sessions/${occurrence.sessionId}`}>{occurrence.sessionId}</Link>
        </li>
      ))}
    </ul>
  )
}
