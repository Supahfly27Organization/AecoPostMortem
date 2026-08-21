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
 */
export function Pager({ page, pageCount, onChange }: PagerProps) {
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
      <span className="pager__status">
        Page {page} of {pageCount}
      </span>
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
