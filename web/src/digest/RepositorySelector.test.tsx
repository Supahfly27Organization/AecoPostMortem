import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { RepositorySelector } from './RepositorySelector'

/** Scenario 3 (issue #45) / PRD Part 8 Q5: "Given a store holding more than one repository, when
 * the digest renders, then it shows one repository at a time, selectable." Default: one
 * repository, selectable — the selector is the seam for a later cross-repository view, not that
 * view itself. */
describe('RepositorySelector', () => {
  it('shows the single default repository when it is the only one in the store', () => {
    render(
      <RepositorySelector
        scope={{ selectedRepository: 'aeco/AecoPostMortem', availableRepositories: ['aeco/AecoPostMortem'] }}
      />,
    )

    expect(screen.getByRole('combobox', { name: 'Repository' })).toHaveValue('aeco/AecoPostMortem')
  })

  it('offers every repository the store holds, with the selected one shown', () => {
    render(
      <RepositorySelector
        scope={{
          selectedRepository: 'aeco/AecoPostMortem',
          availableRepositories: ['aeco/AecoLedger', 'aeco/AecoPostMortem', 'aeco/Upfront'],
        }}
      />,
    )

    const select = screen.getByRole('combobox', { name: 'Repository' })
    expect(select).toHaveValue('aeco/AecoPostMortem')
    expect(screen.getAllByRole('option')).toHaveLength(3)
  })

  it('is selectable — choosing another repository reports the choice back', async () => {
    const onSelect = vi.fn()
    const user = userEvent.setup()

    render(
      <RepositorySelector
        scope={{
          selectedRepository: 'aeco/AecoPostMortem',
          availableRepositories: ['aeco/AecoLedger', 'aeco/AecoPostMortem'],
        }}
        onSelect={onSelect}
      />,
    )

    await user.selectOptions(screen.getByRole('combobox', { name: 'Repository' }), 'aeco/AecoLedger')

    expect(onSelect).toHaveBeenCalledWith('aeco/AecoLedger')
  })
})
