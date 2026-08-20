import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { FindingRow } from './FindingRow'
import type { FindingEnvelope } from '../api/digest'

function waste(overrides: Partial<FindingEnvelope> = {}): FindingEnvelope {
  return {
    kind: 'general',
    class: 'waste',
    provenance: 'derived',
    evidence: [
      { field: 'data.path', value: 'src/hot.cs' },
      { field: 'read_count:session-1', value: '4' },
    ],
    recurrence: {
      key: 'src/hot.cs',
      occurrences: [
        { sessionId: 'session-1', ruleSetVersion: null },
        { sessionId: 'session-2', ruleSetVersion: null },
      ],
    },
    sessionsAffected: 2,
    suggestion: { state: 'present', text: 'Name `rg` instead of repeated `view` calls.' },
    operatorResponse: 'ignored',
    ...overrides,
  } as FindingEnvelope
}

/** Scenario 1 (issue #45): "Given any digest row, when it is expanded, then it shows the evidence
 * quoting the actual event fields, its provenance badge, and a suggestion." Collapsed by default —
 * "expanded" implies there is something to expand into. */
describe('FindingRow', () => {
  it('is collapsed by default, showing no evidence or suggestion yet', () => {
    render(
      <ul>
        <FindingRow finding={waste()} />
      </ul>,
    )

    expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument()
    expect(screen.queryByText('src/hot.cs', { selector: 'dd' })).not.toBeInTheDocument()
    expect(screen.queryByText(/Name `rg`/)).not.toBeInTheDocument()
  })

  // S-36's edge case (issue #44): "a finding touching one session is an anecdote and must be
  // visually subordinate to one touching thirty — that's the ranking's entire purpose, so make the
  // 'sessions affected' count visually prominent, not a small annotation." A count only visible
  // after expanding the row cannot do that job, so it belongs in the always-visible summary.
  it('shows how many sessions it touched without needing to be expanded first', () => {
    render(
      <ul>
        <FindingRow finding={waste({ sessionsAffected: 30 })} />
      </ul>,
    )

    const summary = screen.getByRole('button', { expanded: false })
    const metric = summary.querySelector('[data-rank-metric="sessions-affected"]')

    expect(metric).toHaveTextContent('30')
    expect(metric).toHaveTextContent(/sessions/i)
  })

  it('reads a single-session finding as one session, not as "1 sessions"', () => {
    render(
      <ul>
        <FindingRow finding={waste({ sessionsAffected: 1 })} />
      </ul>,
    )

    const metric = screen
      .getByRole('button', { expanded: false })
      .querySelector('[data-rank-metric="sessions-affected"]')

    expect(metric).toHaveTextContent(/^1\s*session$/i)
  })

  it('expanding the row reveals the quoted evidence, the provenance badge, and the suggestion', async () => {
    const user = userEvent.setup()
    render(
      <ul>
        <FindingRow finding={waste()} />
      </ul>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    expect(screen.getByText('data.path')).toBeInTheDocument()
    expect(screen.getByText('src/hot.cs', { selector: 'dd' })).toBeInTheDocument()
    expect(screen.getByText('Derived')).toBeInTheDocument()
    expect(screen.getByText('Name `rg` instead of repeated `view` calls.')).toBeInTheDocument()
  })

  it('an expanded row also shows the recurrence strip naming the sessions it touched', async () => {
    const user = userEvent.setup()
    render(
      <ul>
        <FindingRow finding={waste()} />
      </ul>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    const strip = screen.getByRole('list', { name: 'Sessions touched' })
    expect(strip).toHaveTextContent('session-1')
    expect(strip).toHaveTextContent('session-2')
  })

  // Scenario 4: a finding with no suggestion template expands anyway.
  it('a finding with no suggestion template still expands, stating that none is offered', async () => {
    const user = userEvent.setup()
    render(
      <ul>
        <FindingRow finding={waste({ suggestion: { state: 'absent' } })} />
      </ul>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    expect(screen.getByText('data.path')).toBeInTheDocument()
    expect(screen.getByText(/no suggestion is offered/i)).toBeInTheDocument()
  })

  it('collapses again on a second click', async () => {
    const user = userEvent.setup()
    render(
      <ul>
        <FindingRow finding={waste()} />
      </ul>,
    )

    const toggle = screen.getByRole('button', { expanded: false })
    await user.click(toggle)
    await user.click(screen.getByRole('button', { expanded: true }))

    expect(screen.queryByText(/no suggestion is offered/i)).not.toBeInTheDocument()
    expect(screen.queryByText('Name `rg` instead of repeated `view` calls.')).not.toBeInTheDocument()
  })
})
