// Mirrors AecoPostMortem.Api.DigestEnvelope (src/AecoPostMortem.Api/DigestEnvelope.cs) and the
// Finding/Suggestion contracts it wraps (FindingEnvelope.cs, SuggestionEnvelope.cs). The route and
// field shapes are the contract between the .NET host and this client; keep both in sync by hand
// until a generated client exists (the same gap `web/src/api/appState.ts` documents).
//
// `/api/digest` is not served by `ApiHost` yet — FR-41's real orchestration (assembling
// `MastheadCounters`, a `CheckRegistry` and every `Finding` from the live store into one
// `ProcessDigest`) is later work no story has wired yet. `fetchDigest`/`useDigest` target the route
// ahead of that wiring, the same seam `fetchAppState`/`useAppState` established for `/api/app-state`
// before S-48 served it for real: the moment a future story serves this route, this page starts
// rendering live data with no frontend change.

export const DigestRoute = '/api/digest'

/** Wire values for `AecoPostMortem.Findings.FindingClass` — camelCase because they carry no
 * per-property `JsonConverter` of their own, so they pick up `ApiHost`'s global
 * `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` once a real endpoint serves them. */
export type FindingClass =
  | 'ruleAdherenceToolChoice'
  | 'waste'
  | 'ruleAdherenceWrittenContent'
  | 'missingCapability'

/** PRD §3.8's three provenance levels, camelCase for the same reason as `FindingClass`. */
export type Provenance = 'observed' | 'derived' | 'inferred'

/** FR-45's three responses, camelCase for the same reason as `FindingClass`. */
export type OperatorResponse = 'ignored' | 'accepted' | 'rejected'

/** `DigestState` carries its own `[JsonConverter(typeof(JsonStringEnumConverter))]` with no naming
 * policy (`DigestEnvelope.cs`), so it serialises as its exact member name, not camelCase. */
export type DigestState = 'NotYetAnalyzed' | 'Incomplete' | 'Analyzed'

/** Same reasoning as `DigestState` — `RuleCoverageStatus` keeps its exact member name. */
export type RuleCoverageStatus = 'NotYetAnalyzed'

export interface EvidenceItem {
  field: string
  value: string
}

export interface RecurrenceOccurrence {
  sessionId: string
  ruleSetVersion: string | null
}

export interface Recurrence {
  key: string
  occurrences: RecurrenceOccurrence[]
}

export interface Resolution {
  operandLayer: string
  callCount: number
}

/** `SuggestionEnvelope`'s two explicit states (S-50, issue #13) — "no suggestion template" is a
 * named state, never a missing or nullable field. */
export type SuggestionEnvelope = { state: 'present'; text: string } | { state: 'absent' }

interface FindingEnvelopeBase {
  class: FindingClass
  provenance: Provenance
  evidence: EvidenceItem[]
  recurrence: Recurrence
  suggestion: SuggestionEnvelope
  operatorResponse: OperatorResponse
}

/** `FindingEnvelope`'s two closed shapes (`kind: "general"` / `"adherence"`) — only the adherence
 * shape carries a `resolution` and `ruleVersion` (FR-33). */
export type FindingEnvelope =
  | (FindingEnvelopeBase & { kind: 'general' })
  | (FindingEnvelopeBase & { kind: 'adherence'; resolution: Resolution; ruleVersion: string })

/** `RepositoryScopeEnvelope` (FR-41 part 2, S-54): PRD Part 8 Q5's default-one-repository,
 * selectable decision — `availableRepositories` is the seam a later cross-repository view switches
 * through. */
export interface RepositoryScopeEnvelope {
  selectedRepository: string | null
  availableRepositories: string[]
}

export interface MastheadEnvelope {
  sessionCount: number
  spanStart: string | null
  spanEnd: string | null
  repositoryCount: number
  eventCount: number
  toolCallCount: number
  ruleCoverage: RuleCoverageStatus
  repositoryScope: RepositoryScopeEnvelope
}

export interface DigestEnvelope {
  masthead: MastheadEnvelope
  state: DigestState
  rankedFindings: FindingEnvelope[]
}

/** Throws on a non-2xx response or a network failure; callers (see `useDigest`) turn that into a
 * state a component can render rather than an unhandled rejection. */
export async function fetchDigest(signal?: AbortSignal): Promise<DigestEnvelope> {
  const response = await fetch(DigestRoute, { signal })

  if (!response.ok) {
    throw new Error(`GET ${DigestRoute} failed with status ${response.status}`)
  }

  return (await response.json()) as DigestEnvelope
}
