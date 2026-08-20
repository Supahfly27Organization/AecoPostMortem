import type { RecurrenceOccurrence } from '../api/digest'
import './SessionStrip.css'

/**
 * Mockup parity item #2 (docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md,
 * `docs/product-superpowers/discovery/mockups/digest.html`'s own `.strip`): one cell per session in
 * the currently scoped corpus, lit where this finding's own `recurrence.occurrences` touched that
 * session — rendered on the collapsed row, so a finding's *pattern* of recurrence (clustered vs.
 * spread across the corpus) is visible at a glance, not only its count.
 *
 * `sessionIds` is `masthead.repositoryScope.sessionIds` (`AecoPostMortem.Findings.RepositoryScope`)
 * — the same, chronologically ordered set every check on the digest was scoped to — never a
 * corpus-wide list a finding's own occurrences might not be a subset of. A session id present in
 * `occurrences` but absent from `sessionIds` (should not happen per that contract, but is not
 * trusted blindly here) simply cannot be positioned, so it lights nothing extra rather than
 * crashing or padding the strip.
 */
export function SessionStrip({
  sessionIds,
  occurrences,
}: {
  sessionIds: string[]
  occurrences: RecurrenceOccurrence[]
}) {
  if (sessionIds.length === 0) {
    return null
  }

  const touched = new Set(occurrences.map((occurrence) => occurrence.sessionId))
  const touchedCount = sessionIds.filter((sessionId) => touched.has(sessionId)).length

  return (
    <div
      className="session-strip"
      role="img"
      aria-label={`${touchedCount} of ${sessionIds.length} sessions affected`}
    >
      {sessionIds.map((sessionId) => (
        <i
          key={sessionId}
          className={
            touched.has(sessionId) ? 'session-strip__cell session-strip__cell--on' : 'session-strip__cell'
          }
        />
      ))}
    </div>
  )
}
