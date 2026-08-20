import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { MonitorComparisonEnvelope } from '../api/monitor'
import { MonitorComparisonBlock } from './MonitorComparisonBlock'

/** The reference edit (FR-39, PRD discovery finding 4): 3 sessions measuring 41.8% before a
 * 2026-05-23 rule edit, 4 sessions measuring 71.7% after it. */
const referenceEdit: MonitorComparisonEnvelope = {
  beforeVersion: {
    repository: 'supahfly27/UpFront',
    hash: '1a47450a',
    firstSessionId: 's1',
    lastSessionId: 's3',
    sessionCount: 3,
  },
  afterVersion: {
    repository: 'supahfly27/UpFront',
    hash: '9579a981',
    firstSessionId: 's4',
    lastSessionId: 's7',
    sessionCount: 4,
  },
  before: {
    ruleVersion: { repository: 'supahfly27/UpFront', hash: '1a47450a' },
    adherent: { operandText: 'rg', layer: 'exactToolName', callCount: 23 },
    divergent: [{ operandText: 'grep', layer: 'exactToolName', callCount: 32 }],
    operands: [
      { operandText: 'rg', layer: 'exactToolName', callCount: 23 },
      { operandText: 'grep', layer: 'exactToolName', callCount: 32 },
    ],
    adherentCalls: 23,
    totalCalls: 55,
    percentage: 41.8,
  },
  after: {
    ruleVersion: { repository: 'supahfly27/UpFront', hash: '9579a981' },
    adherent: { operandText: 'rg', layer: 'exactToolName', callCount: 76 },
    divergent: [{ operandText: 'grep', layer: 'exactToolName', callCount: 30 }],
    operands: [
      { operandText: 'rg', layer: 'exactToolName', callCount: 76 },
      { operandText: 'grep', layer: 'exactToolName', callCount: 30 },
    ],
    adherentCalls: 76,
    totalCalls: 106,
    percentage: 71.7,
  },
}

describe('MonitorComparisonBlock', () => {
  // Scenario 1 (issue #43): "adherence is reported for each under a single stated resolution" —
  // both sides render, each with its own percentage.
  it('renders adherence for both the before and after side', () => {
    render(<MonitorComparisonBlock comparison={referenceEdit} />)

    expect(screen.getByText('41.8%')).toBeInTheDocument()
    expect(screen.getByText('71.7%')).toBeInTheDocument()
  })

  // Scenario 2: "the session count on each side is as visible as the percentage" — the edge case
  // warns against a two-number story (percentages) overwhelming a two-session sample, so both
  // figures share the same prominence marker and CSS class this project already uses for "render
  // this count at display size, not a small annotation" (FindingRow's own sessionsAffected count).
  it('renders each sides session count at the same visual prominence as its percentage', () => {
    render(<MonitorComparisonBlock comparison={referenceEdit} />)

    const percentages = screen.getAllByText(/^\d+(\.\d+)?%$/).filter(
      (element) => element.getAttribute('data-emphasis') === 'prominent',
    )
    const sessionCounts = screen
      .getAllByText(/session/i)
      .filter((element) => element.getAttribute('data-emphasis') === 'prominent')

    expect(percentages).toHaveLength(2)
    expect(sessionCounts).toHaveLength(2)

    // Same emphasis marker, same class name -- neither side's count is a smaller annotation next
    // to a large percentage.
    for (const percentage of percentages) {
      expect(percentage.className).toContain('adherence-figure__percentage')
    }
    for (const count of sessionCounts) {
      expect(count.className).toContain('monitor-comparison__session-count')
    }
  })

  it('names 3 sessions before and 4 sessions after, matching the reference edit', () => {
    render(<MonitorComparisonBlock comparison={referenceEdit} />)

    const beforeSide = screen.getByText('Before').closest('[data-side="before"]')
    const afterSide = screen.getByText('After').closest('[data-side="after"]')

    expect(beforeSide).not.toBeNull()
    expect(afterSide).not.toBeNull()

    expect(within(beforeSide as HTMLElement).getByText('3 sessions')).toBeInTheDocument()
    expect(within(afterSide as HTMLElement).getByText('4 sessions')).toBeInTheDocument()
  })

  it('renders a single session as "1 session", not "1 sessions"', () => {
    render(
      <MonitorComparisonBlock
        comparison={{
          ...referenceEdit,
          beforeVersion: { ...referenceEdit.beforeVersion, sessionCount: 1 },
        }}
      />,
    )

    expect(screen.getByText('1 session')).toBeInTheDocument()
  })
})
