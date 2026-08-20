import { render, screen } from '@testing-library/react'
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

    render(<RecurrenceStrip recurrence={recurrence} />)

    const strip = screen.getByRole('list', { name: 'Sessions touched' })
    expect(strip).toHaveTextContent('session-1')
    expect(strip).toHaveTextContent('session-2')
    expect(strip).toHaveTextContent('session-3')
    expect(screen.getAllByRole('listitem')).toHaveLength(3)
  })
})
