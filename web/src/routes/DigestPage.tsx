import { useState } from 'react'
import { useDigest } from '../api/useDigest'
import { FindingRow } from '../digest/FindingRow'
import { RepositorySelector } from '../digest/RepositorySelector'
import './DigestPage.css'

/**
 * The front door (PRD §3.1: "Getting started ... Open the Process Digest"). FR-41's masthead and
 * ranking (S-36, issue #44) plus row expansion, the recurrence strip and the repository selector
 * (S-54, issue #45, this story).
 *
 * `/api/digest` is not served by `ApiHost` yet (`web/src/api/digest.ts` documents why: assembling a
 * real `ProcessDigest` from the live store is later, unwired work). `useDigest` targets the route
 * ahead of that wiring, the same seam `useAppState` established for `/api/app-state` before S-48
 * served it — a fetch failure here renders its own distinct message rather than nothing, exactly
 * `AppStateBanner`'s pattern for "the API host is unreachable".
 */
export function DigestPage() {
  const query = useDigest()
  const [pendingRepository, setPendingRepository] = useState<string | null>(null)

  if (query.status === 'loading') {
    return (
      <div className="digest-page">
        <h2>Process Digest</h2>
      </div>
    )
  }

  if (query.status === 'error') {
    return (
      <div className="digest-page">
        <h2>Process Digest</h2>
        <p role="alert">
          Could not reach the local API. Is <code>aecopostmortem serve</code> running?
        </p>
      </div>
    )
  }

  const { digest } = query
  const scope = digest.masthead.repositoryScope
  // The seam PRD Part 8 Q5 names: selecting another repository updates which one the selector
  // shows, but no caller here re-fetches a cross-repository digest yet (that view is later work) —
  // this story implements the default and keeps the control itself real and selectable.
  const displayedScope = { ...scope, selectedRepository: pendingRepository ?? scope.selectedRepository }

  return (
    <div className="digest-page">
      <h2>Process Digest</h2>

      <RepositorySelector scope={displayedScope} onSelect={setPendingRepository} />

      {digest.state === 'NotYetAnalyzed' && <p>No check has run against this corpus yet.</p>}
      {digest.state === 'Incomplete' && <p>Ingestion is still under way — this digest is incomplete.</p>}
      {digest.state === 'Analyzed' && digest.rankedFindings.length === 0 && (
        <p>Every check ran clean — nothing to show.</p>
      )}

      <ul className="digest-page__findings">
        {digest.rankedFindings.map((finding) => (
          <FindingRow key={`${finding.class}:${finding.recurrence.key}`} finding={finding} />
        ))}
      </ul>
    </div>
  )
}
