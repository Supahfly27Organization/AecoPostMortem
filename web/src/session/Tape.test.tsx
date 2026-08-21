import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup } from '@testing-library/react'
import type { SessionTapeStep } from '../api/session'
import { Tape } from './Tape'

afterEach(() => {
  cleanup()
})

/** FR-21, part 3 of 3 (S-53, issue #17), Scenario 1: the largest measured session — 84 turns and
 * 764 tool calls — is used directly as the synthetic scale fixture, rather than a rounder but less
 * representative number. */
function buildSteps(count: number): SessionTapeStep[] {
  return Array.from({ length: count }, (_, index) => ({
    kind: index % 2 === 0 ? 'prompt' : 'toolCall',
    stepId: `step-${index}`,
    label: `Step ${index}`,
    pluginName: null,
    pluginVersion: null,
    timestamp: '2026-08-16T10:00:00Z',
    offsetMs: index * 1_000,
    ownerKind: 'main',
    agentId: null,
  }))
}

const LARGEST_MEASURED_STEP_COUNT = 84 + 764

describe('A long session renders without loading every step', () => {
  it('virtualises rather than mounting every step at once, turn headers included', () => {
    const steps = buildSteps(LARGEST_MEASURED_STEP_COUNT)
    render(<Tape steps={steps} />)

    const rows = screen.getAllByRole('listitem')
    expect(rows.length).toBeGreaterThan(0)
    expect(rows.length).toBeLessThan(100)
    expect(rows.length).toBeLessThan(steps.length)

    // Mockup parity item #12: `buildSteps` alternates 'prompt' every other step, so this fixture
    // really does exercise turn-header insertion at scale (not just absent by construction) —
    // confirming the windowing budget above still holds once headers are a second row type.
    const headers = document.querySelectorAll('.session-tape__turn-header')
    expect(headers.length).toBeGreaterThan(0)
    expect(headers.length).toBeLessThan(100)
  })

  it('mounts the step scrolled to, not only the steps at the very start', () => {
    const steps = buildSteps(LARGEST_MEASURED_STEP_COUNT)
    render(<Tape steps={steps} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    fireEvent.scroll(tape, { target: { scrollTop: 10_000 } })

    expect(screen.queryByText('Step 0')).not.toBeInTheDocument()
  })
})

describe('The tape is navigable without a mouse', () => {
  it('reaches and selects the very last step via End, without needing every intermediate step keystroked', () => {
    const steps = buildSteps(LARGEST_MEASURED_STEP_COUNT)
    render(<Tape steps={steps} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    tape.focus()
    fireEvent.keyDown(tape, { key: 'End' })

    const lastStepId = steps[steps.length - 1].stepId
    const lastRow = screen.getByText(`Step ${steps.length - 1}`).closest('li')
    expect(lastRow).not.toBeNull()
    expect(lastRow).toHaveAttribute('aria-selected', 'true')
    expect(tape).toHaveAttribute('aria-activedescendant', `tape-step-${lastStepId}`)
    // Mockup parity item #12: reachability lands on a real step, never a turn-header row.
    expect(lastRow).not.toHaveClass('session-tape__turn-header')
  })

  it('reaches and selects the first step via Home after moving away from it', () => {
    const steps = buildSteps(LARGEST_MEASURED_STEP_COUNT)
    render(<Tape steps={steps} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    tape.focus()
    fireEvent.keyDown(tape, { key: 'End' })
    fireEvent.keyDown(tape, { key: 'Home' })

    const firstRow = screen.getByText('Step 0').closest('li')
    expect(firstRow).toHaveAttribute('aria-selected', 'true')
    expect(tape).toHaveAttribute('aria-activedescendant', 'tape-step-step-0')
    // Step 0 is itself a 'prompt' step (Turn 1's own), so this also proves Home lands on the real
    // step row beneath its own turn header, not the header row rendered just above it.
    expect(firstRow).not.toHaveClass('session-tape__turn-header')
  })

  it('never selects a turn header while stepping through every row with the arrow key', () => {
    // Every other step is a 'prompt' (see buildSteps), so a header sits immediately before roughly
    // half of these 60 rows — the densest realistic turn-grouping case this fixture family can
    // produce, well past initial-viewport scale.
    const steps = buildSteps(60)
    render(<Tape steps={steps} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    tape.focus()

    for (let i = 0; i < steps.length - 1; i += 1) {
      fireEvent.keyDown(tape, { key: 'ArrowDown' })
      const activeId = tape.getAttribute('aria-activedescendant')
      expect(activeId).toBe(`tape-step-${steps[i + 1].stepId}`)
    }
  })

  it('moves selection one step at a time with the arrow keys', () => {
    const steps = buildSteps(50)
    render(<Tape steps={steps} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    tape.focus()
    fireEvent.keyDown(tape, { key: 'ArrowDown' })
    fireEvent.keyDown(tape, { key: 'ArrowDown' })

    const row = screen.getByText('Step 2').closest('li')
    expect(row).toHaveAttribute('aria-selected', 'true')
  })

  it('jumps by a page with PageDown, reaching steps far past the initial viewport', () => {
    const steps = buildSteps(LARGEST_MEASURED_STEP_COUNT)
    render(<Tape steps={steps} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    tape.focus()
    for (let i = 0; i < 20; i += 1) {
      fireEvent.keyDown(tape, { key: 'PageDown' })
    }

    const activeId = tape.getAttribute('aria-activedescendant')
    expect(activeId).not.toBeNull()
    expect(activeId).not.toBe('tape-step-step-0')
  })

  it('fires the selection callback on Enter', () => {
    const steps = buildSteps(10)
    let selected: SessionTapeStep | null = null
    render(<Tape steps={steps} onSelectStep={(step) => { selected = step }} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    tape.focus()
    fireEvent.keyDown(tape, { key: 'ArrowDown' })
    fireEvent.keyDown(tape, { key: 'Enter' })

    expect(selected).not.toBeNull()
    expect(selected!.stepId).toBe('step-1')
  })
})

/** Mockup parity item #10: a small decorative glyph per step kind, alongside — never instead of —
 * `KIND_LABEL`'s existing text, the same "colour/icon is a second signal on top of the word, never
 * the only one" discipline `ProvenanceBadge.tsx` established for provenance. */
describe('Each step kind renders its own glyph before the text label', () => {
  it('renders a distinct, aria-hidden glyph per kind, without changing the accessible text label', () => {
    const kinds: SessionTapeStep['kind'][] = ['prompt', 'hook', 'skill', 'toolCall', 'mcpCall']
    const steps: SessionTapeStep[] = kinds.map((kind, index) => ({
      kind,
      stepId: `step-${index}`,
      label: `Step ${index}`,
      pluginName: null,
      pluginVersion: null,
      timestamp: '2026-08-16T10:00:00Z',
      offsetMs: index * 1_000,
      ownerKind: 'main',
      agentId: null,
    }))
    render(<Tape steps={steps} />)

    const expectedLabels: Record<SessionTapeStep['kind'], string> = {
      prompt: 'Prompt',
      hook: 'Hook',
      skill: 'Skill',
      toolCall: 'Tool call',
      mcpCall: 'MCP call',
    }

    const glyphMarkup = new Set<string>()

    kinds.forEach((kind, index) => {
      const row = screen.getByText(`Step ${index}`).closest('li')
      expect(row).not.toBeNull()

      const kindSpan = row!.querySelector('.session-tape__kind')
      expect(kindSpan).not.toBeNull()
      // The existing accessible text label is unchanged and still present.
      expect(kindSpan!.textContent).toContain(expectedLabels[kind])

      const glyph = row!.querySelector(`svg[data-glyph="${kind}"]`)
      expect(glyph).not.toBeNull()
      expect(glyph).toHaveAttribute('aria-hidden', 'true')

      glyphMarkup.add(glyph!.innerHTML)
    })

    // Each of the 5 kinds gets a visually distinct glyph, not one shape reused for all.
    expect(glyphMarkup.size).toBe(kinds.length)
  })
})

/** Mockup parity item #16 (`session/TapeMinimap.tsx`): `onViewportChange` is the one additive hook
 * this file gives a parent to build a scroll-synced overview without lifting `scrollTop` itself out
 * of this component. */
describe('Mockup parity item #16: onViewportChange reports the mounted step window', () => {
  it('reports the initial viewport once on mount, in step index space', () => {
    const steps = buildSteps(10)
    const calls: Array<[number, number, number]> = []
    render(<Tape steps={steps} onViewportChange={(first, count, total) => calls.push([first, count, total])} />)

    expect(calls.length).toBeGreaterThan(0)
    const [first, count, total] = calls[calls.length - 1]
    expect(first).toBe(0)
    expect(count).toBe(steps.length)
    expect(total).toBe(steps.length)
  })

  it('updates the reported viewport once the tape scrolls past the initial window', () => {
    const steps = buildSteps(LARGEST_MEASURED_STEP_COUNT)
    const calls: Array<[number, number, number]> = []
    render(<Tape steps={steps} onViewportChange={(first, count, total) => calls.push([first, count, total])} />)

    const tape = screen.getByRole('list', { name: 'Tape' })
    calls.length = 0
    fireEvent.scroll(tape, { target: { scrollTop: 10_000 } })

    expect(calls.length).toBeGreaterThan(0)
    const [first, , total] = calls[calls.length - 1]
    expect(first).toBeGreaterThan(0)
    expect(total).toBe(steps.length)
  })

  it('does not fire again when the reported viewport has not actually changed', () => {
    const steps = buildSteps(10)
    const calls: Array<[number, number, number]> = []
    const record = (first: number, count: number, total: number) => calls.push([first, count, total])
    const { rerender } = render(<Tape steps={steps} onViewportChange={record} />)

    const callCountAfterMount = calls.length
    // A new inline callback identity, but the mounted step window itself hasn't changed — the
    // dedup guard compares emitted values, not the callback's own identity.
    rerender(<Tape steps={steps} onViewportChange={(first, count, total) => record(first, count, total)} />)

    expect(calls.length).toBe(callCountAfterMount)
  })
})

describe('An empty tape', () => {
  it('states that no steps were recorded rather than rendering an empty virtualised list', () => {
    render(<Tape steps={[]} />)

    expect(screen.getByText(/no steps were recorded/i)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Tape' })).not.toBeInTheDocument()
  })
})

/** FR-22 (S-09, issue #18), Scenario 5: "each agent occupies its own lane and the main thread is
 * distinguishable from all of them" — a per-row marker, not a block grouping, since two concurrent
 * subagents' steps can interleave in wall-clock order rather than arriving as contiguous runs. */
describe('Lanes are visually separable from the main thread', () => {
  it('marks a main-thread step distinctly from a subagent-owned step', () => {
    const steps: SessionTapeStep[] = [
      { kind: 'prompt', stepId: 'main-1', label: 'Completed', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:00Z', offsetMs: 0, ownerKind: 'main', agentId: null },
      { kind: 'toolCall', stepId: 'agent-1', label: 'view', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:01Z', offsetMs: 1_000, ownerKind: 'agent', agentId: 'a1' },
    ]
    render(<Tape steps={steps} />)

    const mainRow = screen.getByText('Completed').closest('li')
    const agentRow = screen.getByText('view').closest('li')

    expect(mainRow).toHaveAttribute('data-owner-kind', 'main')
    expect(agentRow).toHaveAttribute('data-owner-kind', 'agent')
    expect(agentRow).toHaveAttribute('data-agent-id', 'a1')
  })

  it('gives two different concurrent subagents their own, distinct lane', () => {
    const steps: SessionTapeStep[] = [
      { kind: 'toolCall', stepId: 'agent-1', label: 'view', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:01Z', offsetMs: 1_000, ownerKind: 'agent', agentId: 'a1' },
      { kind: 'toolCall', stepId: 'agent-2', label: 'grep', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:02Z', offsetMs: 2_000, ownerKind: 'agent', agentId: 'a2' },
      { kind: 'toolCall', stepId: 'agent-3', label: 'apply_patch', pluginName: null, pluginVersion: null, timestamp: '2026-08-16T10:00:03Z', offsetMs: 3_000, ownerKind: 'agent', agentId: 'a1' },
    ]
    render(<Tape steps={steps} />)

    const a1First = screen.getByText('view').closest('li')
    const a2 = screen.getByText('grep').closest('li')
    const a1Second = screen.getByText('apply_patch').closest('li')

    const laneOf = (row: Element | null) => row?.getAttribute('data-agent-lane')

    expect(laneOf(a1First)).not.toBeNull()
    expect(laneOf(a1First)).not.toBe(laneOf(a2))
    // The same agent keeps the same lane across two non-contiguous rows of its own steps.
    expect(laneOf(a1First)).toBe(laneOf(a1Second))
  })
})

function buildStep(step: Pick<SessionTapeStep, 'kind' | 'stepId' | 'label' | 'offsetMs'> & Partial<SessionTapeStep>): SessionTapeStep {
  return {
    pluginName: null,
    pluginVersion: null,
    timestamp: '2026-08-16T10:00:00Z',
    ownerKind: 'main',
    agentId: null,
    ...step,
  }
}

/** Mockup parity item #12 (`docs/product-superpowers/prioritization/2026-08-21-mockup-parity-gaps.md`,
 * row #12): the tape groups into visual turn sections. The only turn-boundary signal the wire
 * carries at all is `step.kind === 'prompt'` (`SessionTapeStep` has no `turnId` — verified against
 * `src/AecoPostMortem.Api/SessionEnvelope.cs`'s `SessionTapeStepEnvelope`, which has none either);
 * a prompt step's own `label` is the turn's own `Outcome` (`Api/CLAUDE.md`'s remarks on that
 * envelope), so a header reuses it rather than inventing a second text source. */
describe('Mockup parity item #12: the tape groups into turn sections', () => {
  it('renders one header per turn, immediately before that turn\'s own prompt row, labelled with the prompt\'s own reused label', () => {
    const steps: SessionTapeStep[] = [
      buildStep({ kind: 'prompt', stepId: 'p1', label: 'Fix the reconciliation bug', offsetMs: 0 }),
      buildStep({ kind: 'toolCall', stepId: 't1', label: 'rg', offsetMs: 1_000 }),
      buildStep({ kind: 'toolCall', stepId: 't2', label: 'view', offsetMs: 2_000, ownerKind: 'agent', agentId: 'a1' }),
      buildStep({ kind: 'prompt', stepId: 'p2', label: 'Write a regression test', offsetMs: 5_000 }),
      buildStep({ kind: 'toolCall', stepId: 't3', label: 'apply_patch', offsetMs: 6_000 }),
    ]
    const { container } = render(<Tape steps={steps} />)

    const rows = Array.from(container.querySelectorAll<HTMLElement>('.session-tape__turn-header, .session-tape__step'))
    const shapes = rows.map((row) =>
      row.classList.contains('session-tape__turn-header')
        ? { kind: 'header' as const, text: row.textContent }
        : { kind: 'step' as const, id: row.id },
    )

    expect(shapes).toEqual([
      { kind: 'header', text: 'Turn 1 — Fix the reconciliation bug' },
      { kind: 'step', id: 'tape-step-p1' },
      { kind: 'step', id: 'tape-step-t1' },
      { kind: 'step', id: 'tape-step-t2' },
      { kind: 'header', text: 'Turn 2 — Write a regression test' },
      { kind: 'step', id: 'tape-step-p2' },
      { kind: 'step', id: 'tape-step-t3' },
    ])
  })

  it('keeps a subagent step occurring between two prompts inside the earlier turn\'s group, at its normal wall-clock position, still carrying its own lane marker', () => {
    const steps: SessionTapeStep[] = [
      buildStep({ kind: 'prompt', stepId: 'p1', label: 'Fix the reconciliation bug', offsetMs: 0 }),
      buildStep({ kind: 'toolCall', stepId: 't1', label: 'rg', offsetMs: 1_000 }),
      // A subagent step, interleaved in real wall-clock order between turn 1's prompt and turn 2's —
      // it must stay in turn 1's group, never be pulled into a block of its own (the rejected design,
      // item #20, "Won't") and never reordered relative to its wall-clock neighbours.
      buildStep({ kind: 'toolCall', stepId: 'agent-1', label: 'view', offsetMs: 2_000, ownerKind: 'agent', agentId: 'a1' }),
      buildStep({ kind: 'prompt', stepId: 'p2', label: 'Write a regression test', offsetMs: 5_000 }),
    ]
    const { container } = render(<Tape steps={steps} />)

    const rows = Array.from(container.querySelectorAll<HTMLElement>('.session-tape__turn-header, .session-tape__step'))
    const ids = rows.map((row) => (row.classList.contains('session-tape__turn-header') ? `header:${row.textContent}` : row.id))

    // The agent step sits between t1 and the Turn 2 header — inside Turn 1's group, in its normal
    // wall-clock position, not reordered ahead of or behind its main-thread neighbours.
    expect(ids).toEqual([
      'header:Turn 1 — Fix the reconciliation bug',
      'tape-step-p1',
      'tape-step-t1',
      'tape-step-agent-1',
      'header:Turn 2 — Write a regression test',
      'tape-step-p2',
    ])

    const agentRow = container.querySelector('#tape-step-agent-1')
    expect(agentRow).toHaveAttribute('data-owner-kind', 'agent')
    expect(agentRow).toHaveAttribute('data-agent-id', 'a1')
    expect(agentRow).toHaveAttribute('data-agent-lane')
  })

  it('does not count a turn header as a listitem — only real, selectable steps are', () => {
    const steps: SessionTapeStep[] = [
      buildStep({ kind: 'prompt', stepId: 'p1', label: 'Fix the reconciliation bug', offsetMs: 0 }),
      buildStep({ kind: 'toolCall', stepId: 't1', label: 'rg', offsetMs: 1_000 }),
      buildStep({ kind: 'prompt', stepId: 'p2', label: 'Write a regression test', offsetMs: 5_000 }),
    ]
    render(<Tape steps={steps} />)

    // 3 steps, 2 turn headers — if a header counted as a listitem this would be 5, not 3.
    expect(screen.getAllByRole('listitem')).toHaveLength(steps.length)
    expect(document.querySelectorAll('.session-tape__turn-header')).toHaveLength(2)
  })

  it('is not a click target: clicking a header never selects it or fires onSelectStep', () => {
    const steps: SessionTapeStep[] = [
      buildStep({ kind: 'prompt', stepId: 'p1', label: 'Fix the reconciliation bug', offsetMs: 0 }),
      buildStep({ kind: 'toolCall', stepId: 't1', label: 'rg', offsetMs: 1_000 }),
    ]
    let selected: SessionTapeStep | null = null
    const { container } = render(<Tape steps={steps} onSelectStep={(step) => { selected = step }} />)

    const header = container.querySelector('.session-tape__turn-header')
    expect(header).not.toBeNull()
    expect(header!.querySelector('button')).toBeNull()

    fireEvent.click(header!)
    expect(selected).toBeNull()
  })
})

/** Mockup parity item #13 (`docs/product-superpowers/prioritization/2026-08-21-mockup-parity-gaps.md`,
 * row #13): the model's own readable reasoning for a prompt step, inlined directly under that
 * step's row rather than requiring a click into the inspector's Thinking tab. */
describe('Mockup parity item #13: readable reasoning inlines under its own prompt row', () => {
  it('renders a preview row immediately after a prompt step whose thinking is present', () => {
    const steps: SessionTapeStep[] = [
      buildStep({
        kind: 'prompt',
        stepId: 'p1',
        label: 'Completed',
        offsetMs: 0,
        thinking: { kind: 'present', text: 'I should check the failing test first.' },
      }),
      buildStep({ kind: 'toolCall', stepId: 't1', label: 'rg', offsetMs: 1_000 }),
    ]
    const { container } = render(<Tape steps={steps} />)

    const rows = Array.from(
      container.querySelectorAll<HTMLElement>('.session-tape__turn-header, .session-tape__step, .session-tape__thinking'),
    )
    const shapes = rows.map((row) => {
      if (row.classList.contains('session-tape__turn-header')) return { kind: 'header' as const }
      if (row.classList.contains('session-tape__thinking')) return { kind: 'thinking' as const, text: row.textContent }
      return { kind: 'step' as const, id: row.id }
    })

    expect(shapes).toEqual([
      { kind: 'header' },
      { kind: 'step', id: 'tape-step-p1' },
      { kind: 'thinking', text: 'I should check the failing test first.' },
      { kind: 'step', id: 'tape-step-t1' },
    ])
  })

  it('renders no extra row for a prompt step whose thinking is unavailable', () => {
    const steps: SessionTapeStep[] = [
      buildStep({
        kind: 'prompt',
        stepId: 'p1',
        label: 'Completed',
        offsetMs: 0,
        thinking: { kind: 'unavailable', reason: 'The model\'s reasoning for this step is provider-encrypted and cannot be read.' },
      }),
    ]
    const { container } = render(<Tape steps={steps} />)

    expect(container.querySelector('.session-tape__thinking')).toBeNull()
  })

  it('renders no extra row for a prompt step carrying no thinking at all', () => {
    const steps: SessionTapeStep[] = [
      buildStep({ kind: 'prompt', stepId: 'p1', label: 'Completed', offsetMs: 0 }),
    ]
    const { container } = render(<Tape steps={steps} />)

    expect(container.querySelector('.session-tape__thinking')).toBeNull()
  })

  it('renders no extra row for a non-prompt step, even if it somehow carried a present thinking', () => {
    const steps: SessionTapeStep[] = [
      buildStep({
        kind: 'toolCall',
        stepId: 't1',
        label: 'view',
        offsetMs: 0,
        thinking: { kind: 'present', text: 'Should never be read for a tool call.' },
      }),
    ]
    const { container } = render(<Tape steps={steps} />)

    expect(container.querySelector('.session-tape__thinking')).toBeNull()
  })

  it('is not a click target and never counts as a listitem', () => {
    const steps: SessionTapeStep[] = [
      buildStep({
        kind: 'prompt',
        stepId: 'p1',
        label: 'Completed',
        offsetMs: 0,
        thinking: { kind: 'present', text: 'I should check the failing test first.' },
      }),
    ]
    let selected: SessionTapeStep | null = null
    const { container } = render(<Tape steps={steps} onSelectStep={(step) => { selected = step }} />)

    const thinkingRow = container.querySelector('.session-tape__thinking')
    expect(thinkingRow).not.toBeNull()
    expect(thinkingRow!.querySelector('button')).toBeNull()

    fireEvent.click(thinkingRow!)
    expect(selected).toBeNull()

    // 1 real step (the prompt) — if the thinking row counted as a listitem this would be 2.
    expect(screen.getAllByRole('listitem')).toHaveLength(1)
  })
})
