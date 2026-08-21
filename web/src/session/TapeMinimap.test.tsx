import { cleanup, render } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { SessionTapeStep } from '../api/session'
import { TapeMinimap, markColorForStep, viewportBand } from './TapeMinimap'

afterEach(() => {
  cleanup()
})

function buildStep(step: Pick<SessionTapeStep, 'kind' | 'stepId'> & Partial<SessionTapeStep>): SessionTapeStep {
  return {
    label: step.stepId,
    pluginName: null,
    pluginVersion: null,
    timestamp: '2026-08-16T10:00:00Z',
    offsetMs: 0,
    ownerKind: 'main',
    agentId: null,
    ...step,
  }
}

/** Mockup parity item #16: `markColorForStep`/`viewportBand` are the data this component's canvas
 * drawing reads — tested directly, the same "test the data, not jsdom's zeroed-out canvas" reasoning
 * `Tape.tsx`'s own doc comment gives for its fixed-height virtualisation math (jsdom returns `null`
 * from `HTMLCanvasElement.getContext('2d')`, so nothing here can assert on real pixel output). */
describe('markColorForStep', () => {
  it('gives a turn-opening prompt its own color, distinct from every other kind', () => {
    const prompt = buildStep({ kind: 'prompt', stepId: 'p1' })
    expect(markColorForStep(prompt)).toBe('turn')
  })

  it('gives a subagent-owned step one shared color, regardless of kind', () => {
    const toolCall = buildStep({ kind: 'toolCall', stepId: 't1', ownerKind: 'agent', agentId: 'a1' })
    const mcpCall = buildStep({ kind: 'mcpCall', stepId: 'm1', ownerKind: 'agent', agentId: 'a2' })
    expect(markColorForStep(toolCall)).toBe('agent')
    expect(markColorForStep(mcpCall)).toBe('agent')
  })

  it('gives a plain main-thread, non-prompt step a third, distinct color', () => {
    const toolCall = buildStep({ kind: 'toolCall', stepId: 't1' })
    const hook = buildStep({ kind: 'hook', stepId: 'h1' })
    expect(markColorForStep(toolCall)).toBe('main')
    expect(markColorForStep(hook)).toBe('main')
  })
})

describe('viewportBand', () => {
  it('returns null when there is no viewport reported yet', () => {
    expect(viewportBand(100, null)).toBeNull()
  })

  it('returns null for an empty session', () => {
    expect(viewportBand(0, { firstVisibleIndex: 0, visibleCount: 0 })).toBeNull()
  })

  it('returns null when the reported window is itself empty', () => {
    expect(viewportBand(100, { firstVisibleIndex: 10, visibleCount: 0 })).toBeNull()
  })

  it('converts a step-index window into fractions of the total', () => {
    const band = viewportBand(200, { firstVisibleIndex: 20, visibleCount: 50 })
    expect(band).not.toBeNull()
    expect(band!.startFraction).toBeCloseTo(20 / 200)
    expect(band!.endFraction).toBeCloseTo(70 / 200)
  })

  it('clamps a reported window that runs past the end of the session', () => {
    const band = viewportBand(100, { firstVisibleIndex: 80, visibleCount: 50 })
    expect(band).not.toBeNull()
    expect(band!.startFraction).toBeCloseTo(0.8)
    expect(band!.endFraction).toBe(1)
  })
})

/** Mockup parity item #16's own scope: "decorative... not interactive" — no click, no keyboard
 * target, never competing with the tape's own roving tab stop. */
describe('TapeMinimap is decorative, never interactive', () => {
  it('renders as an aria-hidden element with no interactive role, tabIndex or click handler', () => {
    const steps = [buildStep({ kind: 'prompt', stepId: 'p1' }), buildStep({ kind: 'toolCall', stepId: 't1' })]
    const { container } = render(<TapeMinimap steps={steps} viewport={{ firstVisibleIndex: 0, visibleCount: 2 }} />)

    const root = container.querySelector('.tape-minimap')
    expect(root).not.toBeNull()
    expect(root).toHaveAttribute('aria-hidden', 'true')
    expect(root).not.toHaveAttribute('role')
    expect(root).not.toHaveAttribute('tabindex')
    expect(root).not.toHaveAttribute('onclick')
  })

  it('renders without throwing for a large step count, and without a real canvas context in jsdom', () => {
    const steps = Array.from({ length: 848 }, (_, index) =>
      buildStep({ kind: index % 2 === 0 ? 'prompt' : 'toolCall', stepId: `step-${index}` }),
    )

    expect(() =>
      render(<TapeMinimap steps={steps} viewport={{ firstVisibleIndex: 100, visibleCount: 40 }} />),
    ).not.toThrow()
  })

  it('renders two stacked canvases: one for the step marks, one for the viewport band', () => {
    const steps = [buildStep({ kind: 'prompt', stepId: 'p1' })]
    const { container } = render(<TapeMinimap steps={steps} viewport={null} />)

    expect(container.querySelector('canvas.tape-minimap__marks')).not.toBeNull()
    expect(container.querySelector('canvas.tape-minimap__viewport')).not.toBeNull()
  })
})
