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
  // FR-25 (S-12, issue #21): a 'skill' step's plugin and version, carried alongside `label` (the
  // skill's own name) rather than folded into it. Null for every other step kind, and for a skill
  // Copilot recorded no plugin for.
  pluginName: string | null
  pluginVersion: string | null
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

export interface SessionEnvelope {
  masthead: SessionMasthead
  steps: SessionTapeStep[]
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
