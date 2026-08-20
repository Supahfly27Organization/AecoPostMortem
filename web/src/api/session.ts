// Mirrors AecoPostMortem.Api.SessionEnvelope (src/AecoPostMortem.Api/SessionEnvelope.cs). The route
// and the field shapes are the contract between the .NET host and this client; keep both in sync by
// hand until a generated client exists — the same discipline `appState.ts` documents for its own
// contract.

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

/** Mirrors `AecoPostMortem.Api.SessionRecordingStatusEnvelope` (FR-21 part 3 of 3, S-53, issue
 * #17). A closed three-shape union, the same discipline `SessionTokenFigures` already uses here —
 * `'complete'` is the only kind a caller may render the tape from as the session's final picture. */
export type SessionRecordingStatus =
  | { kind: 'complete' }
  | { kind: 'ingestIncomplete' }
  | { kind: 'reconstructionFailed'; skipped: string[] }

export interface SessionEnvelope {
  masthead: SessionMasthead
  steps: SessionTapeStep[]
  status: SessionRecordingStatus
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
