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
})
