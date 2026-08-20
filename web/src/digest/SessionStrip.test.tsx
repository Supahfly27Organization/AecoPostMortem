import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SessionStrip } from './SessionStrip'
import type { RecurrenceOccurrence } from '../api/digest'

function occurrences(...sessionIds: string[]): RecurrenceOccurrence[] {
  return sessionIds.map((sessionId) => ({ sessionId, ruleSetVersion: null }))
}

/** Mockup parity item #2 (docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md): a
 * small horizontal bar, one cell per session in the currently scoped corpus, lit where this
 * finding's own occurrences touched that session — visible on the collapsed row, without expanding. */
describe('SessionStrip', () => {
  it('renders one cell per session in the scope, in the scope\'s own order', () => {
    render(<SessionStrip sessionIds={['s1', 's2', 's3']} occurrences={occurrences('s1')} />)

    const strip = screen.getByRole('img', { name: '1 of 3 sessions affected' })
    expect(strip.children).toHaveLength(3)
  })

  it('lights exactly the cells this finding touched, by position', () => {
    render(<SessionStrip sessionIds={['s1', 's2', 's3', 's4']} occurrences={occurrences('s2', 's4')} />)

    const cells = screen.getByRole('img', { name: '2 of 4 sessions affected' }).children
    expect(cells[0]).not.toHaveClass('session-strip__cell--on')
    expect(cells[1]).toHaveClass('session-strip__cell--on')
    expect(cells[2]).not.toHaveClass('session-strip__cell--on')
    expect(cells[3]).toHaveClass('session-strip__cell--on')
  })

  // A finding's own occurrences are always a subset of the scope (RepositoryScope.sessionIds
  // documents this), but the strip must not crash or double-count if a session id it is handed
  // isn't in the scope's list at all — it simply cannot be positioned, so it lights nothing extra.
  it('does not fail when an occurrence names a session outside the given scope', () => {
    render(<SessionStrip sessionIds={['s1', 's2']} occurrences={occurrences('s1', 'not-in-scope')} />)

    // The out-of-scope occurrence cannot be positioned, so it neither crashes the render nor
    // inflates the "of N" denominator beyond the scope's own length.
    const strip = screen.getByRole('img', { name: '1 of 2 sessions affected' })
    expect(strip.children).toHaveLength(2)
  })

  it('renders nothing lit when the finding touched none of the scope', () => {
    render(<SessionStrip sessionIds={['s1', 's2']} occurrences={[]} />)

    const cells = screen.getByRole('img', { name: '0 of 2 sessions affected' }).children
    expect(cells[0]).not.toHaveClass('session-strip__cell--on')
    expect(cells[1]).not.toHaveClass('session-strip__cell--on')
  })

  it('renders no strip at all when the scope has no sessions', () => {
    render(<SessionStrip sessionIds={[]} occurrences={[]} />)

    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })
})
