import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { AppStateRoute, type AppStateReport } from './api/appState'
import { DigestRoute } from './api/digest'

/** S-48, Scenario 1: "The three surfaces are routable." Every route resolves under a shared
 * shell. The digest (S-36/S-54) now has real content; the other two remain named placeholders
 * until their own stories build them. */
describe('App routing', () => {
  const ready: AppStateReport = { kind: 'ready', message: 'Ready.', fixCommand: null }

  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()

        if (url.includes(AppStateRoute)) {
          return new Response(JSON.stringify(ready), { status: 200 })
        }

        if (url.includes(DigestRoute)) {
          // No real /api/digest endpoint exists yet (web/src/api/digest.ts) — routing only cares
          // that DigestPage renders its own content, not that a digest loads successfully here.
          return new Response('', { status: 404 })
        }

        throw new Error(`Unexpected fetch: ${url}`)
      }),
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reaches the Process Digest at the root route', () => {
    render(
      <MemoryRouter initialEntries={['/']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Process Digest' })).toBeInTheDocument()
  })

  it('reaches the session view and names its own arrival release', () => {
    render(
      <MemoryRouter initialEntries={['/sessions']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Session Flight Recorder' })).toBeInTheDocument()
    expect(screen.getByText(/arrives in Release 1 \(S-08\)/)).toBeInTheDocument()
  })

  it('reaches the Rules Inventory and names a different arrival release', () => {
    render(
      <MemoryRouter initialEntries={['/rules']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Rules Inventory' })).toBeInTheDocument()
    expect(screen.getByText(/arrives in Release 2 \(S-22\)/)).toBeInTheDocument()
  })

  it('exposes navigation to all three surfaces from every route', () => {
    render(
      <MemoryRouter initialEntries={['/rules']}>
        <App />
      </MemoryRouter>,
    )

    const nav = screen.getByRole('navigation', { name: 'Surfaces' })
    expect(nav).toHaveTextContent('Digest')
    expect(nav).toHaveTextContent('Session view')
    expect(nav).toHaveTextContent('Rules Inventory')
  })
})
