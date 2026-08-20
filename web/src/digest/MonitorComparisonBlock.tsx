import type { MonitorComparisonEnvelope } from '../api/monitor'
import type { AdherenceFigure } from '../api/digest'
import type { RuleSetVersionEnvelope } from '../api/rulesInventory'
import { AdherenceFigureBlock } from './AdherenceFigureBlock'
import './MonitorComparisonBlock.css'

/**
 * One side of the comparison: its own sample size, at the same visual weight
 * `AdherenceFigureBlock` gives its percentage, followed by that percentage and its full
 * resolution table. Reusing `AdherenceFigureBlock` here rather than re-rendering the percentage
 * is what keeps this project's own rule intact — "no component ... can put a percentage on the
 * page without the operands beside it" (`AdherenceFigureBlock.tsx`'s own doc comment) — this
 * component adds a second, equally prominent figure beside it, never a second percentage.
 */
function MonitorSide({
  label,
  version,
  figure,
}: {
  label: 'Before' | 'After'
  version: RuleSetVersionEnvelope
  figure: AdherenceFigure
}) {
  return (
    <div className="monitor-comparison__side" data-side={label.toLowerCase()}>
      <h4 className="monitor-comparison__side-label">{label}</h4>

      {/* Scenario 2 (issue #43): "the session count on each side is as visible as the
          percentage" — the edge case warns against a two-number story overwhelming a two-session
          sample, so the sample size shares `adherence-figure__percentage`'s own class and
          `data-emphasis="prominent"` marker, not a smaller annotation a later change could shrink
          on its own without touching the percentage too. */}
      <p className="monitor-comparison__sample-size">
        <span
          className="adherence-figure__percentage monitor-comparison__session-count"
          data-emphasis="prominent"
        >
          {version.sessionCount} {version.sessionCount === 1 ? 'session' : 'sessions'}
        </span>
      </p>

      <AdherenceFigureBlock figure={figure} />
    </div>
  )
}

/**
 * FR-39 (S-35, issue #43): "the Monitor comparison" — adherence for the same rule, before and
 * after an adjacent rule-set-version edit. Scenario 1's "under a single stated resolution" is
 * already structural on the server (`MonitorComparison.Compare` resolves the operand pair once
 * and reuses it for both sides) — this component renders that shared shape, one
 * `AdherenceFigureBlock` per side, so nothing here can drift the two sides' resolutions apart on
 * the way to the screen.
 */
export function MonitorComparisonBlock({ comparison }: { comparison: MonitorComparisonEnvelope }) {
  return (
    <section className="monitor-comparison">
      <div className="monitor-comparison__sides">
        <MonitorSide label="Before" version={comparison.beforeVersion} figure={comparison.before} />
        <MonitorSide label="After" version={comparison.afterVersion} figure={comparison.after} />
      </div>
    </section>
  )
}
