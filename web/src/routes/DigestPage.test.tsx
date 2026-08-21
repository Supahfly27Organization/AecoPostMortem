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
        sessionIds: ['session-1', 'session-2', 'session-3'],
      },
    },
    state: 'Analyzed',
    rankedFindings: [
      {
        kind: 'general',
        class: 'waste',
        provenance: 'derived',
        headline: 'src/hot.cs was read repeatedly',
        evidence: [{ field: 'data.path', value: 'src/hot.cs' }],
        recurrence: {
          key: 'src/hot.cs',
          occurrences: [
            { sessionId: 'session-1', ruleSetVersion: null },
            { sessionId: 'session-2', ruleSetVersion: null },
          ],
        },
        sessionsAffected: 2,
        suggestion: { state: 'present', text: 'Name `rg` instead of repeated `view` calls.' },
        operatorResponse: 'ignored',
      },
    ],
    inferredFindings: [],
    silentChecks: [],
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

    expect(await screen.findByText('src/hot.cs was read repeatedly')).toBeInTheDocument()
  })

  // Scenario 2 (issue #44): the digest states the scope it is ranking within, not only the ranking.
  it('states the corpus scope, so the ranking below it is read against a stated denominator', async () => {
    respondWith(digestWith())
    render(<DigestPage />)

    const scope = await screen.findByRole('group', { name: /corpus scope/i })

    expect(scope).toHaveTextContent('35')
    expect(scope).toHaveTextContent('56,138')
    expect(scope).toHaveTextContent(/rules not yet analysed/i)
  })

  // Scenario 4: mid-ingest is a designed state, distinct from both empty states above.
  it('states that analysis is incomplete mid-ingest, rather than showing partial counts as final', async () => {
    respondWith(digestWith({ state: 'Incomplete' }))
    render(<DigestPage />)

    expect(await screen.findByText(/analysis is incomplete/i)).toBeInTheDocument()
    expect(screen.getByRole('group', { name: /corpus scope/i })).toHaveAttribute(
      'data-provisional',
      'true',
    )
  })

  // Scenario 3: defaults to one repository, selectable.
  it('shows one repository at a time, offering the others as a selectable seam', async () => {
    respondWith(digestWith())
    render(<DigestPage />)

    const select = await screen.findByRole('combobox', { name: 'Repository' })
    expect(select).toHaveValue('aeco/AecoPostMortem')
    expect(screen.getAllByRole('option')).toHaveLength(3)
  })

  // Mockup parity item #2: the session strip is threaded from the masthead's own repository
  // scope down to each row — this is the real, wired path a browser exercises, not just
  // FindingRow's own unit coverage of the prop in isolation.
  it('threads the corpus scope down to each row as the session strip', async () => {
    respondWith(digestWith())
    render(<DigestPage />)

    const summary = await screen.findByRole('button', { expanded: false })
    const strip = summary.querySelector('[role="img"]')

    expect(strip).toHaveAttribute('aria-label', '2 of 3 sessions affected')
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

  // Regression: `AecoPostMortem.Findings.Digest.cs` derives `DigestState.Analyzed` from whether any
  // check ran, independent of how many findings resulted — `rankedFindings` can be empty while
  // `inferredFindings` is not (every check that ran happened to only produce hypotheses). "Every
  // check ran and found nothing." would be self-contradictory directly above a populated "Judgment
  // calls" section, so that message must also require `inferredFindings` to be empty.
  it('does not claim "found nothing" when the ranked list is empty but a judgment call exists', async () => {
    respondWith(
      digestWith({
        rankedFindings: [],
        inferredFindings: [
          {
            kind: 'general',
            class: 'missingCapability',
            provenance: 'inferred',
            headline: 'codebase-memory-mcp-search_graph fails often enough to be a missing capability',
            evidence: [{ field: 'data.tool', value: 'codebase-memory-mcp-search_graph' }],
            recurrence: {
              key: 'codebase-memory-mcp-search_graph',
              occurrences: [{ sessionId: 'session-9', ruleSetVersion: null }],
            },
            sessionsAffected: 1,
            suggestion: { state: 'absent' },
            operatorResponse: 'ignored',
          },
        ],
      }),
    )
    render(<DigestPage />)

    expect(await screen.findByText(/judgment calls/i)).toBeInTheDocument()
    expect(screen.queryByText(/found nothing/i)).not.toBeInTheDocument()
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

  // FR-48 (issue #52, S-42): `DigestEnvelope.InferredFindings` is real, served data — every
  // hypothesis-level finding, kept out of `rankedFindings` on the server side already. This is the
  // gap issue's own worked example: the field arrives on the wire and, before this fix, was silently
  // dropped because `DigestEnvelope` (this file's own `../api/digest`) never declared it.
  it('renders inferred findings in their own section, separate from the ranked list', async () => {
    respondWith(
      digestWith({
        inferredFindings: [
          {
            kind: 'general',
            class: 'missingCapability',
            provenance: 'inferred',
            headline: 'codebase-memory-mcp-search_graph fails often enough to be a missing capability',
            evidence: [{ field: 'data.tool', value: 'codebase-memory-mcp-search_graph' }],
            recurrence: {
              key: 'codebase-memory-mcp-search_graph',
              occurrences: [{ sessionId: 'session-9', ruleSetVersion: null }],
            },
            sessionsAffected: 1,
            suggestion: { state: 'absent' },
            operatorResponse: 'ignored',
          },
        ],
      }),
    )
    render(<DigestPage />)

    expect(await screen.findByText(/judgment calls/i)).toBeInTheDocument()
    expect(
      screen.getByText('codebase-memory-mcp-search_graph fails often enough to be a missing capability'),
    ).toBeInTheDocument()
  })

  it('renders no "Judgment calls" section at all when there are no inferred findings', async () => {
    respondWith(digestWith())
    render(<DigestPage />)

    await screen.findByText('src/hot.cs was read repeatedly')

    expect(screen.queryByText(/judgment calls/i)).not.toBeInTheDocument()
  })

  // Mockup parity item #6 (`docs/product-superpowers/discovery/mockups/digest.html`'s "Checks that
  // found nothing" section): a check that ran clean states its population, its zero count and its
  // provenance, so silence never reads as "never looked".
  it('renders a card for each silent check, naming its population and provenance', async () => {
    respondWith(
      digestWith({
        silentChecks: [
          {
            checkId: 'hook-failure',
            population: 35,
            findingCount: 0,
            provenance: 'observed',
            provenanceLabel: 'Observed — read directly from the session log.',
          },
        ],
      }),
    )
    render(<DigestPage />)

    expect(await screen.findByText(/checks that found nothing/i)).toBeInTheDocument()
    expect(screen.getByText('Hook Failure')).toBeInTheDocument()
    expect(screen.getByText(/0 found.*35 checked/)).toBeInTheDocument()
    expect(screen.getByText('Observed')).toBeInTheDocument()
  })

  it('renders no "Checks that found nothing" section at all when no check ran clean', async () => {
    respondWith(digestWith())
    render(<DigestPage />)

    await screen.findByText('src/hot.cs was read repeatedly')

    expect(screen.queryByText(/checks that found nothing/i)).not.toBeInTheDocument()
  })
})
