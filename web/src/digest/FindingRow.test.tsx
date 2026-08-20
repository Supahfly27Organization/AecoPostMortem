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
