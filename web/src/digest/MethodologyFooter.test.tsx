import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MethodologyFooter } from './MethodologyFooter'
import type { MastheadEnvelope } from '../api/digest'

function mastheadWith(overrides: Partial<MastheadEnvelope> = {}): MastheadEnvelope {
  return {
    sessionCount: 35,
    spanStart: '2026-05-01T00:00:00Z',
    spanEnd: '2026-08-19T00:00:00Z',
    repositoryCount: 3,
    eventCount: 56_138,
    toolCallCount: 12_345,
    ruleCoverage: 'NotYetAnalyzed',
    repositoryScope: {
      selectedRepository: 'aeco/AecoPostMortem',
      availableRepositories: ['aeco/AecoLedger', 'aeco/AecoPostMortem', 'aeco/Upfront'],
      sessionIds: ['session-1', 'session-2', 'session-3'],
    },
    ...overrides,
  }
}

describe('MethodologyFooter', () => {
  it('states the corpus scope actually served, sourced from the masthead', () => {
    render(<MethodologyFooter masthead={mastheadWith()} />)

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent('35 sessions')
    expect(footer).toHaveTextContent('3 repositories')
    expect(footer).toHaveTextContent('56,138')
    expect(footer).toHaveTextContent('12,345')
  })

  it('states that rule text is shown verbatim, as Copilot injected it', () => {
    render(<MethodologyFooter masthead={mastheadWith()} />)

    expect(screen.getByText(/verbatim/i)).toBeInTheDocument()
  })

  it('explains the session strip is chronologically ordered against the repository scope', () => {
    render(<MethodologyFooter masthead={mastheadWith()} />)

    const strip = screen.getByText(/chronological order/i)
    expect(strip).toHaveTextContent('3 sessions')
  })

  it('states an empty corpus has no span, mirroring the masthead', () => {
    render(
      <MethodologyFooter
        masthead={mastheadWith({ spanStart: null, spanEnd: null, sessionCount: 0 })}
      />,
    )

    expect(screen.getByText(/no span yet/i)).toBeInTheDocument()
    expect(screen.getByRole('contentinfo')).toHaveTextContent('0 sessions')
  })

  it('singularises a scope of exactly one session, without an "1 sessions" typo', () => {
    render(
      <MethodologyFooter
        masthead={mastheadWith({
          sessionCount: 1,
          repositoryScope: {
            selectedRepository: 'aeco/AecoPostMortem',
            availableRepositories: ['aeco/AecoPostMortem'],
            sessionIds: ['session-1'],
          },
        })}
      />,
    )

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent('1 session ')
    expect(footer).not.toHaveTextContent('1 sessions')
  })
})
