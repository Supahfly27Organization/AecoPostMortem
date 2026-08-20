// Mirrors AecoPostMortem.Api.SessionEnvelope (src/AecoPostMortem.Api/SessionEnvelope.cs) and
// AecoPostMortem.Api.StepEvidenceEnvelope (src/AecoPostMortem.Api/StepEvidenceEnvelope.cs). The
// route and the field shapes are the contract between the .NET host and this client; keep both in
// sync by hand until a generated client exists — the same discipline `appState.ts` documents for its
// own contract.

import type { FindingEnvelope } from './digest'

export type SessionTapeStepKind = 'prompt' | 'hook' | 'skill' | 'toolCall' | 'mcpCall'

export type OwnerKind = 'main' | 'agent'

export interface SessionTapeStep {
  kind: SessionTapeStepKind
  stepId: string
  label: string
  timestamp: string
  offsetMs: number
  ownerKind: OwnerKind
  agentId: string | null
}

export type SessionTokenFigures =
  | {
      kind: 'observed'
      inputTokens: number
      outputTokens: number
      cacheReadTokens: number | null
      cacheWriteTokens: number | null
      reasoningTokens: number | null
      modelCount: number | null
    }
  | { kind: 'notRecorded' }

export interface SessionMasthead {
  sessionId: string
  repository: string | null
  branch: string | null
  copilotVersion: string
  elapsedMs: number | null
  turnCount: number
  toolCallCount: number
  subagentCount: number
  skillCount: number
  modelCount: number | null
  contextSize: SessionTokenFigures
}

/** One chip on the Flight Recorder's chip row (FR-21 part 2 of 3, S-52, issue #16): a finding
 * affecting this session, plus how many sessions across the corpus it affects — "with its count"
 * (the story's own Gherkin wording). */
export interface SessionFindingChip {
  finding: FindingEnvelope
  sessionsAffected: number
}

export interface SessionEnvelope {
  masthead: SessionMasthead
  steps: SessionTapeStep[]
  /** Scenario 3's own designed state: an empty array *is* "no findings affect this session",
   * rendered explicitly by `SessionPage`, never a blank area. */
  findings: SessionFindingChip[]
}

export function sessionRoute(sessionId: string): string {
  return `/api/sessions/${encodeURIComponent(sessionId)}`
}

/** Throws on a non-2xx response (including a 404 for an unknown session id) or a network
 * failure; callers (see `useSession`) turn that into a state a component can render rather than
 * an unhandled rejection — the same contract `fetchAppState` documents. */
export async function fetchSession(sessionId: string, signal?: AbortSignal): Promise<SessionEnvelope> {
  const route = sessionRoute(sessionId)
  const response = await fetch(route, { signal })

  if (!response.ok) {
    throw new Error(`GET ${route} failed with status ${response.status}`)
  }

  return (await response.json()) as SessionEnvelope
}

/** `ThinkingEnvelope`'s two closed shapes (`StepEvidenceEnvelope.cs`) — "no reasoning" is a stated
 * value, never a blank Thinking panel. */
export type ThinkingEnvelope =
  | { kind: 'present'; text: string }
  | { kind: 'unavailable'; reason: string }

/** `RawStepEventEnvelope`'s two closed shapes — the edge case's own words: a step whose raw event
 * was skipped at ingest "shows that fact rather than an empty panel." */
export type RawStepEventEnvelope =
  | { kind: 'present'; eventType: string; payload: string }
  | { kind: 'skipped'; reason: string }

export interface StepEvidenceEnvelope {
  thinking: ThinkingEnvelope
  raw: RawStepEventEnvelope
}

export function stepEvidenceRoute(sessionId: string, stepId: string, kind: SessionTapeStepKind): string {
  return `/api/sessions/${encodeURIComponent(sessionId)}/steps/${encodeURIComponent(stepId)}?kind=${encodeURIComponent(kind)}`
}

/** Same throw-on-non-2xx contract as `fetchSession`. */
export async function fetchStepEvidence(
  sessionId: string,
  stepId: string,
  kind: SessionTapeStepKind,
  signal?: AbortSignal,
): Promise<StepEvidenceEnvelope> {
  const route = stepEvidenceRoute(sessionId, stepId, kind)
  const response = await fetch(route, { signal })

  if (!response.ok) {
    throw new Error(`GET ${route} failed with status ${response.status}`)
  }

  return (await response.json()) as StepEvidenceEnvelope
}
