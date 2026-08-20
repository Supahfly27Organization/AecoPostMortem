import { useState } from 'react'
import type { FindingEnvelope } from '../api/digest'
import { ProvenanceBadge } from './ProvenanceBadge'
import { RecurrenceStrip } from './RecurrenceStrip'
import { SuggestionBlock } from './SuggestionBlock'
import './FindingRow.css'

/** Scenario 1 (issue #45): a digest row collapsed by default; expanding it reveals the evidence
 * quoting the actual event fields, its provenance badge, the recurrence strip (Scenario 2) and its
 * suggestion (Scenario 4: an explicit "no suggestion offered" when the finding's class has none) —
 * everything `FindingEnvelope` already carries, this only decides when to show it. */
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
