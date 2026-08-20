import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DigestPage } from './DigestPage'
import { DigestRoute, type DigestEnvelope } from '../api/digest'

function respondWith(digest: DigestEnvelope) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      expect(url).toContain(DigestRoute)
      return new Response(JSON.stringify(digest), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    }),
  )
}

function digestWith(overrides: Partial<DigestEnvelope> = {}): DigestEnvelope {
  return {
    masthead: {
      sessionCount: 35,
      spanStart: '2026-05-01T00:00:00Z',
      spanEnd: '2026-08-19T00:00:00Z',
      repositoryCount: 3,
      eventCount: 56_138,
      toolCallCount: 12_345,
      ruleCoverage: 'NotYetAnalyzed',
      repositoryScope: {
        selectedRepository: 'aeco/AecoPostMortem',
        availableRepositories: ['aeco/AecoLedger', 'aeco/AecoPostMortem', 'aeco/Upfront'],
      },
    },
    state: 'Analyzed',
    rankedFindings: [
      {
        kind: 'general',
        class: 'waste',
        provenance: 'derived',
        evidence: [{ field: 'data.path', value: 'src/hot.cs' }],
        recurrence: {
          key: 'src/hot.cs',
          occurrences: [
            { sessionId: 'session-1', ruleSetVersion: null },
            { sessionId: 'session-2', ruleSetVersion: null },
          ],
        },
        suggestion: { state: 'present', text: 'Name `rg` instead of repeated `view` calls.' },
        operatorResponse: 'ignored',
      },
    ],
    ...overrides,
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DigestPage', () => {
  it('renders the masthead and the ranked findings once the digest loads', async () => {
    respondWith(digestWith())
    render(<DigestPage />)

    expect(await screen.findByText('src/hot.cs')).toBeInTheDocument()
  })

  // Scenario 3: defaults to one repository, selectable.
  it('shows one repository at a time, offering the others as a selectable seam', async () => {
    respondWith(digestWith())
    render(<DigestPage />)

    const select = await screen.findByRole('combobox', { name: 'Repository' })
    expect(select).toHaveValue('aeco/AecoPostMortem')
    expect(screen.getAllByRole('option')).toHaveLength(3)
  })

  // Scenario 1: every row carries its evidence and provenance once expanded.
  it('expands a row to show its evidence, provenance badge and suggestion', async () => {
    const user = userEvent.setup()
    respondWith(digestWith())
    render(<DigestPage />)

    await user.click(await screen.findByRole('button', { expanded: false }))

    expect(screen.getByText('data.path')).toBeInTheDocument()
    expect(screen.getByText('Derived')).toBeInTheDocument()
    expect(screen.getByText('Name `rg` instead of repeated `view` calls.')).toBeInTheDocument()
  })

  it('states honestly that no check has run yet, rather than rendering an empty list unexplained', async () => {
    respondWith(
      digestWith({
        state: 'NotYetAnalyzed',
        rankedFindings: [],
      }),
    )
    render(<DigestPage />)

    expect(await screen.findByText(/no check has run/i)).toBeInTheDocument()
  })

  it('reports an unreachable API distinctly, rather than showing nothing', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new TypeError('Failed to fetch')
      }),
    )
    render(<DigestPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('aecopostmortem serve')
  })
})
