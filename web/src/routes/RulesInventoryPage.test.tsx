import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RulesInventoryPage } from './RulesInventoryPage'
import {
  RulesInventoryRoute,
  type RulesInventoryEnvelope,
  type RulesInventoryRowEnvelope,
} from '../api/rulesInventory'

const olderVersion = {
  repository: 'supahfly27/UpFront',
  hash: 'hash-older',
  firstSessionId: 'session-1',
  lastSessionId: 'session-3',
  sessionCount: 3,
}

const newerVersion = {
  repository: 'supahfly27/UpFront',
  hash: 'hash-newer',
  firstSessionId: 'session-4',
  lastSessionId: 'session-7',
  sessionCount: 4,
}

function row(overrides: Partial<RulesInventoryRowEnvelope> = {}): RulesInventoryRowEnvelope {
  return {
    sourceFile: 'CLAUDE.md',
    text: 'Prefer the index over a broad file search.',
    status: { status: 'watched', label: 'Watched' },
    sessionIds: ['session-1', 'session-2'],
    inForceFrom: '2026-05-01T09:00:00Z',
    inForceUntil: '2026-05-21T09:00:00Z',
    retirement: { state: 'inForce' },
    adherenceFrozenAt: null,
    ...overrides,
  }
}

function inventoryWith(overrides: Partial<RulesInventoryEnvelope> = {}): RulesInventoryEnvelope {
  const rows = overrides.rows ?? [row()]

  return {
    selectedVersion: olderVersion,
    availableVersions: [olderVersion, newerVersion],
    state: 'Listed',
    rows,
    statusCounts: {
      watched: 4,
      checkableNotYetBuilt: 9,
      notCheckable: 9,
      notARule: 21,
      total: 43,
    },
    ...overrides,
  }
}

function respondWith(...inventories: RulesInventoryEnvelope[]) {
  let call = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      expect(url).toContain(RulesInventoryRoute)
      const inventory = inventories[Math.min(call, inventories.length - 1)]
      call += 1
      return new Response(JSON.stringify(inventory), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    }),
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Rules Inventory (FR-40, S-22)', () => {
  it('gives every statement exactly one status', async () => {
    respondWith(
      inventoryWith({
        rows: [
          row({ text: 'A.', status: { status: 'watched', label: 'Watched' } }),
          row({
            text: 'B.',
            status: { status: 'checkableNotYetBuilt', label: 'Checkable — not yet built' },
          }),
          row({
            text: 'C.',
            status: {
              status: 'notCheckable',
              label: 'Not checkable',
              reason: 'The logs record no such event.',
            },
          }),
          row({ text: 'D.', status: { status: 'notARule', label: 'Not a rule' } }),
        ],
      }),
    )

    render(<RulesInventoryPage />)

    const statements = await screen.findAllByRole('row', { name: /statement/i })
    expect(statements).toHaveLength(4)
    for (const statement of statements) {
      expect(within(statement).getAllByTestId('rule-status')).toHaveLength(1)
    }
  })

  it('states the reason a statement is not checkable', async () => {
    respondWith(
      inventoryWith({
        rows: [
          row({
            status: {
              status: 'notCheckable',
              label: 'Not checkable',
              reason: 'Copilot logs no such event.',
            },
          }),
        ],
      }),
    )

    render(<RulesInventoryPage />)

    expect(await screen.findByText(/Copilot logs no such event\./)).toBeInTheDocument()
  })

  it('shows each row its source file and every session carrying it', async () => {
    respondWith(
      inventoryWith({ rows: [row({ sourceFile: 'AGENTS.md', sessionIds: ['session-1', 'session-2'] })] }),
    )

    render(<RulesInventoryPage />)

    const statement = await screen.findByRole('row', { name: /statement/i })
    expect(within(statement).getByText('AGENTS.md')).toBeInTheDocument()
    expect(within(statement).getByText(/session-1/)).toBeInTheDocument()
    expect(within(statement).getByText(/session-2/)).toBeInTheDocument()
  })

  it('states the first and last in-force dates', async () => {
    respondWith(
      inventoryWith({
        rows: [row({ inForceFrom: '2026-05-01T09:00:00Z', inForceUntil: '2026-05-21T09:00:00Z' })],
      }),
    )

    render(<RulesInventoryPage />)

    const statement = await screen.findByRole('row', { name: /statement/i })
    expect(within(statement).getByText(/2026-05-01T09:00:00Z/)).toBeInTheDocument()
    expect(within(statement).getByText(/2026-05-21T09:00:00Z/)).toBeInTheDocument()
  })

  it('states that no rules were found rather than rendering an empty table', async () => {
    respondWith(inventoryWith({ state: 'NoInstructionBlocks', rows: [] }))

    render(<RulesInventoryPage />)

    expect(await screen.findByText(/no rules were found/i)).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('distinguishes blocks that carried no list item from no block at all', async () => {
    respondWith(inventoryWith({ state: 'BlocksCarriedNoStatements', rows: [] }))

    render(<RulesInventoryPage />)

    expect(await screen.findByText(/carried no list item/i)).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('keeps a retired rule visible with its adherence frozen at the removal date', async () => {
    respondWith(
      inventoryWith({
        rows: [
          row({
            text: 'A rule since removed.',
            retirement: { state: 'retired', retiredAt: '2026-05-23T09:00:00Z' },
            adherenceFrozenAt: '2026-05-23T09:00:00Z',
          }),
        ],
      }),
    )

    render(<RulesInventoryPage />)

    const statement = await screen.findByRole('row', { name: /statement/i })
    expect(within(statement).getByText('A rule since removed.')).toBeInTheDocument()
    expect(within(statement).getByText(/retired/i)).toBeInTheDocument()
    expect(within(statement).getByText(/2026-05-23T09:00:00Z/)).toBeInTheDocument()
  })

  it('names the one rule-set version it is showing', async () => {
    respondWith(inventoryWith())

    render(<RulesInventoryPage />)

    const scope = await screen.findByRole('region', { name: /rule-set version/i })
    expect(scope).toHaveTextContent('hash-older')
    expect(scope).toHaveTextContent('supahfly27/UpFront')
    expect(scope).toHaveTextContent('3')
  })

  it('offers the other versions without rendering them at the same time', async () => {
    respondWith(inventoryWith())

    render(<RulesInventoryPage />)

    const selector = await screen.findByRole('combobox', { name: /rule-set version/i })
    expect(within(selector).getAllByRole('option')).toHaveLength(2)
    expect(screen.getAllByRole('row', { name: /statement/i })).toHaveLength(1)
  })

  // availableVersions arrives in the repository's own chronological order (RulesInventory
  // .ChronologicalVersions), so the last entry is the most recent — the one version in which
  // nothing is retired. Session hashes are opaque, so without saying so the picker is unreadable.
  it('marks which offered version is the most recent', async () => {
    respondWith(inventoryWith())

    render(<RulesInventoryPage />)

    const options = within(await screen.findByRole('combobox', { name: /rule-set version/i })).getAllByRole(
      'option',
    )

    expect(options.at(-1)).toHaveTextContent(/most recent/i)
    expect(options[0]).not.toHaveTextContent(/most recent/i)
  })

  it('re-requests the inventory when another version is selected', async () => {
    respondWith(
      inventoryWith(),
      inventoryWith({ selectedVersion: newerVersion, rows: [row({ text: 'The newer rule.' })] }),
    )

    render(<RulesInventoryPage />)

    const selector = await screen.findByRole('combobox', { name: /rule-set version/i })
    await userEvent.selectOptions(selector, 'hash-newer')

    expect(await screen.findByText('The newer rule.')).toBeInTheDocument()
    expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toContain('hash-newer')
  })

  it('shows the status breakdown without emphasising "Not a rule" as a problem count', async () => {
    respondWith(inventoryWith())

    render(<RulesInventoryPage />)

    const breakdown = await screen.findByRole('region', { name: /status breakdown/i })
    const notARule = within(breakdown).getByTestId('status-count-notARule')

    expect(notARule).toHaveTextContent('21')
    expect(within(breakdown).getAllByTestId(/^status-count-/)).toHaveLength(4)
    for (const tile of within(breakdown).getAllByTestId(/^status-count-/)) {
      expect(tile).toHaveAttribute('data-emphasis', 'neutral')
    }
  })

  it('renders its own message when the API cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })))

    render(<RulesInventoryPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the local api/i)
  })
})
