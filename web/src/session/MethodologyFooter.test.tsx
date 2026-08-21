import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MethodologyFooter } from './MethodologyFooter'
import type { SessionMasthead } from '../api/session'

function sessionMastheadWith(overrides: Partial<SessionMasthead> = {}): SessionMasthead {
  return {
    sessionId: 'session-1',
    repository: 'aeco/AecoPostMortem',
    branch: 'main',
    copilotVersion: '1.2.3',
    startedAt: '2026-08-16T14:03:00Z',
    endedAt: '2026-08-16T15:47:00Z',
    elapsedMs: 6_240_000,
    turnCount: 84,
    toolCallCount: 764,
    subagentCount: 20,
    skillCount: 55,
    modelCount: 2,
    contextSize: { kind: 'notRecorded' },
    ...overrides,
  }
}

describe('MethodologyFooter', () => {
  it('states what was measured, sourced from this session\'s own masthead', () => {
    render(<MethodologyFooter masthead={sessionMastheadWith()} />)

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent('84 turns')
    expect(footer).toHaveTextContent('764 tool calls')
    expect(footer).toHaveTextContent('20 subagents')
    expect(footer).toHaveTextContent('55 skill invocations')
    expect(footer).toHaveTextContent('16 Aug 2026')
  })

  it('states that tape rows are representative of the log, not a verbatim transcript', () => {
    render(<MethodologyFooter masthead={sessionMastheadWith()} />)

    expect(screen.getByText(/representative/i)).toBeInTheDocument()
  })

  it('states rule findings here are tool-choice checks, not code-content checks', () => {
    render(<MethodologyFooter masthead={sessionMastheadWith()} />)

    expect(screen.getByText(/tool-choice/i)).toBeInTheDocument()
  })

  it('explains the Thinking tab\'s readable-vs-encrypted split without asserting a live number', () => {
    render(<MethodologyFooter masthead={sessionMastheadWith()} />)

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent(/thinking tab/i)
    expect(footer).toHaveTextContent(/per model/i)
    // General, always-true context — never a specific percentage for this session up front.
    expect(footer).not.toHaveTextContent(/%/)
  })

  it('singularises counts of exactly one, without an "1 turns" typo', () => {
    render(
      <MethodologyFooter
        masthead={sessionMastheadWith({ turnCount: 1, toolCallCount: 1, subagentCount: 1, skillCount: 1 })}
      />,
    )

    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent('1 turn,')
    expect(footer).toHaveTextContent('1 tool call,')
    expect(footer).toHaveTextContent('1 subagent ')
    expect(footer).toHaveTextContent('1 skill invocation,')
    expect(footer).not.toHaveTextContent('1 turns')
    expect(footer).not.toHaveTextContent('1 tool calls')
    expect(footer).not.toHaveTextContent('1 subagents')
    expect(footer).not.toHaveTextContent('1 skill invocations')
  })
})
