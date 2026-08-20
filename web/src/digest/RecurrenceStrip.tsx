import type { Recurrence } from '../api/digest'
import './RecurrenceStrip.css'

/** Scenario 2 (issue #45): names which sessions a finding touched, not only how many —
 * `Recurrence.occurrences` already carries every session id (FR-57's recurrence key), this only
 * renders it. */
export function RecurrenceStrip({ recurrence }: { recurrence: Recurrence }) {
  return (
    <ul className="recurrence-strip" aria-label="Sessions touched">
      {recurrence.occurrences.map((occurrence) => (
        <li key={occurrence.sessionId} className="recurrence-strip__session">
          {occurrence.sessionId}
        </li>
      ))}
    </ul>
  )
}
