import type { ReactNode } from 'react'
import type { DigestState, MastheadEnvelope } from '../api/digest'
import { RuleCoverageBar } from './RuleCoverageBar'
import './Masthead.css'

/**
 * FR-41's corpus masthead (S-36, issue #44): the scope every ranked finding below it is ranked
 * *within* — sessions, span, repositories, events, tool calls and rule coverage. Mockup parity
 * item #8 added a sixth mockup cell, subagents (`masthead.subagentCount`), positioned last to match
 * the mockup's own cell order (Sessions/Span/Repositories/Events/Tool calls/Subagents) — rendered
 * after this app's own rule-coverage cell, which the mockup itself does not carry. Mockup parity item
 * #15 replaced the rule-coverage cell's plain text with a real proportional bar (`RuleCoverageBar`)
 * once `masthead.ruleCoverage` carries a real breakdown — see that component's own remarks.
 *
 * Every figure here is read straight off `MastheadEnvelope`, which
 * `AecoPostMortem.Findings.MastheadCounters` documents as counters maintained at ingest time. This
 * component therefore does no counting of its own — not even over data it already has in hand (it
 * never derives a session total from `rankedFindings`, say). That is the rendering half of S-36's
 * load-bearing constraint: counting a million rows measured 126 ms on SQLite and 118 ms on Postgres,
 * so the masthead must never be the reason a scan happens, at any layer.
 */
export function Masthead({ masthead, state }: { masthead: MastheadEnvelope; state: DigestState }) {
  // Scenario 4: mid-ingest counts are real, but they are not the whole corpus. Saying so is the
  // difference between "35 sessions" as a fact and "35 sessions" as a claim that turns out false a
  // minute later — the digest states the counts are still moving rather than presenting them final.
  const provisional = state === 'Incomplete'

  return (
    <section
      className="masthead"
      role="group"
      aria-label="Corpus scope"
      data-provisional={provisional}
    >
      {provisional && (
        <p className="masthead__provisional" role="status">
          Ingestion is still under way — these counts are provisional, not the final corpus.
        </p>
      )}

      <dl className="masthead__figures">
        <Figure label="Sessions" value={count.format(masthead.sessionCount)} />
        <Figure label="Span" value={formatSpan(masthead.spanStart, masthead.spanEnd)} />
        <Figure label="Repositories" value={count.format(masthead.repositoryCount)} />
        <Figure label="Events" value={count.format(masthead.eventCount)} />
        <Figure label="Tool calls" value={count.format(masthead.toolCallCount)} />
        <Figure
          label="Rule coverage"
          className="masthead__figure--coverage"
          value={
            masthead.ruleCoverage.state === 'notYetAnalyzed' ? (
              'Rules not yet analysed'
            ) : (
              <RuleCoverageBar counts={masthead.ruleCoverage.counts} />
            )
          }
        />
        <Figure label="Subagents" value={count.format(masthead.subagentCount)} />
      </dl>
    </section>
  )
}

function Figure({
  label,
  value,
  className,
}: {
  label: string
  value: ReactNode
  className?: string
}) {
  return (
    <div className={className ? `masthead__figure ${className}` : 'masthead__figure'}>
      <dt className="masthead__label">{label}</dt>
      <dd className="masthead__value">{value}</dd>
    </div>
  )
}

const count = new Intl.NumberFormat('en-GB')

const day = new Intl.DateTimeFormat('en-GB', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
})

/** Scenario 3: an empty store is a designed state. `MastheadCounters.SpanStart` is null exactly when
 * the corpus holds no session, so an absent span is said in words rather than rendered as a dash
 * between two blanks — which would read as a corpus whose dates merely failed to load. */
function formatSpan(start: string | null, end: string | null): string {
  if (start === null || end === null) {
    return 'Nothing ingested yet'
  }

  return `${day.format(new Date(start))} – ${day.format(new Date(end))}`
}
