import { useState } from 'react'
import './DateRangeFilter.css'

interface DateRangeFilterProps {
  /** The currently active filter, `null` when unset — mirrors `useDigest`'s own `from`/`to` state,
   * both plain `yyyy-MM-dd` calendar dates matching `<input type="date">`'s own value format and
   * `ApiHost.FromParameter`/`ToParameter`'s `DateOnly` query parameters. */
  from: string | null
  to: string | null
  /** Reports the range the operator asked for — never called until the operator submits, so typing
   * into either field does not itself trigger a re-fetch of a corpus this large mid-keystroke. */
  onApply: (from: string | null, to: string | null) => void
}

/**
 * The Process Digest's date-range filter (pager & date-range filter task): re-scopes the whole
 * ranked-findings analysis to sessions whose own `StartedAt` falls in range — see the "A date-range
 * filter re-scopes the whole analysis" non-obvious decision in `AecoPostMortem.Api/CLAUDE.md`. This
 * component only collects the two bounds and reports them on submit; `DigestPage` owns turning that
 * into a re-fetch via `useDigest`.
 *
 * Both bounds are independent — an operator may filter with only a `from`, only a `to`, or both.
 * `role="search"` names the whole control as one reachable group, the same "one named group" pattern
 * `Masthead`'s own `role="group"` establishes for the corpus scope above it.
 */
export function DateRangeFilter({ from, to, onApply }: DateRangeFilterProps) {
  const [pendingFrom, setPendingFrom] = useState(from ?? '')
  const [pendingTo, setPendingTo] = useState(to ?? '')

  const active = from !== null || to !== null

  function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    onApply(pendingFrom === '' ? null : pendingFrom, pendingTo === '' ? null : pendingTo)
  }

  function clear() {
    setPendingFrom('')
    setPendingTo('')
    onApply(null, null)
  }

  return (
    <form
      className="date-range-filter"
      role="search"
      aria-label="Date range"
      onSubmit={submit}
    >
      <label className="date-range-filter__field" htmlFor="date-range-filter-from">
        From
        <input
          id="date-range-filter-from"
          type="date"
          value={pendingFrom}
          onChange={(event) => setPendingFrom(event.target.value)}
        />
      </label>
      <label className="date-range-filter__field" htmlFor="date-range-filter-to">
        To
        <input
          id="date-range-filter-to"
          type="date"
          value={pendingTo}
          onChange={(event) => setPendingTo(event.target.value)}
        />
      </label>
      <button type="submit">Apply</button>
      {active && (
        <button type="button" onClick={clear}>
          Clear
        </button>
      )}
    </form>
  )
}
