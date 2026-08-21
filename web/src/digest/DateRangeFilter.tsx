import { useState } from 'react'
import './DateRangeFilter.css'

interface DateRangeFilterProps {
  /** The currently active filter, `null` when unset — mirrors `useDigest`'s own `from`/`to` state,
   * both plain `yyyy-MM-dd` calendar dates matching `<input type="date">`'s own value format and
   * `ApiHost.FromParameter`/`ToParameter`'s `DateOnly` query parameters. */
  from: string | null
  to: string | null
  /** Reports the range the operator asked for — never called until the operator submits, so typing
   * into either field does not itself trigger a re-fetch of a corpus this large mid-keystroke. Never
   * called for an inverted range at all — see the module doc comment. */
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
 *
 * **Code review Critical #1**: the server correctly answers 400 for an inverted range
 * (`from` after `to`), but nothing previously stopped an operator from sending one — a two-click
 * mistake reached a dead-end page (the fetch failure collapsed into `useDigest`'s generic error
 * state, which unmounts this very control, with no way back except a full reload). This component
 * now refuses to submit an inverted range at all: `submit` compares the two pending values as plain
 * ISO `yyyy-MM-dd` strings (lexicographic order matches calendar order for that format) and renders
 * an inline `role="alert"` instead of calling `onApply`, so the request that would 400 is never sent.
 * `min`/`max` on the two inputs are a second, earlier line of defence — most date pickers refuse to
 * let the operator pick a `To` before the current `From` (or vice versa) in the first place — but the
 * validation on submit is the one that actually holds, since `min`/`max` do not stop a typed value
 * that never went through the picker UI.
 */
export function DateRangeFilter({ from, to, onApply }: DateRangeFilterProps) {
  const [pendingFrom, setPendingFrom] = useState(from ?? '')
  const [pendingTo, setPendingTo] = useState(to ?? '')
  const [error, setError] = useState<string | null>(null)

  const active = from !== null || to !== null

  function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (pendingFrom !== '' && pendingTo !== '' && pendingFrom > pendingTo) {
      setError('From must be on or before To.')
      return
    }

    setError(null)
    onApply(pendingFrom === '' ? null : pendingFrom, pendingTo === '' ? null : pendingTo)
  }

  function clear() {
    setPendingFrom('')
    setPendingTo('')
    setError(null)
    onApply(null, null)
  }

  return (
    <form
      className="date-range-filter"
      role="search"
      aria-label="Date range"
      onSubmit={submit}
      // The min/max attributes below are picker guidance, not the validation gate — a native
      // constraint-validation failure would block the submit event before this component's own
      // check (and its friendlier, testable error message) ever runs. noValidate makes this
      // component's own comparison the sole authority, matching what the code review's Critical #1
      // fix needs: the request must never be sent, but the reason has to reach the operator too.
      noValidate
    >
      {/* Code review Minor (both reviews): neither label said what the two dates actually filter
          on (a session's own start time, not its end, and not any occurrence within it) nor that
          the day boundary is UTC (`ApiHost.StartOfDayUtc`/`EndOfDayUtc`), which a local-timezone
          operator could otherwise read as "midnight in my own timezone". */}
      <p className="date-range-filter__hint">Filters by session start date (UTC).</p>
      <label className="date-range-filter__field" htmlFor="date-range-filter-from">
        From
        <input
          id="date-range-filter-from"
          type="date"
          value={pendingFrom}
          max={pendingTo === '' ? undefined : pendingTo}
          onChange={(event) => setPendingFrom(event.target.value)}
        />
      </label>
      <label className="date-range-filter__field" htmlFor="date-range-filter-to">
        To
        <input
          id="date-range-filter-to"
          type="date"
          value={pendingTo}
          min={pendingFrom === '' ? undefined : pendingFrom}
          onChange={(event) => setPendingTo(event.target.value)}
        />
      </label>
      <button type="submit">Apply</button>
      {active && (
        <button type="button" onClick={clear}>
          Clear
        </button>
      )}
      {error !== null && (
        <p role="alert" className="date-range-filter__error">
          {error}
        </p>
      )}
    </form>
  )
}
