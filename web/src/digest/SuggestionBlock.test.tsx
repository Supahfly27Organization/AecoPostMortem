import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SuggestionBlock } from './SuggestionBlock'

describe('SuggestionBlock', () => {
  it('renders the suggestion text when one is present', () => {
    render(<SuggestionBlock suggestion={{ state: 'present', text: 'Name `rg` instead of `grep`.' }} />)

    expect(screen.getByText('Name `rg` instead of `grep`.')).toBeInTheDocument()
  })

  // Mockup parity item #3 (docs/product-superpowers/prioritization/2026-08-21-mockup-parity-gaps.md):
  // the mockup's `.sug` box carries a small uppercase `Suggested change` label above the sentence.
  it('labels a present suggestion "Suggested change"', () => {
    render(<SuggestionBlock suggestion={{ state: 'present', text: 'Name `rg` instead of `grep`.' }} />)

    expect(screen.getByText('Suggested change')).toBeInTheDocument()
  })

  // Scenario 4 (issue #45): "Given a finding whose class has no suggestion template, when its row
  // is expanded, then it shows its evidence and states that no suggestion is offered" — reusing
  // SuggestionEnvelope's existing `absent` state, never a blank suggestion area.
  it('states explicitly that no suggestion is offered, rather than rendering nothing', () => {
    render(<SuggestionBlock suggestion={{ state: 'absent' }} />)

    expect(screen.getByText(/no suggestion is offered/i)).toBeInTheDocument()
  })

  // A "Suggested change" label above "No suggestion is offered." would read as self-contradictory
  // (a heading promising a change directly over a sentence saying there isn't one) — the mockup
  // never depicts an absent-suggestion box at all, so this state stays label-free, same as before.
  it('does not label the absent state', () => {
    render(<SuggestionBlock suggestion={{ state: 'absent' }} />)

    expect(screen.queryByText('Suggested change')).not.toBeInTheDocument()
  })
})
