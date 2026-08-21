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
  // Mockup parity item #14: the session's own wall-clock start/end, alongside `elapsedMs`'s
  // duration. `endedAt` is null under the identical condition `elapsedMs` is — this session never
  // recorded `session.shutdown`.
  startedAt: string
  endedAt: string | null
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

/** One chip on the Flight Recorder's chip row (FR-21 part 2 of 3, S-52, issue #16): a finding
 * affecting this session, plus how many sessions across the corpus it affects — "with its count"
 * (the story's own Gherkin wording). */
export interface SessionFindingChip {
  finding: FindingEnvelope
  sessionsAffected: number
}

/** Mirrors `AecoPostMortem.Data.Execution.Agent.AgentOutcome`. */
export type AgentOutcome = 'running' | 'completed' | 'completedCostUnknown' | 'failed'

/** FR-22 (S-09, issue #18): a subagent lane's own report — "the report it actually produced" (the
 * story's own wording), never the parent's truncated `read_agent` completion. A closed three-shape
 * union, the same discipline `SessionRecordingStatus` already uses here: which of "a real report",
 * "nothing recorded" or "the subagent failed" applies is a stated value, never inferred from which
 * fields happen to be present. */
export type SubagentOutputEnvelope =
  | { kind: 'present'; text: string }
  | { kind: 'notRecorded'; reason: string }
  | { kind: 'failed'; error: string }

/** FR-22 (S-09, issue #18): one subagent's own lane — its identity, how it finished, and the report
 * resolved from its own message stream. */
export interface SessionAgentLane {
  agentId: string
  parentAgentId: string | null
  name: string
  displayName: string
  outcome: AgentOutcome
  error: string | null
  output: SubagentOutputEnvelope
}

export interface SessionEnvelope {
  masthead: SessionMasthead
  steps: SessionTapeStep[]
  status: SessionRecordingStatus
  /** Scenario 3's own designed state: an empty array *is* "no findings affect this session",
   * rendered explicitly by `SessionPage`, never a blank area. */
  findings: SessionFindingChip[]
  /** FR-22 (S-09, issue #18): one entry per subagent this session spawned. An empty array is the
   * designed "no subagents" state, the same discipline `findings` already establishes. */
  lanes: SessionAgentLane[]
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

/** FR-23 (S-10, issue #19): one model's measured readable-reasoning share for this session, mirroring
 * `AecoPostMortem.Api.ModelReasoningReadability` — never a corpus-wide constant, and never averaged
 * across two models a session used (the story's own edge case: two figures, not one). */
export interface ModelReasoningReadability {
  model: string
  readableCount: number
  totalCount: number
  readableSharePercent: number
}

/** `ThinkingEnvelope`'s two closed shapes (`StepEvidenceEnvelope.cs`) — "no reasoning" is a stated
 * value, never a blank Thinking panel. FR-23 (S-10, issue #19) added `readabilityByModel`, present
 * only when `reason` states the reasoning is provider-encrypted — optional here (rather than
 * `| null` required) so existing literals that predate this field still type-check. */
export type ThinkingEnvelope =
  | { kind: 'present'; text: string }
  | { kind: 'unavailable'; reason: string; readabilityByModel?: ModelReasoningReadability[] | null }

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
