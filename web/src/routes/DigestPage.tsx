import { useState } from 'react'
import { useDigest } from '../api/useDigest'
import { FindingRow } from '../digest/FindingRow'
import { Masthead } from '../digest/Masthead'
import { RepositorySelector } from '../digest/RepositorySelector'
import './DigestPage.css'

/**
 * The front door (PRD §3.1: "Getting started ... Open the Process Digest"). FR-41's masthead and
 * ranking (S-36, issue #44) plus row expansion, the recurrence strip and the repository selector
 * (S-54, issue #45).
 *
 * The order on the page is the story's argument: the corpus scope first (what was looked at), then
 * the findings ranked by how many of those sessions each touched (what to fix first). Neither the
 * masthead nor this page counts anything — `MastheadEnvelope`'s figures are ingest-time counters and
 * `sessionsAffected` is the key the server already ranked by, so nothing here re-derives a total
 * from the findings it happens to be holding.
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

      <Masthead masthead={digest.masthead} state={digest.state} />

      <RepositorySelector scope={displayedScope} onSelect={setPendingRepository} />

      {/* The three designed states, each said in its own words rather than all collapsing into an
          unexplained empty list. "Nothing analysed yet" and "found nothing" are different facts
          about the operator's setup and lead to different next actions — see
          `AecoPostMortem.Findings.DigestState`, which draws the same distinction one layer down. */}
      {digest.state === 'NotYetAnalyzed' && (
        <p className="digest-page__state">
          Nothing has been analysed yet — no check has run against this corpus.
        </p>
      )}
      {digest.state === 'Incomplete' && (
        <p className="digest-page__state">
          Analysis is incomplete — ingestion is still under way, so this ranking is not final.
        </p>
      )}
      {digest.state === 'Analyzed' &&
        digest.rankedFindings.length === 0 &&
        digest.inferredFindings.length === 0 && (
          <p className="digest-page__state">Every check ran and found nothing.</p>
        )}

      <ul className="digest-page__findings">
        {digest.rankedFindings.map((finding) => (
          <FindingRow key={`${finding.class}:${finding.recurrence.key}`} finding={finding} />
        ))}
      </ul>

      {/* FR-48 (issue #52, S-42): `inferredFindings` is real, served data
          (`DigestEnvelope.InferredFindings`) — never interleaved by rank with the list above (see
          `Findings/CLAUDE.md`'s own remarks on why a hypothesis is never ranked by sessions
          affected). Renders no section at all when the list is empty, the same "no section at all"
          discipline `AgentLanes` already established for an empty `envelope.lanes` (`web/CLAUDE.md`)
          — there is nothing designed to say here beyond simply not showing the section. */}
      {digest.inferredFindings.length > 0 && (
        <section className="digest-page__inferred" aria-labelledby="digest-page__inferred-heading">
          <h3 id="digest-page__inferred-heading">Judgment calls</h3>
          <p className="digest-page__state">
            Hypotheses inferred from the data, not measured claims — shown separately from the
            ranked findings above and never ranked by sessions affected.
          </p>
          <ul className="digest-page__findings">
            {digest.inferredFindings.map((finding) => (
              <FindingRow
                key={`${finding.class}:${finding.recurrence.key}`}
                finding={finding}
                variant="unranked"
              />
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
