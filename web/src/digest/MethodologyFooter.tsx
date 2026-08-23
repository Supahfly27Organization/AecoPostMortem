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

/** The date-range filter's own optional bounds, mirroring `api/digest.ts`'s `DateRange` — kept as a
 * separate, structurally identical type rather than importing it, so this file does not need to
 * import from `api/digest.ts` just for one shape (`DigestPage.tsx` already imports both). */
interface AppliedRange {
  from: string | null
  to: string | null
}

/** Code review Important #2: states the applied window in words, both bounds independent — a bare
 * `to`-only range reads "through", a bare `from`-only range reads "from … onward", and both present
 * reads as a plain span, the same "to" join `formatSpan` already uses for the corpus-wide span. */
function formatRange(range: AppliedRange): string {
  if (range.from !== null && range.to !== null) {
    return `${day.format(new Date(range.from))} to ${day.format(new Date(range.to))}`
  }
  if (range.from !== null) {
    return `from ${day.format(new Date(range.from))} onward`
  }
  return `through ${day.format(new Date(range.to!))}`
}

/**
 * Mockup parity item #9 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`,
 * "Methodology footer"): the mockup's own footer states what was measured and how the recurrence
 * strip's positions are sourced. The mockup's real copy is one fixed set of numbers for one frozen
 * date (`~/.copilot/` on 2026-08-16) — this component states the same three things, but every
 * figure is read straight off the `MastheadEnvelope` this page already fetched, the same
 * "nothing on this page counts anything" discipline `Masthead.tsx` documents for its own figures.
 * No absent-data caveat paragraph here: this app's findings are all real, not mockup placeholders.
 *
 * `range` (the pager & date-range filter task, code review Important #2): the corpus-wide "measured
 * from N sessions" sentence below stays true whether or not a date filter is active — it is a fact
 * about the corpus, per `AecoPostMortem.Api/CLAUDE.md`'s "A date-range filter re-scopes the whole
 * analysis" decision that `MastheadCounters` ignores the filter — but that sentence alone left the
 * page with no statement of what the *ranking itself* actually covers once a filter narrows it. When
 * `range` is non-null (an active filter, passed by `DigestPage`), a second sentence states the real
 * scope: how many of the corpus-wide sessions the ranking is actually over, and the applied window.
 * `null` (the default when no filter is active) renders neither this sentence nor the "within the
 * applied date range" clause on the session-strip sentence below — this component adds no new prose
 * at all for the unfiltered case, byte-for-byte the same as before this parameter existed.
 */
export function MethodologyFooter({
  masthead,
  range = null,
}: {
  masthead: MastheadEnvelope
  range?: AppliedRange | null
}) {
  const sessionsInScope = masthead.repositoryScope.sessionIds.length
  // The repository filter: while `RepositorySelector` was display-only, the digest could only ever
  // be one repository and "the selected repository" was unambiguous. Selecting now genuinely
  // re-scopes the ranking, so this sentence names which one — read off the served scope already in
  // hand rather than taken as a new prop, the same "nothing on this page counts or is told anything
  // twice" discipline the figures above follow. Null (no session in the store records a repository
  // at all) keeps the original unnamed wording, since there is no name to give.
  const repository = masthead.repositoryScope.selectedRepository

  return (
    <footer className="methodology-footer">
      <p>
        This digest was measured from {plural(masthead.sessionCount, 'session')} across{' '}
        {plural(masthead.repositoryCount, 'repository', 'repositories')} (
        {formatSpan(masthead.spanStart, masthead.spanEnd)}) — {count.format(masthead.eventCount)}{' '}
        events and {count.format(masthead.toolCallCount)} tool calls, read from this machine&rsquo;s
        own store. Every figure here is a served count, never recomputed on this page.
      </p>
      {range !== null && (
        <p>
          Ranked over {sessionsInScope} of {count.format(masthead.sessionCount)} sessions,{' '}
          {formatRange(range)} — a date filter re-scopes the whole ranking to just those sessions,
          it does not merely hide rows computed against the full corpus above.
        </p>
      )}
      <p>
        Rule text shown anywhere in this digest is verbatim — exactly as Copilot injected it into the
        session, with no paraphrasing or normalisation.
      </p>
      <p>
        Each finding&rsquo;s session strip lays out the {plural(sessionsInScope, 'session')} in{' '}
        {repository ?? 'the selected repository'}
        {range !== null && ' within the applied date range'} in chronological order, and lights the
        ones that finding actually touched — the same session set every check on this page was run
        over.
      </p>
    </footer>
  )
}
