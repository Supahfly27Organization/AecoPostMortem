import { useEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties, KeyboardEvent, UIEvent } from 'react'
import type { SessionTapeStep } from '../api/session'
import './Tape.css'

const KIND_LABEL: Record<SessionTapeStep['kind'], string> = {
  prompt: 'Prompt',
  hook: 'Hook',
  skill: 'Skill',
  toolCall: 'Tool call',
  mcpCall: 'MCP call',
}

/** Mockup parity item #10: a small glyph per step kind, a second, faster-to-scan signal alongside —
 * never instead of — `KIND_LABEL`'s own text, the same "colour/icon on top of the word, never the
 * only signal" discipline `ProvenanceBadge.tsx` established for provenance. Purely decorative
 * (`aria-hidden`, `focusable={false}`): `KIND_LABEL`'s text stays the row's one accessible name for
 * its kind. Each shape is a static inline SVG path using `stroke="currentColor"`/`fill="currentColor"`
 * only — no hardcoded colour — so it inherits `.session-tape__kind`'s own `--ink-3` token and needs
 * no separate rule for dark mode. Static markup only, computed from nothing per row (no measurement,
 * no per-row work beyond the `KIND_LABEL` lookup this file already does), keeping it as cheap as the
 * virtualised tape's own performance budget requires. */
function StepGlyph({ kind }: { kind: SessionTapeStep['kind'] }) {
  const common = {
    className: 'session-tape__glyph',
    viewBox: '0 0 24 24',
    width: 12,
    height: 12,
    'aria-hidden': true as const,
    focusable: false as const,
    'data-glyph': kind,
  }

  switch (kind) {
    case 'prompt':
      // A speech bubble: the operator's own words starting the step.
      return (
        <svg {...common} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
      )
    case 'hook':
      // A shepherd's-crook curve: a lifecycle hook catching the flow.
      return (
        <svg {...common} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M8 2v11a4 4 0 0 0 8 0V8" />
        </svg>
      )
    case 'skill':
      // A spark: a packaged skill firing.
      return (
        <svg {...common} fill="currentColor" stroke="none">
          <path d="M13 2 3 14h9l-1 8 10-12h-9l1-8z" />
        </svg>
      )
    case 'toolCall':
      // A wrench: the operator's own tools.
      return (
        <svg {...common} fill="currentColor" stroke="none">
          <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" />
        </svg>
      )
    case 'mcpCall':
      // A link: reaching out to an external MCP server.
      return (
        <svg {...common} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M15 7h3a5 5 0 0 1 5 5 5 5 0 0 1-5 5h-3m-6 0H6a5 5 0 0 1-5-5 5 5 0 0 1 5-5h3" />
          <line x1="8" y1="12" x2="16" y2="12" />
        </svg>
      )
    default:
      return null
  }
}

/** Fixed, never measured from the real DOM — jsdom (and this component's own tests) report zero
 * for every element's real layout size, so windowing math here is driven entirely by these two
 * constants and the scroll position, never by `getBoundingClientRect`. `Tape.css`'s own
 * `--session-tape-row-height`/`--session-tape-viewport-height` custom properties carry the same
 * two numbers into the stylesheet, so a future change to either constant only has to happen once
 * here plus once there, not be kept in sync by eye across a measured layout. */
const ROW_HEIGHT_PX = 32
const VIEWPORT_HEIGHT_PX = 480
const OVERSCAN_ROWS = 6

function formatOffset(offsetMs: number): string {
  return `${(offsetMs / 1000).toFixed(1)}s`
}

/** Mockup parity item #17: a small flag on the specific row a finding is unambiguously about — e.g.
 * the exact failed tool-call or hook row, not only the session-level chip bar
 * (`routes/SessionPage.tsx`'s `FindingChips`). `step.findings` is empty for the overwhelming
 * majority of rows (only the finding shapes `AecoPostMortem.Api.SessionTapeStepFindingLookup`
 * covers today ever populate it), so this renders nothing for a normal row — the same "no glyph
 * unless there is a real reason for one" discipline `StepGlyph`'s own kind icons already follow.
 * One `role="img"` with a single joined `aria-label` for however many findings flag this row,
 * mirroring `SessionStrip.tsx`'s own precedent for a compact marker that names everything it
 * represents in one accessible string rather than one per item. */
function StepFlag({ findings }: { findings: SessionTapeStep['findings'] }) {
  if (!findings || findings.length === 0) {
    return null
  }

  const label = findings.map((finding) => finding.headline).join(' ')

  return (
    <span className="session-tape__flag" role="img" aria-label={`Flagged: ${label}`} title={label}>
      ⚑
    </span>
  )
}

/** Mockup parity item #12: one row per position in the flat, wall-clock-ordered `steps` array,
 * plus one extra `'header'` row inserted immediately before each `'prompt'` step — the only
 * turn-boundary signal the wire carries at all (`SessionTapeStep` has no `turnId`; a prompt step's
 * own `label` already *is* the turn's own `Outcome`, `Api/CLAUDE.md`'s remarks on
 * `SessionTapeStepEnvelope`). This is deliberately a flat row list, not a second, nested
 * grouping/virtualisation structure layered on top of `steps`: a header is just another
 * fixed-`ROW_HEIGHT_PX` row type, so the windowing (`firstVisible`/`visible`), `ensureVisible` and
 * `moveSelection` machinery below needs no second math to learn — it already knows how to window
 * over "a flat list of fixed-height rows," and a `TapeRow` is still exactly that.
 *
 * Grouping is positional, never a re-sort: a subagent's own steps interleave with the main thread
 * in real wall-clock order (see this file's own "A subagent's lane is a per-row marker, not a
 * contiguous block" precedent, `web/CLAUDE.md` — mockup parity item #20 was marked "Won't" for the
 * identical reason), and this function's single linear pass over `steps` preserves that order
 * unchanged. A subagent step occurring between turn N's prompt and turn N+1's prompt therefore
 * renders inside turn N's group, at its normal wall-clock position, carrying its own lane marker
 * (`data-owner-kind`/`data-agent-lane`, item #10) as the only signal distinguishing it from a
 * main-thread step in the same group — this function does not special-case `ownerKind` at all, and
 * never reorders `steps`. Any leading steps before the tape's first `'prompt'` (not expected from a
 * real session, which always opens on the user's own prompt, but not structurally ruled out) render
 * with no header above them, rather than inventing an unlabelled zeroth turn.
 *
 * Mockup parity item #13 ("Prose in transcript"): a `'thinking'` row is inserted immediately after a
 * `'prompt'` step's own row, but only when that step's `thinking` is the `'present'` shape — real,
 * readable reasoning text, not a stated-absence reason. The mockup's own prose blocks render full,
 * unclamped text; this row instead stays exactly one fixed `ROW_HEIGHT_PX` tall and clamps its text
 * with CSS ellipsis, a deliberate divergence: `Tape`'s absolute-positioning windowing math (see the
 * class doc comment below) depends on every row being the identical fixed height, so a variable-height
 * block would misposition every row beneath it. The Thinking tab (unchanged by this item) is still
 * where the full text reads; this row is a readable-at-a-glance preview, not a replacement for it. The
 * `'unavailable'` shape (encrypted or simply not recorded) adds no row at all — on the live reference
 * corpus a single session's own 195 turns split 35 present / 105 unavailable / 55 with no reasoning
 * recorded, so inlining the unavailable reason on every one of those rows would have made the tape
 * mostly repeated boilerplate rather than a readability win.
 */
type TapeRow =
  | { kind: 'header'; key: string; turnNumber: number; label: string }
  | { kind: 'step'; key: string; step: SessionTapeStep; stepIndex: number }
  | { kind: 'thinking'; key: string; text: string }

function buildRows(steps: SessionTapeStep[]): { rows: TapeRow[]; rowIndexByStep: number[] } {
  const rows: TapeRow[] = []
  const rowIndexByStep: number[] = new Array(steps.length)
  let turnNumber = 0

  steps.forEach((step, stepIndex) => {
    if (step.kind === 'prompt') {
      turnNumber += 1
      rows.push({ kind: 'header', key: `turn-${step.stepId}`, turnNumber, label: step.label })
    }

    rowIndexByStep[stepIndex] = rows.length
    rows.push({ kind: 'step', key: step.stepId, step, stepIndex })

    if (step.kind === 'prompt' && step.thinking?.kind === 'present') {
      rows.push({ kind: 'thinking', key: `thinking-${step.stepId}`, text: step.thinking.text })
    }
  })

  return { rows, rowIndexByStep }
}

/** FR-22 (S-09, issue #18), Scenario 5: a deterministic lane index (0-7) for one subagent's own
 * `agentId`, cheap enough to recompute per row rather than carried in state — two rows sharing an
 * `agentId` always land in the same lane, and this needs no lane list to be passed in at all
 * (`Tape` only ever sees the steps, never the full `SessionAgentLane[]`), which is what keeps two
 * concurrent subagents' steps visually distinct even though they can interleave in wall-clock order
 * rather than arriving as one contiguous block each. */
const LANE_COUNT = 8

function laneIndex(agentId: string): number {
  let hash = 0
  for (let i = 0; i < agentId.length; i += 1) {
    hash = (hash * 31 + agentId.charCodeAt(i)) >>> 0
  }
  return hash % LANE_COUNT
}

/** FR-25 (S-12, issue #21): a skill step's plugin, alongside its version when both are recorded —
 * neither is rendered alone, matching `SessionTapeStep.pluginVersion`'s own "never populated
 * without `pluginName`" contract. */
function formatPlugin(step: SessionTapeStep): string | null {
  if (step.pluginName === null) {
    return null
  }

  return step.pluginVersion === null ? step.pluginName : `${step.pluginName} v${step.pluginVersion}`
}

/**
 * FR-21, part 3 of 3 (S-53, issue #17): the tape at scale. Windowing (Scenario 1) and full
 * keyboard reachability (Scenario 2) are the same mechanism here, not two separate features bolted
 * together — `selectedIndex` is state, not DOM focus, precisely because a virtualised row is not
 * always mounted to receive real focus; moving the selection is what pulls a distant row into the
 * mounted window (`ensureVisible`), and only then does it exist in the DOM to select.
 *
 * A single roving tab stop (this `<ul>` itself, `tabIndex={0}`) rather than one tab stop per row:
 * a session at the largest measured scale (84 turns, 764 tool calls) would otherwise put 848 stops
 * in the operator's Tab order for one page. `aria-activedescendant` names the selected row's id so
 * assistive technology still announces which step is current, the same composite-widget pattern a
 * combobox or a virtualised listbox already uses for this exact "many options, one tab stop" shape.
 *
 * A row is deliberately not its own tab stop, but it is still a real click target: `selectRow`
 * gives a mouse user the same "select any step" contract (FR-21 part 2 of 3, S-52, issue #16,
 * Scenario 1) that `moveSelection` plus Enter/Space already give a keyboard user, converging on the
 * one `onSelectStep` callback either input method calls.
 *
 * Mockup parity item #12 (turn grouping): `selectedIndex`/`moveSelection`/keyboard handling all
 * still operate purely in *step* index space — `buildRows`'s header rows are never counted or
 * addressable there, so Home/End/Arrow/PageUp/PageDown can only ever land on a real step, matching
 * this file's own "a row is a real click target but never a second tab stop" discipline extended one
 * level further: a header is not a tab stop *and* not a click target *and* not individually
 * selectable at all. Only `ensureVisible` needs to know a row can be a header — it translates a step
 * index to its row position (`rowIndexByStep`) before doing the identical scroll-into-view math it
 * always has.
 */
export function Tape({
  steps,
  onSelectStep,
  onViewportChange,
}: {
  steps: SessionTapeStep[]
  onSelectStep?: (step: SessionTapeStep) => void
  /** Mockup parity item #16 (`session/TapeMinimap.tsx`): the smallest additive hook a parent needs
   * to build a scroll-synced overview without lifting `scrollTop` itself out of this component or
   * touching its own virtualisation/selection internals. Fired from the same place this file already
   * recomputes `firstVisible`/`visible` (the `useMemo` below) — `firstVisibleIndex`/`visibleCount`
   * are in *step* index space, never row space, so a caller never has to know `buildRows` inserts an
   * extra header row per turn; `totalSteps` is `steps.length`, handed through so a caller does not
   * have to hold `steps` itself just to know its own length. Deduplicated against the last emitted
   * values (a ref, not state — this must not itself trigger a re-render) so a scroll tick that
   * doesn't actually change which steps are mounted never fires a redundant parent update. */
  onViewportChange?: (firstVisibleIndex: number, visibleCount: number, totalSteps: number) => void
}) {
  const containerRef = useRef<HTMLUListElement>(null)
  const [scrollTop, setScrollTop] = useState(0)
  const [selectedIndex, setSelectedIndex] = useState(0)
  const lastViewportRef = useRef<{ first: number; count: number; total: number } | null>(null)

  useEffect(() => {
    setSelectedIndex((current) => Math.min(current, Math.max(steps.length - 1, 0)))
    setScrollTop(0)
    // Only re-clamp when the step list itself changes (a new session loaded) — not on every
    // scroll/selection change, which would fight the user's own scrolling.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [steps])

  useEffect(() => {
    const node = containerRef.current
    if (node && node.scrollTop !== scrollTop) {
      node.scrollTop = scrollTop
    }
  }, [scrollTop])

  const { rows, rowIndexByStep } = useMemo(() => buildRows(steps), [steps])
  const totalHeight = rows.length * ROW_HEIGHT_PX
  const rowsPerPage = Math.max(1, Math.ceil(VIEWPORT_HEIGHT_PX / ROW_HEIGHT_PX))

  const { firstVisible, visible } = useMemo(() => {
    const first = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT_PX) - OVERSCAN_ROWS)
    const last = Math.min(rows.length - 1, first + rowsPerPage + 2 * OVERSCAN_ROWS)
    return { firstVisible: first, visible: rows.slice(first, last + 1) }
  }, [scrollTop, rows, rowsPerPage])

  useEffect(() => {
    if (!onViewportChange) {
      return
    }

    const stepRows = visible.filter((row): row is Extract<TapeRow, { kind: 'step' }> => row.kind === 'step')
    const firstStepIndex = stepRows.length > 0 ? stepRows[0].stepIndex : 0
    const visibleCount = stepRows.length
    const totalSteps = steps.length

    const last = lastViewportRef.current
    if (last && last.first === firstStepIndex && last.count === visibleCount && last.total === totalSteps) {
      return
    }

    lastViewportRef.current = { first: firstStepIndex, count: visibleCount, total: totalSteps }
    onViewportChange(firstStepIndex, visibleCount, totalSteps)
  }, [visible, steps.length, onViewportChange])

  if (steps.length === 0) {
    return <p className="session-tape__empty">No steps were recorded for this session.</p>
  }

  /** `stepIndex` addresses `steps`, never `rows` — the caller (`moveSelection`/`selectRow`) always
   * has a step in hand, and this is the one place that turns it into the row position (which may sit
   * one past the step's own index, once a header has been inserted ahead of it) the scroll math
   * needs. */
  function ensureVisible(stepIndex: number) {
    const rowIndex = rowIndexByStep[stepIndex] ?? stepIndex
    const rowTop = rowIndex * ROW_HEIGHT_PX
    const rowBottom = rowTop + ROW_HEIGHT_PX
    setScrollTop((current) => {
      if (rowTop < current) {
        return rowTop
      }

      if (rowBottom > current + VIEWPORT_HEIGHT_PX) {
        return rowBottom - VIEWPORT_HEIGHT_PX
      }

      return current
    })
  }

  function moveSelection(nextIndex: number) {
    const clamped = Math.max(0, Math.min(steps.length - 1, nextIndex))
    setSelectedIndex(clamped)
    ensureVisible(clamped)
  }

  /** A row is not its own tab stop (see the roving-tab-stop remarks above), but a mouse click still
   * has to move the selection and fire `onSelectStep` — the same "select and show evidence" contract
   * Enter/Space give a keyboard user. `index` is a step index, the same space `moveSelection` already
   * operates in — a header row never calls this at all (see the render loop below). */
  function selectRow(index: number) {
    setSelectedIndex(index)
    ensureVisible(index)
    onSelectStep?.(steps[index])
  }

  function handleScroll(event: UIEvent<HTMLUListElement>) {
    setScrollTop(event.currentTarget.scrollTop)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLUListElement>) {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        moveSelection(selectedIndex + 1)
        break
      case 'ArrowUp':
        event.preventDefault()
        moveSelection(selectedIndex - 1)
        break
      case 'Home':
        event.preventDefault()
        moveSelection(0)
        break
      case 'End':
        event.preventDefault()
        moveSelection(steps.length - 1)
        break
      case 'PageDown':
        event.preventDefault()
        moveSelection(selectedIndex + rowsPerPage)
        break
      case 'PageUp':
        event.preventDefault()
        moveSelection(selectedIndex - rowsPerPage)
        break
      case 'Enter':
      case ' ':
        event.preventDefault()
        onSelectStep?.(steps[selectedIndex])
        break
      default:
        break
    }
  }

  const selectedStepId = steps[selectedIndex]?.stepId

  return (
    <ul
      ref={containerRef}
      className="session-tape"
      aria-label="Tape"
      tabIndex={0}
      aria-activedescendant={selectedStepId ? `tape-step-${selectedStepId}` : undefined}
      onScroll={handleScroll}
      onKeyDown={handleKeyDown}
      style={{ height: Math.min(VIEWPORT_HEIGHT_PX, totalHeight) || undefined }}
    >
      <li aria-hidden="true" className="session-tape__spacer" style={{ height: totalHeight }} />
      {visible.map((row, offset) => {
        const rowIndex = firstVisible + offset
        const top = rowIndex * ROW_HEIGHT_PX

        if (row.kind === 'header') {
          // Mockup parity item #12: a section divider, not a step — `role="presentation"` keeps it
          // out of `getAllByRole('listitem')` the same way the spacer above is kept out via
          // `aria-hidden`, so "listitem" still means "a real, selectable step" (the S-08-era
          // invariant `web/CLAUDE.md` documents). Never a tab stop, never a click target: nothing
          // here calls `selectRow`/`moveSelection`. The turn number and the reused prompt label are
          // rendered as one combined string in one element (not two separate spans) so this text
          // never exactly duplicates the prompt step's own `session-tape__label` text below it.
          return (
            <li key={row.key} role="presentation" className="session-tape__turn-header" style={{ top }}>
              <span className="session-tape__turn-header-text">{`Turn ${row.turnNumber} — ${row.label}`}</span>
            </li>
          )
        }

        if (row.kind === 'thinking') {
          // Mockup parity item #13: never a tab stop and never a click target, the same
          // `role="presentation"` treatment the turn header above already gets, and for the same
          // reason — this is a section-local annotation, not a selectable step.
          return (
            <li key={row.key} role="presentation" className="session-tape__thinking" style={{ top }}>
              <span className="session-tape__thinking-text">{row.text}</span>
            </li>
          )
        }

        const { step, stepIndex } = row
        const isSelected = stepIndex === selectedIndex
        const plugin = formatPlugin(step)
        const isFlagged = (step.findings?.length ?? 0) > 0

        const rowStyle: CSSProperties & { '--session-tape-lane'?: number } = { top }
        if (step.ownerKind === 'agent' && step.agentId !== null) {
          rowStyle['--session-tape-lane'] = laneIndex(step.agentId)
        }

        return (
          <li
            key={step.stepId}
            id={`tape-step-${step.stepId}`}
            className="session-tape__step"
            aria-selected={isSelected}
            data-selected={isSelected ? 'true' : undefined}
            data-owner-kind={step.ownerKind}
            data-agent-id={step.agentId ?? undefined}
            data-agent-lane={step.ownerKind === 'agent' && step.agentId !== null ? laneIndex(step.agentId) : undefined}
            data-flagged={isFlagged ? 'true' : undefined}
            style={rowStyle}
          >
            {/* tabIndex={-1}: a mouse/screen-reader click target, not a second tab stop — the
             * roving tab stop above stays the list's only one, per the doc comment's own
             * reasoning; `aria-activedescendant` is what names this row current, not focus. */}
            <button type="button" className="session-tape__step-button" tabIndex={-1} onClick={() => selectRow(stepIndex)}>
              <span className="session-tape__offset">{formatOffset(step.offsetMs)}</span>
              <span className="session-tape__kind">
                <StepGlyph kind={step.kind} />
                {KIND_LABEL[step.kind]}
              </span>
              <span className="session-tape__label">
                {step.kind === 'prompt' && step.promptText ? step.promptText : step.label}
              </span>
              {plugin !== null && <span className="session-tape__plugin">{plugin}</span>}
              <StepFlag findings={step.findings} />
            </button>
          </li>
        )
      })}
    </ul>
  )
}
