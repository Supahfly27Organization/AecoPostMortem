import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DateRangeFilter } from './DateRangeFilter'

describe('DateRangeFilter', () => {
  it('renders empty inputs when no filter is active', () => {
    render(<DateRangeFilter from={null} to={null} onApply={vi.fn()} />)

    expect(screen.getByLabelText('From')).toHaveValue('')
    expect(screen.getByLabelText('To')).toHaveValue('')
  })

  it('pre-fills the inputs from the currently active filter', () => {
    render(<DateRangeFilter from="2026-06-01" to="2026-06-30" onApply={vi.fn()} />)

    expect(screen.getByLabelText('From')).toHaveValue('2026-06-01')
    expect(screen.getByLabelText('To')).toHaveValue('2026-06-30')
  })

  it('applies the entered range on submit', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn()
    render(<DateRangeFilter from={null} to={null} onApply={onApply} />)

    await user.type(screen.getByLabelText('From'), '2026-06-01')
    await user.type(screen.getByLabelText('To'), '2026-06-30')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(onApply).toHaveBeenCalledWith('2026-06-01', '2026-06-30')
  })

  // Both bounds are independent — a caller may filter with only one supplied.
  it('applies a range with only one bound supplied', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn()
    render(<DateRangeFilter from={null} to={null} onApply={onApply} />)

    await user.type(screen.getByLabelText('From'), '2026-06-01')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(onApply).toHaveBeenCalledWith('2026-06-01', null)
  })

  it('clears an active filter and reports both bounds as null', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn()
    render(<DateRangeFilter from="2026-06-01" to="2026-06-30" onApply={onApply} />)

    await user.click(screen.getByRole('button', { name: 'Clear' }))

    expect(onApply).toHaveBeenCalledWith(null, null)
  })

  it('the whole control is reachable as one named group', () => {
    render(<DateRangeFilter from={null} to={null} onApply={vi.fn()} />)

    expect(screen.getByRole('search', { name: 'Date range' })).toBeInTheDocument()
  })

  // Code review Critical #1: the server correctly 400s an inverted range, but nothing stopped an
  // operator from ever sending one — a two-click mistake (From after To) reached a dead-end error
  // page with a false "can't reach the API" message and no way back except a reload. This is the
  // client-side guard that keeps that request from ever being sent.
  it('refuses to submit an inverted range and states why, rather than calling onApply', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn()
    render(<DateRangeFilter from={null} to={null} onApply={onApply} />)

    await user.type(screen.getByLabelText('From'), '2026-06-30')
    await user.type(screen.getByLabelText('To'), '2026-06-01')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(onApply).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(/from.*on or before.*to/i)
  })

  it('submits successfully once the inverted range is corrected', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn()
    render(<DateRangeFilter from={null} to={null} onApply={onApply} />)

    await user.type(screen.getByLabelText('From'), '2026-06-30')
    await user.type(screen.getByLabelText('To'), '2026-06-01')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    expect(onApply).not.toHaveBeenCalled()

    await user.clear(screen.getByLabelText('To'))
    await user.type(screen.getByLabelText('To'), '2026-07-01')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(onApply).toHaveBeenCalledWith('2026-06-30', '2026-07-01')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('guides the picker itself: To carries a min matching the entered From', () => {
    render(<DateRangeFilter from="2026-06-01" to={null} onApply={vi.fn()} />)

    expect(screen.getByLabelText('To')).toHaveAttribute('min', '2026-06-01')
  })

  it('guides the picker itself: From carries a max matching the entered To', () => {
    render(<DateRangeFilter from={null} to="2026-06-30" onApply={vi.fn()} />)

    expect(screen.getByLabelText('From')).toHaveAttribute('max', '2026-06-30')
  })

  // Code review Minor M3/M5 (both reviews): neither label said what the dates filter on (a
  // session's own start, not any occurrence within it) or that the boundary is UTC.
  it('states what it filters on and that the boundary is UTC', () => {
    render(<DateRangeFilter from={null} to={null} onApply={vi.fn()} />)

    expect(screen.getByText(/session start date/i)).toBeInTheDocument()
    expect(screen.getByText(/utc/i)).toBeInTheDocument()
  })
})
