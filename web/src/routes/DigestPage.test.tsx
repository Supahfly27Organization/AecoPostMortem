import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
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
      subagentCount: 470,
      ruleCoverage: { state: 'notYetAnalyzed' },
      repositoryScope: {
        selectedRepository: 'aeco/AecoPostMortem',
        availableRepositories: ['aeco/AecoLedger', 'aeco/AecoPostMortem', 'aeco/Upfront'],
        sessionIds: ['session-1', 'session-2', 'session-3'],
        sessionLabels: {},
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
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText('src/hot.cs was read repeatedly')).toBeInTheDocument()
  })

  // Scenario 2 (issue #44): the digest states the scope it is ranking within, not only the ranking.
  it('states the corpus scope, so the ranking below it is read against a stated denominator', async () => {
    respondWith(digestWith())
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    const scope = await screen.findByRole('group', { name: /corpus scope/i })

    expect(scope).toHaveTextContent('35')
    expect(scope).toHaveTextContent('56,138')
    expect(scope).toHaveTextContent(/rules not yet analysed/i)
  })

  // Scenario 4: mid-ingest is a designed state, distinct from both empty states above.
  it('states that analysis is incomplete mid-ingest, rather than showing partial counts as final', async () => {
    respondWith(digestWith({ state: 'Incomplete' }))
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText(/analysis is incomplete/i)).toBeInTheDocument()
    expect(screen.getByRole('group', { name: /corpus scope/i })).toHaveAttribute(
      'data-provisional',
      'true',
    )
  })

  // Scenario 3: defaults to one repository, selectable.
  it('shows one repository at a time, offering the others as a selectable seam', async () => {
    respondWith(digestWith())
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    const select = await screen.findByRole('combobox', { name: 'Repository' })
    expect(select).toHaveValue('aeco/AecoPostMortem')
    expect(screen.getAllByRole('option')).toHaveLength(3)
  })

  // Mockup parity item #2: the session strip is threaded from the masthead's own repository
  // scope down to each row — this is the real, wired path a browser exercises, not just
  // FindingRow's own unit coverage of the prop in isolation.
  it('threads the corpus scope down to each row as the session strip', async () => {
    respondWith(digestWith())
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    const summary = await screen.findByRole('button', { expanded: false })
    const strip = summary.querySelector('[role="img"]')

    expect(strip).toHaveAttribute('aria-label', '2 of 3 sessions affected')
  })

  // Scenario 1: every row carries its evidence and provenance once expanded.
  it('expands a row to show its evidence, provenance badge and suggestion', async () => {
    const user = userEvent.setup()
    respondWith(digestWith())
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

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
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

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
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

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
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

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
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText(/judgment calls/i)).toBeInTheDocument()
    expect(
      screen.getByText('codebase-memory-mcp-search_graph fails often enough to be a missing capability'),
    ).toBeInTheDocument()
  })

  it('renders no "Judgment calls" section at all when there are no inferred findings', async () => {
    respondWith(digestWith())
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

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
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText(/checks that found nothing/i)).toBeInTheDocument()
    expect(screen.getByText('Hook Failure')).toBeInTheDocument()
    expect(screen.getByText(/0 found.*35 checked/)).toBeInTheDocument()
    expect(screen.getByText('Observed')).toBeInTheDocument()
  })

  it('renders no "Checks that found nothing" section at all when no check ran clean', async () => {
    respondWith(digestWith())
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('src/hot.cs was read repeatedly')

    expect(screen.queryByText(/checks that found nothing/i)).not.toBeInTheDocument()
  })

  // The date-range filter task's own design decision (`AecoPostMortem.Api/CLAUDE.md`'s "A date-range
  // filter re-scopes the whole analysis"): applying a range asks the server for a re-scoped digest,
  // it never merely hides rows from the response already in hand.
  it('re-fetches the digest with the applied date range as query parameters', async () => {
    const user = userEvent.setup()
    const requestedUrls: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()
        requestedUrls.push(url)
        return new Response(JSON.stringify(digestWith()), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        })
      }),
    )
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('src/hot.cs was read repeatedly')
    expect(requestedUrls).toHaveLength(1)
    expect(requestedUrls[0]).not.toContain('from=')

    await user.type(screen.getByLabelText('From'), '2026-06-01')
    await user.type(screen.getByLabelText('To'), '2026-06-30')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    await vi.waitFor(() => expect(requestedUrls).toHaveLength(2))
    expect(requestedUrls[1]).toContain('from=2026-06-01')
    expect(requestedUrls[1]).toContain('to=2026-06-30')
  })

  // The repository filter: `RepositorySelector` used to be a display-only seam — selecting another
  // repository changed the `<select>` and nothing else, so every repository but the server's own
  // most-sessions default was unreachable through the whole product. It now re-scopes the analysis
  // server-side, exactly as the date range above does.
  it('re-fetches the digest with the selected repository as a query parameter', async () => {
    const user = userEvent.setup()
    const requestedUrls: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()
        requestedUrls.push(url)
        return new Response(JSON.stringify(digestWith()), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        })
      }),
    )
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('src/hot.cs was read repeatedly')
    expect(requestedUrls).toHaveLength(1)
    expect(requestedUrls[0]).not.toContain('repository=')

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Repository' }),
      'aeco/AecoLedger',
    )

    await vi.waitFor(() => expect(requestedUrls).toHaveLength(2))
    // Read through URLSearchParams rather than asserting on the encoded text: a repository name
    // always contains a `/`, so a raw substring check would be asserting percent-encoding, not the
    // value actually sent.
    expect(new URL(requestedUrls[1], 'http://localhost').searchParams.get('repository')).toBe(
      'aeco/AecoLedger',
    )
  })

  // The re-fetch keeps the *previous* digest on screen (Important #4 above), so a selector driven
  // purely off the served scope would snap back to the old repository for the whole request and read
  // as having rejected the click. `DateRangeFilter` already shows the requested values rather than
  // the served ones for the same reason; the selector has to match.
  it('keeps the newly selected repository shown while its digest is still loading', async () => {
    const user = userEvent.setup()
    let callCount = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(() => {
        callCount += 1
        if (callCount === 1) {
          return Promise.resolve(
            new Response(JSON.stringify(digestWith()), {
              status: 200,
              headers: { 'content-type': 'application/json' },
            }),
          )
        }
        // Never resolves: the assertions below are all about the in-flight window.
        return new Promise<Response>(() => {})
      }),
    )
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('src/hot.cs was read repeatedly')
    const selector = screen.getByRole('combobox', { name: 'Repository' })
    await user.selectOptions(selector, 'aeco/AecoLedger')

    expect(selector).toHaveValue('aeco/AecoLedger')
    expect(screen.getByRole('status')).toHaveTextContent(/updating/i)
  })

  // The same reasoning `applyRange` already follows: a new repository re-scopes the whole ranked
  // list server-side, so the previous repository's page position has no meaning against the new one.
  it('returns to the first page when the repository changes', async () => {
    const user = userEvent.setup()
    const template = digestWith().rankedFindings[0]
    const manyFindings = Array.from({ length: 30 }, (_, index) => ({
      ...template,
      headline: `Finding number ${index + 1}`,
      recurrence: { key: `key-${index + 1}`, occurrences: [] },
    }))
    respondWith(digestWith({ rankedFindings: manyFindings }))
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('Finding number 1')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument()

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Repository' }),
      'aeco/AecoLedger',
    )

    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(screen.getByText('Finding number 1')).toBeInTheDocument()
  })

  // Real corpus check: the dominant repository serves 297 ranked findings, well past one page —
  // the pager slices the already-served list rather than the server sending only one page's worth.
  it('paginates the ranked findings list once it exceeds one page', async () => {
    const user = userEvent.setup()
    const template = digestWith().rankedFindings[0]
    const manyFindings = Array.from({ length: 30 }, (_, index) => ({
      ...template,
      headline: `Finding number ${index + 1}`,
      recurrence: { key: `key-${index + 1}`, occurrences: [] },
    }))
    respondWith(digestWith({ rankedFindings: manyFindings }))
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('Finding number 1')
    expect(screen.getByText('Finding number 25')).toBeInTheDocument()
    expect(screen.queryByText('Finding number 26')).not.toBeInTheDocument()
    expect(screen.getByText('Page 1 of 2')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Next page' }))

    expect(await screen.findByText('Finding number 26')).toBeInTheDocument()
    expect(screen.queryByText('Finding number 1')).not.toBeInTheDocument()
  })

  it('shows no pager at all when everything fits on one page', async () => {
    respondWith(digestWith())
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('src/hot.cs was read repeatedly')

    expect(screen.queryByRole('group', { name: 'Findings pages' })).not.toBeInTheDocument()
  })

  // Code review Important #5 test gap: `applyRange` resets `page` to 1, but nothing asserted it —
  // land on page 2, apply a range, and the page must read 1 again once the re-scoped digest loads.
  it('resets to page 1 after applying a new date range', async () => {
    const user = userEvent.setup()
    const template = digestWith().rankedFindings[0]
    const manyFindings = Array.from({ length: 30 }, (_, index) => ({
      ...template,
      headline: `Finding number ${index + 1}`,
      recurrence: { key: `key-${index + 1}`, occurrences: [] },
    }))
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        return new Response(JSON.stringify(digestWith({ rankedFindings: manyFindings })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        })
      }),
    )
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('Finding number 1')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument()

    await user.type(screen.getByLabelText('From'), '2026-06-01')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(screen.getByText('Finding number 1')).toBeInTheDocument()
  })

  // Code review Important #4: applying a filter used to blank the entire page (masthead, selector
  // and the filter control itself all unmounted) while the re-scoped digest loaded — a one-click
  // dead end mid-interaction and a lost filter control. The previously loaded digest must stay on
  // screen, with a distinct, non-alarming indicator that a re-fetch is under way.
  it('keeps the previously loaded digest on screen with an "updating" status while a new range loads', async () => {
    const user = userEvent.setup()
    let resolveSecond: (response: Response) => void = () => {}
    let callCount = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(() => {
        callCount += 1
        if (callCount === 1) {
          return Promise.resolve(
            new Response(JSON.stringify(digestWith()), {
              status: 200,
              headers: { 'content-type': 'application/json' },
            }),
          )
        }
        return new Promise<Response>((resolve) => {
          resolveSecond = resolve
        })
      }),
    )
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await screen.findByText('src/hot.cs was read repeatedly')

    await user.type(screen.getByLabelText('From'), '2026-06-01')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    // The previous digest, the masthead and the filter control itself are all still on screen —
    // nothing unmounts while the new range's own request is in flight.
    expect(screen.getByText('src/hot.cs was read repeatedly')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: /corpus scope/i })).toBeInTheDocument()
    expect(screen.getByRole('search', { name: 'Date range' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/updating/i)

    resolveSecond(
      new Response(JSON.stringify(digestWith({ rankedFindings: [] })), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    )

    await vi.waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument())
  })

  // Code review Important #3: every check reports `Ran` unconditionally, even over a population of
  // zero — a date range matching no sessions in the selected repository would otherwise render
  // "Every check ran and found nothing." and a clean-checks grid reading "0 found · 0 checked",
  // which is indistinguishable from "genuinely clean" even though nothing was actually looked at.
  it('states honestly that no sessions fall in the applied range, rather than "found nothing"', async () => {
    const user = userEvent.setup()
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        return new Response(
          JSON.stringify(
            digestWith({
              // The state the real server now serves for a range matching zero sessions — this
              // fixture used to say `Analyzed`, which the server no longer produces for an empty
              // scope. `silentChecks` is deliberately still non-empty here (the server would send
              // `[]` since #144): it proves this page suppresses the clean-checks grid on the state
              // alone, without relying on the list already being empty.
              state: 'NothingInScope',
              rankedFindings: [],
              silentChecks: [
                {
                  checkId: 'hook-failure',
                  population: 0,
                  findingCount: 0,
                  provenance: 'observed',
                  provenanceLabel: 'Observed — read directly from the session log.',
                },
              ],
              masthead: {
                ...digestWith().masthead,
                repositoryScope: { ...digestWith().masthead.repositoryScope, sessionIds: [] },
              },
            }),
          ),
          { status: 200, headers: { 'content-type': 'application/json' } },
        )
      }),
    )
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    await user.type(await screen.findByLabelText('From'), '2026-01-01')
    await user.type(screen.getByLabelText('To'), '2026-01-31')
    await user.click(screen.getByRole('button', { name: 'Apply' }))

    expect(await screen.findByText(/no sessions.*range/i)).toBeInTheDocument()
    expect(screen.queryByText(/every check ran and found nothing/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/checks that found nothing/i)).not.toBeInTheDocument()
  })

  // The case the date-range task explicitly left open ("an unfiltered digest with a truly empty
  // repository scope is a different, pre-existing case this task does not touch"): reachable with no
  // filter at all — an empty store, or a repository carrying no sessions — and it read "Every check
  // ran and found nothing." about a scope nothing ever looked at. The server now says which it is
  // (`DigestState.NothingInScope`), so this no longer depends on a filter being active.
  it('states that nothing was in scope with no filter applied, rather than "found nothing"', async () => {
    respondWith(
      digestWith({
        state: 'NothingInScope',
        rankedFindings: [],
        silentChecks: [],
        masthead: {
          ...digestWith().masthead,
          repositoryScope: { ...digestWith().masthead.repositoryScope, sessionIds: [] },
        },
      }),
    )
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText(/no sessions.*repository/i)).toBeInTheDocument()
    expect(screen.queryByText(/every check ran and found nothing/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/date range/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Findings pages' })).not.toBeInTheDocument()
  })

  // The distinction this whole state exists to protect: a real, non-empty scope where every check
  // genuinely ran and found nothing must still say so.
  it('still says every check ran and found nothing when the scope held real sessions', async () => {
    respondWith(digestWith({ state: 'Analyzed', rankedFindings: [], silentChecks: [] }))
    render(
      <MemoryRouter>
        <DigestPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText(/every check ran and found nothing/i)).toBeInTheDocument()
  })
})
