import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { AdherenceFigure } from '../api/digest'
import { AdherenceFigureBlock } from './AdherenceFigureBlock'

/** FR-33's worked shape: "prefer `rg` over shell search", 3 adherent calls against 1 divergent —
 * two operands that resolved through two different layers, which is the whole point of showing the
 * layer per operand rather than one label for the figure. */
const preferRgOverShell: AdherenceFigure = {
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
}

describe('AdherenceFigureBlock', () => {
  // Scenario 1 (issue #38): "Given any adherence figure, when it is displayed, then the layer used
  // per operand and the resulting call counts are shown with it."
  it('shows the layer used and the call count for every operand, beside the percentage', () => {
    render(<AdherenceFigureBlock figure={preferRgOverShell} />)

    expect(screen.getByText('75%')).toBeInTheDocument()

    const resolution = screen.getByRole('table', { name: /resolution/i })

    const adherentRow = within(resolution).getByRole('row', { name: /rg/ })
    expect(within(adherentRow).getByText(/exact tool name/i)).toBeInTheDocument()
    expect(within(adherentRow).getByText('3')).toBeInTheDocument()

    const divergentRow = within(resolution).getByRole('row', { name: /Shell/ })
    expect(within(divergentRow).getByText(/derived role/i)).toBeInTheDocument()
    expect(within(divergentRow).getByText('1')).toBeInTheDocument()
  })

  it('names the rule-set version the figure was computed within', () => {
    render(<AdherenceFigureBlock figure={preferRgOverShell} />)

    expect(screen.getByText(/b3f1c0/)).toBeInTheDocument()
  })

  // The UI half of the same refusal the response contract enforces: the percentage is rendered by
  // this component and only this component, so there is no code path that puts a percentage on the
  // page without the operand table beside it. A layer that stopped rendering would have to delete
  // the figure too.
  it('renders no percentage anywhere without the operand table that produced it', () => {
    const { container } = render(<AdherenceFigureBlock figure={preferRgOverShell} />)

    expect(container.textContent).toContain('75%')
    expect(screen.getByRole('table', { name: /resolution/i })).toBeInTheDocument()
  })

  // PRD §5.5 tolerates zero occurrences: the figure still ships its resolution, and says plainly
  // that there is no percentage rather than rendering "0%" of nothing.
  it('states that no calls were observed rather than showing 0% when the rule had none', () => {
    render(
      <AdherenceFigureBlock
        figure={{
          ...preferRgOverShell,
          adherent: { ...preferRgOverShell.adherent, callCount: 0 },
          divergent: [{ operandText: 'Shell', layer: 'derivedRole', callCount: 0 }],
          operands: [
            { operandText: 'rg', layer: 'exactToolName', callCount: 0 },
            { operandText: 'Shell', layer: 'derivedRole', callCount: 0 },
          ],
          adherentCalls: 0,
          totalCalls: 0,
          percentage: null,
        }}
      />,
    )

    expect(screen.queryByText('0%')).not.toBeInTheDocument()
    expect(screen.getByText(/no calls were observed/i)).toBeInTheDocument()
    expect(screen.getByRole('table', { name: /resolution/i })).toBeInTheDocument()
  })

  // FR-31's fourth layer is reported, never silently dropped — an operand nothing matched still
  // occupies a row, so the denominator is readable rather than quietly short.
  it('shows an unresolved operand as its own row rather than omitting it', () => {
    render(
      <AdherenceFigureBlock
        figure={{
          ...preferRgOverShell,
          divergent: [{ operandText: 'ack', layer: 'unresolved', callCount: 0 }],
          operands: [
            { operandText: 'rg', layer: 'exactToolName', callCount: 3 },
            { operandText: 'ack', layer: 'unresolved', callCount: 0 },
          ],
          totalCalls: 3,
          percentage: 100,
        }}
      />,
    )

    const unresolvedRow = screen.getByRole('row', { name: /ack/ })
    expect(within(unresolvedRow).getByText(/unresolved/i)).toBeInTheDocument()
  })
})
