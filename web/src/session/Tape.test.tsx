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
    timestamp: '2026-08-16T10:00:00Z',
    offsetMs: index * 1_000,
    ownerKind: 'main',
    agentId: null,
  }))
}

const LARGEST_MEASURED_STEP_COUNT = 84 + 764

describe('A long session renders without loading every step', () => {
  it('virtualises rather than mounting every step at once', () => {
    const steps = buildSteps(LARGEST_MEASURED_STEP_COUNT)
    render(<Tape steps={steps} />)

    const rows = screen.getAllByRole('listitem')
    expect(rows.length).toBeGreaterThan(0)
    expect(rows.length).toBeLessThan(100)
    expect(rows.length).toBeLessThan(steps.length)
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

describe('An empty tape', () => {
  it('states that no steps were recorded rather than rendering an empty virtualised list', () => {
    render(<Tape steps={[]} />)

    expect(screen.getByText(/no steps were recorded/i)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Tape' })).not.toBeInTheDocument()
  })
})
