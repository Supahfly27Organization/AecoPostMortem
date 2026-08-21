import { useEffect, useRef } from 'react'
import './Pager.css'

interface PagerProps {
  /** 1-based, matching how "Page 3 of 12" reads. */
  page: number
  pageCount: number
  onChange: (page: number) => void
}

/**
 * The Process Digest's ranked-findings pager (pager & date-range filter task). Client-side, over the
 * already-served `rankedFindings` array — see the "The pager is client-side" non-obvious decision in
 * `web/CLAUDE.md`: the live corpus serves 297 ranked findings for its dominant repository, small
 * enough that the whole list is already fetched in one response before this component ever slices
 * it, so there is no server-side offset/limit contract here.
 *
 * Renders nothing at all when everything already fits on one page — the same "no control unless
 * there is a real reason for one" discipline `StepFlag`/`RuleCoverageBar` already follow elsewhere in
 * this app.
 *
 * Code review Important #3 (both the internal and external review): landing on the last page
 * disables the exact "Next" button the operator just clicked (the same for "Previous" into page 1),
 * which drops keyboard focus to `<body>` with nothing announced — the tape's own roving-tab-stop
 * keyboard model (`session/Tape.tsx`) already set a higher bar than that. The status text is
 * `role="status"` (an implicit `aria-live="polite"` region, so a screen reader announces the new
 * page without stealing focus the way `role="alert"` would) and also a real focus target
 * (`tabIndex={-1}`): every page change after the first mount moves focus onto it, so focus never
 * lands on `<body>` regardless of which button caused the change or whether it is now disabled.
 * Skipped on first mount (`hasMounted` ref) — nothing has navigated yet, so there is nothing to
 * announce and no reason to steal the page's own initial focus.
 */
export function Pager({ page, pageCount, onChange }: PagerProps) {
  const statusRef = useRef<HTMLParagraphElement>(null)
  const hasMounted = useRef(false)

  useEffect(() => {
    if (hasMounted.current) {
      statusRef.current?.focus()
    }
    hasMounted.current = true
  }, [page])

  if (pageCount <= 1) {
    return null
  }

  return (
    <div className="pager" role="group" aria-label="Findings pages">
      <button
        type="button"
        onClick={() => onChange(page - 1)}
        disabled={page <= 1}
        aria-label="Previous page"
      >
        Previous
      </button>
      <p className="pager__status" role="status" tabIndex={-1} ref={statusRef}>
        Page {page} of {pageCount}
      </p>
      <button
        type="button"
        onClick={() => onChange(page + 1)}
        disabled={page >= pageCount}
        aria-label="Next page"
      >
        Next
      </button>
    </div>
  )
}
