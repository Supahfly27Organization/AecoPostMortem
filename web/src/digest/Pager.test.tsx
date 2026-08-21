import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Pager } from './Pager'

describe('Pager', () => {
  it('renders nothing when everything fits on one page', () => {
    const { container } = render(<Pager page={1} pageCount={1} onChange={vi.fn()} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('states the current page out of the total', () => {
    render(<Pager page={2} pageCount={12} onChange={vi.fn()} />)

    expect(screen.getByText('Page 2 of 12')).toBeInTheDocument()
  })

  it('disables "Previous" on the first page', () => {
    render(<Pager page={1} pageCount={12} onChange={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next page' })).toBeEnabled()
  })

  it('disables "Next" on the last page', () => {
    render(<Pager page={12} pageCount={12} onChange={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeEnabled()
  })

  it('moves forward one page at a time', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Pager page={2} pageCount={12} onChange={onChange} />)

    await user.click(screen.getByRole('button', { name: 'Next page' }))

    expect(onChange).toHaveBeenCalledWith(3)
  })

  it('moves backward one page at a time', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Pager page={2} pageCount={12} onChange={onChange} />)

    await user.click(screen.getByRole('button', { name: 'Previous page' }))

    expect(onChange).toHaveBeenCalledWith(1)
  })

  it('is reachable as one named group', () => {
    render(<Pager page={1} pageCount={12} onChange={vi.fn()} />)

    expect(screen.getByRole('group', { name: 'Findings pages' })).toBeInTheDocument()
  })
})
