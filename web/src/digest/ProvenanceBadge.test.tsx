import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProvenanceBadge } from './ProvenanceBadge'

/** Scenario 1 (issue #45): an expanded row shows its provenance badge — PRD §3.8's three levels
 * must render distinguishably. */
describe('ProvenanceBadge', () => {
  it.each([
    ['observed', 'Observed'],
    ['derived', 'Derived'],
    ['inferred', 'Inferred'],
  ] as const)('names the %s level as "%s"', (provenance, label) => {
    render(<ProvenanceBadge provenance={provenance} />)

    expect(screen.getByText(label)).toBeInTheDocument()
  })

  it('carries the raw provenance value as a data attribute, distinct per level', () => {
    render(<ProvenanceBadge provenance="inferred" />)

    expect(screen.getByText('Inferred')).toHaveAttribute('data-provenance', 'inferred')
  })
})
