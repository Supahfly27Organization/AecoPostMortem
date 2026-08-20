import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Masthead } from './Masthead'
import type { DigestState, MastheadEnvelope } from '../api/digest'

function counters(overrides: Partial<MastheadEnvelope> = {}): MastheadEnvelope {
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
      availableRepositories: ['aeco/AecoPostMortem'],
    },
    ...overrides,
  }
}

function renderMasthead(masthead: MastheadEnvelope, state: DigestState = 'Analyzed') {
  return render(<Masthead masthead={masthead} state={state} />)
}

/** The value rendered against a figure's own label, so each assertion names the figure it is
 * about rather than matching a bare number that could belong to any of the six. */
function figure(label: string) {
  return screen.getByText(label, { selector: 'dt' }).nextElementSibling
}

describe('Masthead', () => {
  // Scenario 2: "it shows sessions, span, repositories, events, tool calls and rule coverage".
  it('states the corpus scope: sessions, span, repositories, events and tool calls', () => {
    renderMasthead(counters())

    expect(figure('Sessions')).toHaveTextContent('35')
    expect(figure('Repositories')).toHaveTextContent('3')
    expect(figure('Events')).toHaveTextContent('56,138')
    expect(figure('Tool calls')).toHaveTextContent('12,345')
    expect(figure('Span')).toHaveTextContent('2026')
  })

  // Scenario 5: rule coverage is honest before rules are analysed.
  it('reads "rules not yet analysed" rather than a zero violation count', () => {
    renderMasthead(counters())

    expect(figure('Rule coverage')).toHaveTextContent(/rules not yet analysed/i)
    expect(screen.getByRole('group', { name: /corpus scope/i })).not.toHaveTextContent(
      /0 violations|no violations/i,
    )
  })

  // Scenario 4: a session still being ingested is a designed state — the counts are real, but they
  // are not final, and the masthead has to say so rather than letting them read as the whole corpus.
  it('states that analysis is incomplete rather than presenting mid-ingest counts as final', () => {
    renderMasthead(counters(), 'Incomplete')

    expect(screen.getByText(/still under way/i)).toBeInTheDocument()
    expect(screen.getByRole('group', { name: /corpus scope/i })).toHaveAttribute(
      'data-provisional',
      'true',
    )
  })

  it('does not mark its counts provisional once ingestion has finished', () => {
    renderMasthead(counters(), 'Analyzed')

    expect(screen.queryByText(/still under way/i)).not.toBeInTheDocument()
    expect(screen.getByRole('group', { name: /corpus scope/i })).toHaveAttribute(
      'data-provisional',
      'false',
    )
  })

  // Scenario 3: an empty store is a designed state — an empty corpus genuinely has no span, and
  // saying so beats rendering a blank or a fabricated range between two absent dates.
  it('says an empty corpus has no span instead of rendering an empty date range', () => {
    renderMasthead(
      counters({
        sessionCount: 0,
        spanStart: null,
        spanEnd: null,
        repositoryCount: 0,
        eventCount: 0,
        toolCallCount: 0,
      }),
      'NotYetAnalyzed',
    )

    expect(figure('Span')).toHaveTextContent(/nothing ingested yet/i)
  })
})
