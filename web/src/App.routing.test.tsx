import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import type { AppStateReport } from './api/appState'

/** S-48, Scenario 1: "The three surfaces are routable." Every route resolves under a shared
 * shell, and a surface with no real content yet names the release it arrives in. */
describe('App routing', () => {
  const ready: AppStateReport = { kind: 'ready', message: 'Ready.', fixCommand: null }

  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify(ready), { status: 200 })),
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
    expect(screen.getByText(/arrives in Release 1 \(S-36 \/ S-54\)/)).toBeInTheDocument()
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
