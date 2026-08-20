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
      <ul>
        <FindingRow finding={waste()} />
      </ul>,
    )

    expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument()
    expect(screen.queryByText('src/hot.cs', { selector: 'dd' })).not.toBeInTheDocument()
    expect(screen.queryByText(/Name `rg`/)).not.toBeInTheDocument()
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

  // S-24 / FR-33, Scenario 1: "Given any adherence figure, when it is displayed, then the layer used
  // per operand and the resulting call counts are shown with it." The row is where a figure is
  // displayed, and it delegates the whole figure — percentage and resolution together — to
  // AdherenceFigureBlock rather than rendering the number itself.
  it('an adherence row shows the figure with the layer and call count for every operand', async () => {
    const user = userEvent.setup()
    render(
      <ul>
        <FindingRow finding={adherence()} />
      </ul>,
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
      <ul>
        <FindingRow finding={waste()} />
      </ul>,
    )

    await user.click(screen.getByRole('button', { expanded: false }))

    expect(screen.queryByRole('table', { name: /resolution/i })).not.toBeInTheDocument()
  })

  // The collapsed summary is deliberately figure-free: showing the percentage on a row that has not
  // been expanded would put a bare number on the page with its resolution one click away, which is
  // the exact reading FR-33 exists to prevent.
  it('does not show the percentage until the row is expanded, so it never appears without its resolution', () => {
    render(
      <ul>
        <FindingRow finding={adherence()} />
      </ul>,
    )

    expect(screen.queryByText('75%')).not.toBeInTheDocument()
    expect(screen.queryByRole('table', { name: /resolution/i })).not.toBeInTheDocument()
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
