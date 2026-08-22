import { useState } from 'react'
import type { DateRange } from '../api/digest'
import { useDigest } from '../api/useDigest'
import { CleanChecks } from '../digest/CleanChecks'
import { DateRangeFilter } from '../digest/DateRangeFilter'
import { FindingRow } from '../digest/FindingRow'
import { Masthead } from '../digest/Masthead'
import { MethodologyFooter } from '../digest/MethodologyFooter'
import { Pager } from '../digest/Pager'
import { RepositorySelector } from '../digest/RepositorySelector'
import './DigestPage.css'

/** How many ranked findings render per page — client-side, over the already-served list. The live
 * corpus serves 297 ranked findings for its dominant repository, well within a single fetch's own
 * payload size (this page already fetches the whole digest in one shot); a server-side offset/limit
 * contract is deliberately deferred until a corpus's real scale justifies one — see the "The pager is
 * client-side" non-obvious decision in `web/CLAUDE.md`. */
const PAGE_SIZE = 25

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
  const [range, setRange] = useState<DateRange>({ from: null, to: null })
  const query = useDigest(range.from, range.to)
  const [pendingRepository, setPendingRepository] = useState<string | null>(null)
  const [page, setPage] = useState(1)

  // A new range re-scopes the whole analysis server-side (see `useDigest`'s own remarks), so the
  // previous range's page position has no meaning against the new list — always back to page 1.
  function applyRange(from: string | null, to: string | null) {
    setRange({ from, to })
    setPage(1)
  }

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

  const { digest, isRefetching } = query
  const scope = digest.masthead.repositoryScope
  // The seam PRD Part 8 Q5 names: selecting another repository updates which one the selector
  // shows, but no caller here re-fetches a cross-repository digest yet (that view is later work) —
  // this story implements the default and keeps the control itself real and selectable.
  const displayedScope = { ...scope, selectedRepository: pendingRepository ?? scope.selectedRepository }

  // "Nothing was in scope" is now a served fact (`DigestState.NothingInScope`), not something this
  // page derives. It used to be `rangeActive && scope.sessionIds.length === 0` — a client-side
  // derivation that necessarily missed the unfiltered case (an empty store, or a repository carrying
  // no sessions), which therefore still rendered "Every check ran and found nothing." about a scope
  // nothing ever looked at. `rangeActive` survives only to choose *which* sentence to say: the cause
  // differs, and naming the real one is the difference between an operator clearing a filter and an
  // operator wondering why their corpus looks clean.
  const rangeActive = range.from !== null || range.to !== null
  const nothingInScope = digest.state === 'NothingInScope'

  // Clamped rather than trusted outright: a stale `page` (e.g. a shrinking list under a new range,
  // even though `applyRange` already resets to 1) never indexes past the end of what is actually
  // being served — the same "never serve a number the data doesn't support" discipline this app
  // follows for every other figure.
  const pageCount = Math.max(1, Math.ceil(digest.rankedFindings.length / PAGE_SIZE))
  const currentPage = Math.min(page, pageCount)
  const pageStart = (currentPage - 1) * PAGE_SIZE
  const pagedFindings = digest.rankedFindings.slice(pageStart, pageStart + PAGE_SIZE)

  return (
    <div className="digest-page">
      <h2>Process Digest</h2>

      <Masthead masthead={digest.masthead} state={digest.state} />

      <RepositorySelector scope={displayedScope} onSelect={setPendingRepository} />

      <DateRangeFilter from={range.from} to={range.to} onApply={applyRange} />

      {/* Code review Important #4: a re-fetch (a new date range) used to blank the whole page —
          `useDigest` now keeps the previous digest attached with `isRefetching: true` instead of
          reporting bare `loading`, so nothing above or below unmounts; this is the one visible sign
          a new request is under way. `role="status"` is an implicit `aria-live="polite"` region, so
          assistive technology announces it without stealing focus the way `role="alert"` would. */}
      {isRefetching && (
        <p className="digest-page__state" role="status">
          Updating…
        </p>
      )}

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
      {nothingInScope && rangeActive && (
        <p className="digest-page__state">
          No sessions in the selected repository started in the applied date range — nothing was
          looked at, which is a different fact from every check running clean.
        </p>
      )}
      {nothingInScope && !rangeActive && (
        <p className="digest-page__state">
          No sessions in the selected repository have been ingested — nothing was looked at, which is
          a different fact from every check running clean.
        </p>
      )}
      {digest.state === 'Analyzed' &&
        digest.rankedFindings.length === 0 &&
        digest.inferredFindings.length === 0 && (
          <p className="digest-page__state">Every check ran and found nothing.</p>
        )}

      {!nothingInScope && (
        <>
          <ul className="digest-page__findings">
            {pagedFindings.map((finding) => (
              <FindingRow
                key={`${finding.class}:${finding.recurrence.key}`}
                finding={finding}
                sessionIds={digest.masthead.repositoryScope.sessionIds}
                sessionLabels={digest.masthead.repositoryScope.sessionLabels}
              />
            ))}
          </ul>

          <Pager page={currentPage} pageCount={pageCount} onChange={setPage} />

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
                    sessionIds={digest.masthead.repositoryScope.sessionIds}
                    sessionLabels={digest.masthead.repositoryScope.sessionLabels}
                    variant="unranked"
                  />
                ))}
              </ul>
            </section>
          )}

          <CleanChecks checks={digest.silentChecks} />
        </>
      )}

      <MethodologyFooter masthead={digest.masthead} range={rangeActive ? range : null} />
    </div>
  )
}
