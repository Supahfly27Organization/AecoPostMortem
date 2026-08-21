import type { MastheadEnvelope } from '../api/digest'
import './MethodologyFooter.css'

const count = new Intl.NumberFormat('en-GB')

const day = new Intl.DateTimeFormat('en-GB', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
})

/** Mirrors `Masthead.tsx`'s own `formatSpan` — an empty corpus has no span, said in words rather
 * than a dash between two blanks. Kept local rather than imported: `Masthead.tsx` does not export
 * it, and this story's own scope note keeps that file untouched. */
function formatSpan(start: string | null, end: string | null): string {
  if (start === null || end === null) {
    return 'no span yet'
  }

  return `${day.format(new Date(start))} to ${day.format(new Date(end))}`
}

function plural(n: number, singular: string, pluralForm = `${singular}s`): string {
  return `${count.format(n)} ${n === 1 ? singular : pluralForm}`
}

/**
 * Mockup parity item #9 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`,
 * "Methodology footer"): the mockup's own footer states what was measured and how the recurrence
 * strip's positions are sourced. The mockup's real copy is one fixed set of numbers for one frozen
 * date (`~/.copilot/` on 2026-08-16) — this component states the same three things, but every
 * figure is read straight off the `MastheadEnvelope` this page already fetched, the same
 * "nothing on this page counts anything" discipline `Masthead.tsx` documents for its own figures.
 * No absent-data caveat paragraph here: this app's findings are all real, not mockup placeholders.
 */
export function MethodologyFooter({ masthead }: { masthead: MastheadEnvelope }) {
  const sessionsInScope = masthead.repositoryScope.sessionIds.length

  return (
    <footer className="methodology-footer">
      <p>
        This digest was measured from {plural(masthead.sessionCount, 'session')} across{' '}
        {plural(masthead.repositoryCount, 'repository', 'repositories')} (
        {formatSpan(masthead.spanStart, masthead.spanEnd)}) — {count.format(masthead.eventCount)}{' '}
        events and {count.format(masthead.toolCallCount)} tool calls, read from this machine&rsquo;s
        own store. Every figure here is a served count, never recomputed on this page.
      </p>
      <p>
        Rule text shown anywhere in this digest is verbatim — exactly as Copilot injected it into the
        session, with no paraphrasing or normalisation.
      </p>
      <p>
        Each finding&rsquo;s session strip lays out the {plural(sessionsInScope, 'session')} in the
        selected repository in chronological order, and lights the ones that finding actually
        touched — the same session set every check on this page was run over.
      </p>
    </footer>
  )
}
