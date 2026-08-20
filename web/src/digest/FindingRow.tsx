import { useState } from 'react'
import type { FindingEnvelope } from '../api/digest'
import { ProvenanceBadge } from './ProvenanceBadge'
import { RecurrenceStrip } from './RecurrenceStrip'
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
 * expanded `RecurrenceStrip`'s job — the count ranks, the names explain. */
export function FindingRow({ finding }: { finding: FindingEnvelope }) {
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
        <ProvenanceBadge provenance={finding.provenance} />
      </button>

      {expanded && (
        <div className="finding-row__detail">
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
