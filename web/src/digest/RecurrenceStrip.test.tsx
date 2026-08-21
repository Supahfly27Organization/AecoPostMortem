import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { RecurrenceStrip } from './RecurrenceStrip'
import type { Recurrence } from '../api/digest'

/** Scenario 2 (issue #45): "Given a finding touching several sessions, when its row renders, then a
 * recurrence strip shows which sessions it touched, not only how many." */
describe('RecurrenceStrip', () => {
  it('names every session the finding touched, not only the count', () => {
    const recurrence: Recurrence = {
      key: 'src/hot.cs',
      occurrences: [
        { sessionId: 'session-1', ruleSetVersion: null },
        { sessionId: 'session-2', ruleSetVersion: null },
        { sessionId: 'session-3', ruleSetVersion: null },
      ],
    }

    render(
      <MemoryRouter>
        <RecurrenceStrip recurrence={recurrence} />
      </MemoryRouter>,
    )

    const strip = screen.getByRole('list', { name: 'Sessions touched' })
    expect(strip).toHaveTextContent('session-1')
    expect(strip).toHaveTextContent('session-2')
    expect(strip).toHaveTextContent('session-3')
    expect(screen.getAllByRole('listitem')).toHaveLength(3)
  })

  /** Mockup parity item #21: an operator today has to copy a session id and hand-edit the URL bar
   * to reach `/sessions/:sessionId` — each session id here must be a real link to that route. */
  it('links each session id to its session page', () => {
    const recurrence: Recurrence = {
      key: 'src/hot.cs',
      occurrences: [
        { sessionId: 'session-1', ruleSetVersion: null },
        { sessionId: 'session-2', ruleSetVersion: null },
      ],
    }

    render(
      <MemoryRouter>
        <RecurrenceStrip recurrence={recurrence} />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'session-1' })).toHaveAttribute(
      'href',
      '/sessions/session-1',
    )
    expect(screen.getByRole('link', { name: 'session-2' })).toHaveAttribute(
      'href',
      '/sessions/session-2',
    )
  })

  /** Digest session-naming (Slice 2): a session with a resolved label shows that text as the link's
   * own label — never the bare session id an operator would otherwise have to recognise by memory —
   * while the raw session id stays reachable as the link's `title` (tooltip). */
  it('shows a session\'s own resolved label as the link text, with the raw session id as its tooltip', () => {
    const recurrence: Recurrence = {
      key: 'src/hot.cs',
      occurrences: [{ sessionId: 'session-1', ruleSetVersion: null }],
    }

    render(
      <MemoryRouter>
        <RecurrenceStrip
          recurrence={recurrence}
          sessionLabels={{ 'session-1': 'run ef database update for…' }}
        />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: 'run ef database update for…' })
    expect(link).toHaveAttribute('href', '/sessions/session-1')
    expect(link).toHaveAttribute('title', 'session-1')
  })

  /** A session with no resolved label (absent from `sessionLabels`, or no `sessionLabels` prop at
   * all) falls back to the raw session id as its own link text — the pre-Slice-2 behaviour, never a
   * blank or placeholder label. */
  it('falls back to the raw session id when no label resolved for that session', () => {
    const recurrence: Recurrence = {
      key: 'src/hot.cs',
      occurrences: [{ sessionId: 'session-1', ruleSetVersion: null }],
    }

    render(
      <MemoryRouter>
        <RecurrenceStrip recurrence={recurrence} sessionLabels={{}} />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: 'session-1' })
    expect(link).toHaveAttribute('title', 'session-1')
  })
})
