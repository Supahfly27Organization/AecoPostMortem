import type { RulesInventoryStatusCountsEnvelope } from '../api/rulesInventory'
import './RuleCoverageBar.css'

const count = new Intl.NumberFormat('en-GB')

type CoverageSegmentKey = 'watched' | 'checkableNotYetBuilt' | 'notCheckable' | 'notARule'

const SEGMENTS: { key: CoverageSegmentKey; label: string }[] = [
  { key: 'watched', label: 'watched' },
  { key: 'checkableNotYetBuilt', label: 'checkable, not built' },
  { key: 'notCheckable', label: 'normative but unobservable' },
  { key: 'notARule', label: 'not a rule' },
]

/**
 * Mockup parity item #15: the masthead's rule-coverage bar, ported from
 * `docs/product-superpowers/discovery/mockups/digest.html`'s `.covbar`/`.covkey` with this app's own
 * design tokens (`web/CLAUDE.md`'s "Design tokens are ported verbatim from the mockups" note), not
 * the mockup's raw hex values.
 *
 * A real, deliberate divergence from the mockup's own layout, not a port defect: the mockup's bar is
 * proportional only over "actual rules" (watched + checkable + unobservable), with "not a rule"
 * named separately as plain, uncoloured text. This bar is proportional over all four statuses —
 * `RulesInventoryStatusCountsEnvelope.total`, the full breakdown FR-40 defines — each with its own
 * coloured segment and legend entry: a three-segment bar would leave "not a rule" (often the corpus'
 * largest bucket — `RulesInventoryPage`'s own "No status count is styled as a problem count" note)
 * with no visual representation at all in the one element whose entire point is to show where the
 * corpus' rules landed.
 */
export function RuleCoverageBar({ counts }: { counts: RulesInventoryStatusCountsEnvelope }) {
  if (counts.total === 0) {
    return (
      <p className="rule-coverage-bar__empty">
        No rule statements were extracted for this rule-set version.
      </p>
    )
  }

  const ariaLabel =
    `Of ${count.format(counts.total)} extracted rule statements, ${count.format(counts.watched)} are watched, ` +
    `${count.format(counts.checkableNotYetBuilt)} are checkable but not yet built, ` +
    `${count.format(counts.notCheckable)} are normative but unobservable, and ` +
    `${count.format(counts.notARule)} are not rules at all.`

  return (
    <div className="rule-coverage-bar">
      <div className="rule-coverage-bar__bar" role="img" aria-label={ariaLabel}>
        {SEGMENTS.map(segment => (
          <i
            key={segment.key}
            className="rule-coverage-bar__segment"
            data-segment={segment.key}
            style={{ width: `${(counts[segment.key] / counts.total) * 100}%` }}
          />
        ))}
      </div>
      <ul className="rule-coverage-bar__legend">
        {SEGMENTS.map(segment => (
          <li key={segment.key} data-segment={segment.key}>
            <i className="rule-coverage-bar__swatch" data-segment={segment.key} aria-hidden="true" />
            <b>{count.format(counts[segment.key])}</b> {segment.label}
          </li>
        ))}
      </ul>
    </div>
  )
}
