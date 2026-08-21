import { useState } from 'react'
import type { FindingEnvelope } from '../api/digest'
import { AdherenceFigureBlock } from './AdherenceFigureBlock'
import { ProvenanceBadge } from './ProvenanceBadge'
import { RecurrenceStrip } from './RecurrenceStrip'
import { SessionStrip } from './SessionStrip'
import { SuggestionBlock } from './SuggestionBlock'
import './FindingRow.css'

/** Scenario 1 (issue #45): a digest row collapsed by default; expanding it reveals the evidence
 * quoting the actual event fields, its provenance badge, the recurrence strip (Scenario 2) and its
 * suggestion (Scenario 4: an explicit "no suggestion offered" when the finding's class has none) —
 * everything `FindingEnvelope` already carries, this only decides when to show it.
 *
 * `sessionsAffected` is the exception that stays visible while collapsed (S-36, issue #44): it is
 * the key the list is ranked by, and its whole purpose is to make a finding touching one session
 * read as an anecdote beside one touching thirty. Behind an expander it could not do that, so it
 * leads the summary at display size rather than annotating it. The full session list is still the
 * expanded `RecurrenceStrip`'s job — the count ranks, the names explain.
 *
 * Mockup parity item #2: `SessionStrip` joins `sessionsAffected` on the collapsed row for the same
 * reason — which sessions, in what pattern, is also worth scanning without expanding. `sessionIds`
 * is the caller's `masthead.repositoryScope.sessionIds` (`DigestPage`), the same session set every
 * ranked finding was scoped to.
 *
 * `variant` (FR-48, issue #52, S-42) defaults to `'ranked'`, unchanged from before this prop
 * existed. The digest's own "Judgment calls" section passes `'unranked'` for a
 * `DigestEnvelope.inferredFindings` entry: `Findings/CLAUDE.md` is explicit that an Inferred
 * finding is never ranked by `sessionsAffected`, and this leading column exists specifically to
 * make that number the most visually prominent thing on the row (S-36's edge case) — showing it at
 * the same prominence on a hypothesis would visually contradict the guarantee the server went out
 * of its way to build. Nothing is lost by omitting it: `RecurrenceStrip`, rendered on expand either
 * way, already names every session the finding touched.
 *
 * Mockup parity item #5: the collapsed summary's headline is `finding.headline` — a full written
 * sentence naming the problem (the mockup's own `t` field) — never `finding.recurrence.key`, a raw
 * tool name or a rule's own text with no sentence around it. `finding-row__headline` (renamed from
 * `finding-row__key`, `FindingRow.css`) renders it in the app's sans font rather than the mono font
 * a bare key used, matching the mockup's own `.ttl .t` styling for a title. */
export function FindingRow({
  finding,
  sessionIds,
  variant = 'ranked',
}: {
  finding: FindingEnvelope
  sessionIds: string[]
  variant?: 'ranked' | 'unranked'
}) {
  const [expanded, setExpanded] = useState(false)

  return (
    <li className="finding-row" data-class={finding.class}>
      <button
        type="button"
        className="finding-row__summary"
        aria-expanded={expanded}
        onClick={() => setExpanded((current) => !current)}
      >
        {variant === 'ranked' && (
          <span className="finding-row__sessions" data-rank-metric="sessions-affected">
            <strong className="finding-row__sessions-count">{finding.sessionsAffected}</strong>
            <span className="finding-row__sessions-unit">
              {finding.sessionsAffected === 1 ? 'session' : 'sessions'}
            </span>
          </span>
        )}
        <span className="finding-row__headline">{finding.headline}</span>
        <SessionStrip sessionIds={sessionIds} occurrences={finding.recurrence.occurrences} />
        <ProvenanceBadge provenance={finding.provenance} />
      </button>

      {expanded && (
        <div className="finding-row__detail">
          {/* FR-33 (S-24, issue #38): an adherence finding's figure is rendered by
              `AdherenceFigureBlock`, which owns the percentage and the per-operand resolution
              together. This row never reads `figure.percentage` itself, so there is no path here
              that could show the number without the layers that produced it. The collapsed summary
              above deliberately shows no figure at all for the same reason. */}
          {finding.kind === 'adherence' && <AdherenceFigureBlock figure={finding.figure} />}

          <RecurrenceStrip recurrence={finding.recurrence} />

          <dl className="finding-row__evidence">
            {finding.evidence.map((item) => (
              <div className="finding-row__evidence-item" key={item.field}>
                <dt>{item.field}</dt>
                <dd>{item.value}</dd>
              </div>
            ))}
          </dl>

          <SuggestionBlock suggestion={finding.suggestion} />
        </div>
      )}
    </li>
  )
}
