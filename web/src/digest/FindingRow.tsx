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
 * ranked finding was scoped to. */
export function FindingRow({
  finding,
  sessionIds,
}: {
  finding: FindingEnvelope
  sessionIds: string[]
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
        <span className="finding-row__sessions" data-rank-metric="sessions-affected">
          <strong className="finding-row__sessions-count">{finding.sessionsAffected}</strong>
          <span className="finding-row__sessions-unit">
            {finding.sessionsAffected === 1 ? 'session' : 'sessions'}
          </span>
        </span>
        <span className="finding-row__key">{finding.recurrence.key}</span>
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
