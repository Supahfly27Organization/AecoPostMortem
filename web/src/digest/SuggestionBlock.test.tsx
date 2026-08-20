import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SuggestionBlock } from './SuggestionBlock'

describe('SuggestionBlock', () => {
  it('renders the suggestion text when one is present', () => {
    render(<SuggestionBlock suggestion={{ state: 'present', text: 'Name `rg` instead of `grep`.' }} />)

    expect(screen.getByText('Name `rg` instead of `grep`.')).toBeInTheDocument()
  })

  // Scenario 4 (issue #45): "Given a finding whose class has no suggestion template, when its row
  // is expanded, then it shows its evidence and states that no suggestion is offered" — reusing
  // SuggestionEnvelope's existing `absent` state, never a blank suggestion area.
  it('states explicitly that no suggestion is offered, rather than rendering nothing', () => {
    render(<SuggestionBlock suggestion={{ state: 'absent' }} />)

    expect(screen.getByText(/no suggestion is offered/i)).toBeInTheDocument()
  })
})
