import { useEffect, useMemo, useRef } from 'react'
import type { SessionTapeStep } from '../api/session'
import './TapeMinimap.css'

/** Mockup parity item #16: the color a single step's mark takes on the minimap, one of three
 * semantic tokens rather than the full 8-way per-agent lane hash `Tape.tsx`'s own `laneIndex`
 * computes for its rows — at the ~1px-per-mark scale a decorative overview draws at, eight hues are
 * not legibly distinguishable, so every subagent-owned step shares one tone (`'agent'`) instead. A
 * turn-opening prompt gets its own tone (`'turn'`, the same `--accent` `Tape.tsx`'s own turn-header
 * uses) since it is the one signal `SessionTapeStep` itself carries for "a new turn started" — see
 * `Tape.tsx`'s `buildRows` remarks. Exported and unit-tested directly against `SessionTapeStep`
 * fixtures, independent of canvas rendering. */
export type MinimapStepColor = 'turn' | 'agent' | 'main'

export function markColorForStep(step: SessionTapeStep): MinimapStepColor {
  if (step.kind === 'prompt') {
    return 'turn'
  }

  return step.ownerKind === 'agent' ? 'agent' : 'main'
}

const COLOR_TOKEN: Record<MinimapStepColor, string> = {
  turn: '--accent',
  agent: '--lane',
  main: '--ink-3',
}

/** The mounted window `Tape.tsx` reports via `onViewportChange`, in step index space. */
export interface MinimapViewport {
  firstVisibleIndex: number
  visibleCount: number
}

/** The highlighted band's vertical extent, as fractions of the minimap's own height (0..1) —
 * resolution-independent, so the canvas draw step only has to multiply by its own real pixel height.
 * `null` when there is nothing to highlight (no steps, or no viewport reported yet). Exported and
 * unit-tested directly, the same "test the data, not jsdom's zeroed-out canvas" reasoning
 * `Tape.tsx`'s own doc comment gives for its fixed-height virtualisation math — jsdom returns `null`
 * from `HTMLCanvasElement.getContext('2d')`, so nothing here can assert on real pixel output. */
export function viewportBand(totalSteps: number, viewport: MinimapViewport | null): { startFraction: number; endFraction: number } | null {
  if (viewport === null || totalSteps <= 0 || viewport.visibleCount <= 0) {
    return null
  }

  const start = Math.max(0, Math.min(viewport.firstVisibleIndex, totalSteps))
  const end = Math.max(start, Math.min(viewport.firstVisibleIndex + viewport.visibleCount, totalSteps))
  return { startFraction: start / totalSteps, endFraction: end / totalSteps }
}

function sizeCanvas(canvas: HTMLCanvasElement): { width: number; height: number; dpr: number } | null {
  const rect = canvas.getBoundingClientRect()
  if (rect.width <= 0 || rect.height <= 0) {
    // jsdom (and this component's own tests) report zero for every element's real layout size — the
    // same reason `Tape.tsx`'s own virtualisation math never measures the DOM. A real browser always
    // has a real height here since `.tape-minimap` is given one in `TapeMinimap.css`.
    return null
  }

  const dpr = window.devicePixelRatio || 1
  canvas.width = Math.round(rect.width * dpr)
  canvas.height = Math.round(rect.height * dpr)
  return { width: rect.width, height: rect.height, dpr }
}

/**
 * Mockup parity item #16: a decorative, read-only overview of the whole session's tape — the
 * mockup's own `.tape`/`#tapeCanvas` (`docs/product-superpowers/discovery/mockups/flight-recorder.html`),
 * ported as a standalone sibling component rather than folded into `Tape.tsx` itself, keeping this
 * item's diff small and disjoint from the turn-grouping/prose/flagbox work landing in `Tape.tsx` in
 * the same round — `Tape.tsx`'s only change for this item is the additive `onViewportChange` prop.
 *
 * Two stacked canvases, not one: `marksCanvasRef` draws one tick per step and is only ever redrawn
 * when `steps` itself changes (a new session loaded) or the container resizes — genuinely expensive
 * only at that cadence. `viewportCanvasRef` draws just the highlighted band and is redrawn on every
 * scroll-driven `viewport` change, which is cheap (a handful of rect/clear calls, never a per-step
 * loop) — this is what keeps scrolling from redrawing all ~800+ step marks on every tick, per this
 * item's own performance note. `Tape.tsx`'s own dedup guard on `onViewportChange` further limits how
 * often `viewport` actually changes identity at all.
 *
 * Colors are read from this app's own design tokens via `getComputedStyle`, the same technique the
 * mockup's own `drawTape()` uses for `--rule-2`/`--accent`/`--flag` (never a hardcoded hex). Unlike
 * plain CSS, a canvas bakes the resolved color in at draw time rather than tracking a token live, so
 * a `prefers-color-scheme` change on its own would otherwise leave a stale-themed minimap next to a
 * correctly-retheme'd tape — the mockup's own `drawTape()` hits this identical problem and fixes it
 * the same way, a `matchMedia('(prefers-color-scheme: dark)')` `'change'` listener that redraws
 * (below), which is the only live trigger this app has today (no in-app theme toggle yet).
 *
 * `aria-hidden` on the whole element, no `role`, no `tabIndex`, no click/keydown handler of any kind:
 * per this item's own scope (the mockup's own footer calls this element "decorative... not
 * interactive"), it must never become a second keyboard target competing with the tape's own roving
 * tab stop (`Tape.tsx`'s own non-obvious-decision section).
 */
export function TapeMinimap({ steps, viewport }: { steps: SessionTapeStep[]; viewport: MinimapViewport | null }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const marksCanvasRef = useRef<HTMLCanvasElement>(null)
  const viewportCanvasRef = useRef<HTMLCanvasElement>(null)

  const marks = useMemo(() => steps.map(markColorForStep), [steps])
  const band = useMemo(() => viewportBand(steps.length, viewport), [steps.length, viewport])

  function drawMarks() {
    const canvas = marksCanvasRef.current
    if (!canvas) {
      return
    }

    const sized = sizeCanvas(canvas)
    const ctx = canvas.getContext('2d')
    if (!sized || !ctx) {
      return
    }

    const { width, height, dpr } = sized
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, width, height)

    if (marks.length === 0) {
      return
    }

    const style = getComputedStyle(canvas)
    const colorFor = (color: MinimapStepColor) => style.getPropertyValue(COLOR_TOKEN[color]).trim() || 'currentColor'

    // One mark per step, positioned by its own fractional offset into the session — several marks
    // land on the same pixel row once there are more steps than pixels of height, and a later mark
    // simply overdraws an earlier one there. This is the same "representative density, not one
    // guaranteed pixel per call" compromise the mockup's own `drawTape()` makes.
    const rowHeight = height / marks.length
    marks.forEach((color, index) => {
      ctx.fillStyle = colorFor(color)
      ctx.fillRect(1, index * rowHeight, width - 2, Math.max(1, rowHeight))
    })
  }

  function drawViewport() {
    const canvas = viewportCanvasRef.current
    if (!canvas) {
      return
    }

    const sized = sizeCanvas(canvas)
    const ctx = canvas.getContext('2d')
    if (!sized || !ctx) {
      return
    }

    const { width, height, dpr } = sized
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, width, height)

    if (band === null) {
      return
    }

    const style = getComputedStyle(canvas)
    const fill = style.getPropertyValue('--accent-soft').trim() || 'rgba(168, 94, 0, 0.35)'
    const stroke = style.getPropertyValue('--accent').trim() || 'currentColor'

    const y1 = band.startFraction * height
    const y2 = Math.max(y1 + 2, band.endFraction * height)

    ctx.fillStyle = fill
    ctx.fillRect(0, y1, width, y2 - y1)
    ctx.strokeStyle = stroke
    ctx.lineWidth = 1
    ctx.strokeRect(0.5, y1 + 0.5, width - 1, Math.max(1, y2 - y1 - 1))
  }

  useEffect(() => {
    drawMarks()
    // Redraws only when the per-step color list itself changes (a new session, or a step's own
    // owner/kind changing identity) — never on a scroll tick, per this file's own performance note.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [marks])

  useEffect(() => {
    drawViewport()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [band])

  useEffect(() => {
    const container = containerRef.current
    if (!container || typeof ResizeObserver === 'undefined') {
      return undefined
    }

    const observer = new ResizeObserver(() => {
      drawMarks()
      drawViewport()
    })
    observer.observe(container)
    return () => observer.disconnect()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    // Colors are read via `getComputedStyle` at draw time (see the component's own remarks), so a
    // canvas never repaints on its own when the OS switches light/dark — unlike every other element
    // on this page, which is plain CSS and updates for free. The mockup's own `drawTape()` hits the
    // identical problem and fixes it the same way: a `prefers-color-scheme` change listener that
    // redraws. This app has no in-app theme toggle yet (`web/CLAUDE.md`'s own note on
    // `index.css`), so the media query is the only live trigger that exists today.
    if (typeof window.matchMedia !== 'function') {
      return undefined
    }

    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const redraw = () => {
      drawMarks()
      drawViewport()
    }
    media.addEventListener('change', redraw)
    return () => media.removeEventListener('change', redraw)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div className="tape-minimap" aria-hidden="true" ref={containerRef}>
      <canvas ref={marksCanvasRef} className="tape-minimap__marks" />
      <canvas ref={viewportCanvasRef} className="tape-minimap__viewport" />
    </div>
  )
}
