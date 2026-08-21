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
 */
export function Tape({
  steps,
  onSelectStep,
}: {
  steps: SessionTapeStep[]
  onSelectStep?: (step: SessionTapeStep) => void
}) {
  const containerRef = useRef<HTMLUListElement>(null)
  const [scrollTop, setScrollTop] = useState(0)
  const [selectedIndex, setSelectedIndex] = useState(0)

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

  const totalHeight = steps.length * ROW_HEIGHT_PX
  const rowsPerPage = Math.max(1, Math.ceil(VIEWPORT_HEIGHT_PX / ROW_HEIGHT_PX))

  const { firstVisible, visible } = useMemo(() => {
    const first = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT_PX) - OVERSCAN_ROWS)
    const last = Math.min(steps.length - 1, first + rowsPerPage + 2 * OVERSCAN_ROWS)
    return { firstVisible: first, visible: steps.slice(first, last + 1) }
  }, [scrollTop, steps, rowsPerPage])

  if (steps.length === 0) {
    return <p className="session-tape__empty">No steps were recorded for this session.</p>
  }

  function ensureVisible(index: number) {
    const rowTop = index * ROW_HEIGHT_PX
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
   * Enter/Space give a keyboard user. */
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
      {visible.map((step, offset) => {
        const index = firstVisible + offset
        const isSelected = index === selectedIndex
        const plugin = formatPlugin(step)

        const rowStyle: CSSProperties & { '--session-tape-lane'?: number } = { top: index * ROW_HEIGHT_PX }
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
            style={rowStyle}
          >
            {/* tabIndex={-1}: a mouse/screen-reader click target, not a second tab stop — the
             * roving tab stop above stays the list's only one, per the doc comment's own
             * reasoning; `aria-activedescendant` is what names this row current, not focus. */}
            <button type="button" className="session-tape__step-button" tabIndex={-1} onClick={() => selectRow(index)}>
              <span className="session-tape__offset">{formatOffset(step.offsetMs)}</span>
              <span className="session-tape__kind">
                <StepGlyph kind={step.kind} />
                {KIND_LABEL[step.kind]}
              </span>
              <span className="session-tape__label">{step.label}</span>
              {plugin !== null && <span className="session-tape__plugin">{plugin}</span>}
            </button>
          </li>
        )
      })}
    </ul>
  )
}
