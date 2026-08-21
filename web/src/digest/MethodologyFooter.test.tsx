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
    subagentCount: 470,
    ruleCoverage: { state: 'notYetAnalyzed' },
    repositoryScope: {
      selectedRepository: 'aeco/AecoPostMortem',
      availableRepositories: ['aeco/AecoLedger', 'aeco/AecoPostMortem', 'aeco/Upfront'],
      sessionIds: ['session-1', 'session-2', 'session-3'],
      sessionLabels: {},
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
            sessionLabels: {},
          },
        })}
      />,
    )

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent('1 session ')
    expect(footer).not.toHaveTextContent('1 sessions')
  })

  // Code review Important #2: under a date filter, the corpus-wide "measured from 35 sessions"
  // sentence is still true as a corpus-wide fact, but it left the page with no statement anywhere
  // of what the *ranking itself* actually covers — 16 of 35, not 35. `range` states that scope
  // explicitly once a filter is active.
  it('states the scope actually ranked when a date range is applied, distinct from the corpus-wide count', () => {
    render(
      <MethodologyFooter
        masthead={mastheadWith({
          repositoryScope: {
            selectedRepository: 'aeco/AecoPostMortem',
            availableRepositories: ['aeco/AecoPostMortem'],
            sessionIds: ['session-1', 'session-2'],
            sessionLabels: {},
          },
        })}
        range={{ from: '2026-06-01', to: '2026-06-30' }}
      />,
    )

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent('Ranked over 2 of 35 sessions')
    expect(footer).toHaveTextContent('1 Jun 2026')
    expect(footer).toHaveTextContent('30 Jun 2026')
  })

  it('renders no range-specific sentence at all when no date filter is applied', () => {
    render(<MethodologyFooter masthead={mastheadWith()} />)

    expect(screen.queryByText(/ranked over/i)).not.toBeInTheDocument()
  })

  it("the session strip's own sentence names the applied range, not only the repository", () => {
    render(<MethodologyFooter masthead={mastheadWith()} range={{ from: '2026-06-01', to: null }} />)

    expect(screen.getByText(/within the applied date range/i)).toBeInTheDocument()
  })

  it('a range with only one bound still states it, e.g. "from 1 Jun 2026 onward"', () => {
    render(<MethodologyFooter masthead={mastheadWith()} range={{ from: '2026-06-01', to: null }} />)

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent('Ranked over 3 of 35 sessions')
    expect(footer).toHaveTextContent(/from 1 jun 2026 onward/i)
  })
})
