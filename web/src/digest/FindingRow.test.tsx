import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { FindingRow } from './FindingRow'
import type { FindingEnvelope } from '../api/digest'

function waste(overrides: Partial<FindingEnvelope> = {}): FindingEnvelope {
  return {
    kind: 'general',
    class: 'waste',
    provenance: 'derived',
    headline: 'src/hot.cs was read 4 times across 1 session.',
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

/** FR-33's worked shape (S-24, issue #38): an adherence finding served with its figure — "prefer
 * `rg` over shell search", two operands resolved through two different layers. */
function adherence(): FindingEnvelope {
  return {
    ...waste(),
    kind: 'adherence',
    class: 'ruleAdherenceToolChoice',
    figure: {
      ruleVersion: { repository: 'AecoPostMortem', hash: 'b3f1c0' },
      adherent: { operandText: 'rg', layer: 'exactToolName', callCount: 3 },
      divergent: [{ operandText: 'Shell', layer: 'derivedRole', callCount: 1 }],
      operands: [
        { operandText: 'rg', layer: 'exactToolName', callCount: 3 },
        { operandText: 'Shell', layer: 'derivedRole', callCount: 1 },
      ],
      adherentCalls: 3,
      totalCalls: 4,
      percentage: 75,
    },
  } as FindingEnvelope
}

/** Scenario 1 (issue #45): "Given any digest row, when it is expanded, then it shows the evidence
 * quoting the actual event fields, its provenance badge, and a suggestion." Collapsed by default —
 * "expanded" implies there is something to expand into. */
describe('FindingRow', () => {
  it('is collapsed by default, showing no evidence or suggestion yet', () => {
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste()} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
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
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste({ sessionsAffected: 30 })} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    const summary = screen.getByRole('button', { expanded: false })
    const metric = summary.querySelector('[data-rank-metric="sessions-affected"]')

    expect(metric).toHaveTextContent('30')
    expect(metric).toHaveTextContent(/sessions/i)
  })

  it('reads a single-session finding as one session, not as "1 sessions"', () => {
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste({ sessionsAffected: 1 })} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    const metric = screen
      .getByRole('button', { expanded: false })
      .querySelector('[data-rank-metric="sessions-affected"]')

    expect(metric).toHaveTextContent(/^1\s*session$/i)
  })

  it('expanding the row reveals the quoted evidence, the provenance badge, and the suggestion', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste()} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
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
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste()} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    const strip = screen.getByRole('list', { name: 'Sessions touched' })
    expect(strip).toHaveTextContent('session-1')
    expect(strip).toHaveTextContent('session-2')
  })

  /** Digest session-naming (Slice 2): `sessionLabels` passed to `FindingRow` reaches the recurrence
   * strip unchanged, so a resolved label (not the bare session id) is what an operator sees there. */
  it('threads sessionLabels through to the recurrence strip', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ul>
        <FindingRow
          finding={waste()}
          sessionIds={['session-1', 'session-2']}
          sessionLabels={{ 'session-1': 'run ef database update for…' }}
        />
      </ul>
      </MemoryRouter>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    expect(screen.getByRole('link', { name: 'run ef database update for…' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'session-2' })).toBeInTheDocument()
  })

  // Scenario 4: a finding with no suggestion template expands anyway.
  it('a finding with no suggestion template still expands, stating that none is offered', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste({ suggestion: { state: 'absent' } })} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    expect(screen.getByText('data.path')).toBeInTheDocument()
    expect(screen.getByText(/no suggestion is offered/i)).toBeInTheDocument()
  })

  // S-24 / FR-33, Scenario 1: "Given any adherence figure, when it is displayed, then the layer used
  // per operand and the resulting call counts are shown with it." The row is where a figure is
  // displayed, and it delegates the whole figure — percentage and resolution together — to
  // AdherenceFigureBlock rather than rendering the number itself.
  it('an adherence row shows the figure with the layer and call count for every operand', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={adherence()} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    expect(screen.getByText('75%')).toBeInTheDocument()

    const resolution = screen.getByRole('table', { name: /resolution/i })
    expect(resolution).toHaveTextContent('rg')
    expect(resolution).toHaveTextContent('Exact tool name')
    expect(resolution).toHaveTextContent('Shell')
    expect(resolution).toHaveTextContent('Derived role')
  })

  // A non-adherence finding has no figure on the wire at all, so there is nothing for this row to
  // render — and no placeholder percentage it could invent.
  it('a non-adherence row shows no figure and no resolution table', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste()} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    expect(screen.queryByRole('table', { name: /resolution/i })).not.toBeInTheDocument()
  })

  // The collapsed summary is deliberately figure-free: showing the percentage on a row that has not
  // been expanded would put a bare number on the page with its resolution one click away, which is
  // the exact reading FR-33 exists to prevent.
  it('does not show the percentage until the row is expanded, so it never appears without its resolution', () => {
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={adherence()} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    expect(screen.queryByText('75%')).not.toBeInTheDocument()
    expect(screen.queryByRole('table', { name: /resolution/i })).not.toBeInTheDocument()
  })

  it('collapses again on a second click', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste()} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    const toggle = screen.getByRole('button', { expanded: false })
    await user.click(toggle)
    await user.click(screen.getByRole('button', { expanded: true }))

    expect(screen.queryByText(/no suggestion is offered/i)).not.toBeInTheDocument()
    expect(screen.queryByText('Name `rg` instead of repeated `view` calls.')).not.toBeInTheDocument()
  })

  // Mockup parity item #2: the per-finding session strip is visible on the collapsed row, not
  // behind expansion the way the recurrence strip is — the whole point is scanning many rows at
  // once without opening any of them.
  it('shows the session strip on the collapsed row', () => {
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste()} sessionIds={['session-1', 'session-2', 'session-3']} />
      </ul>
      </MemoryRouter>,
    )

    const summary = screen.getByRole('button', { expanded: false })
    const strip = summary.querySelector('[role="img"]')

    expect(strip).toHaveAttribute('aria-label', '2 of 3 sessions affected')
  })

  // FR-48 (issue #52, S-42): `Findings.ProcessDigest.InferredFindings` is deliberately never ranked
  // by `sessionsAffected` — `Findings/CLAUDE.md` says applying that figure to a hypothesis "would
  // dress the hypothesis up with the same measured-looking number that ranks Observed and Derived
  // findings." `variant="unranked"` is how a caller (the digest's own "Judgment calls" section) tells
  // this row not to render the same leading rank-metric column a ranked finding gets.
  it('an unranked-variant row omits the leading sessions-affected rank metric', () => {
    render(
      <MemoryRouter>
        <ul>
        <FindingRow
          finding={waste({ provenance: 'inferred', sessionsAffected: 12 })}
          sessionIds={['session-1', 'session-2']}
          variant="unranked"
        />
      </ul>
      </MemoryRouter>,
    )

    const summary = screen.getByRole('button', { expanded: false })
    expect(summary.querySelector('[data-rank-metric="sessions-affected"]')).not.toBeInTheDocument()
    // Mockup parity item #5: the visible label is `finding.headline` — a full written sentence
    // naming the problem — never the bare `recurrence.key` ('src/hot.cs' alone, with no sentence
    // around it); the extra words here are only reachable through the headline field.
    expect(summary).toHaveTextContent('src/hot.cs was read 4 times across 1 session.')
  })

  it('the default variant still shows the rank metric, unchanged from before this prop existed', () => {
    render(
      <MemoryRouter>
        <ul>
        <FindingRow finding={waste({ sessionsAffected: 12 })} sessionIds={['session-1', 'session-2']} />
      </ul>
      </MemoryRouter>,
    )

    const summary = screen.getByRole('button', { expanded: false })
    expect(summary.querySelector('[data-rank-metric="sessions-affected"]')).toBeInTheDocument()
  })

  // Nothing is actually lost by omitting the rank metric: expanding an unranked row still shows the
  // recurrence strip naming every session it touched (the count is just how many `<li>`s that is).
  it('an unranked row still names every session it touched once expanded', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ul>
        <FindingRow
          finding={waste({ provenance: 'inferred' })}
          sessionIds={['session-1', 'session-2']}
          variant="unranked"
        />
      </ul>
      </MemoryRouter>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    const strip = screen.getByRole('list', { name: 'Sessions touched' })
    expect(strip).toHaveTextContent('session-1')
    expect(strip).toHaveTextContent('session-2')
  })
})
