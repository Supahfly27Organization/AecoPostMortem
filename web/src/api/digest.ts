// Mirrors AecoPostMortem.Api.DigestEnvelope (src/AecoPostMortem.Api/DigestEnvelope.cs) and the
// Finding/Suggestion contracts it wraps (FindingEnvelope.cs, SuggestionEnvelope.cs). The route and
// field shapes are the contract between the .NET host and this client; keep both in sync by hand
// until a generated client exists (the same gap `web/src/api/appState.ts` documents).
//
// `/api/digest` is served for real by `ApiHost.GetDigest` (S-36, issue #44): six of the seven
// waste/missing-capability check orchestrators, `MastheadCounters` and a `RepositoryScope`, all
// assembled into one `ProcessDigest`. `fetchDigest`/`useDigest` had targeted the route ahead of that
// wiring, the same seam `fetchAppState`/`useAppState` established for `/api/app-state` before S-48
// served it for real — and the prediction held: this file needed no change once the route went live.

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

/** `AecoPostMortem.Rules.OperandResolutionLayer` — FR-31's four layers, most confident first,
 * camelCase for the same reason as `FindingClass`. `unresolved` is a real member, not an absence:
 * an operand nothing matched is still reported. */
export type OperandResolutionLayer =
  | 'exactToolName'
  | 'mcpServerField'
  | 'derivedRole'
  | 'unresolved'

/** `AecoPostMortem.Findings.OperandResolution` — one rule operand, the layer that resolved it, and
 * the calls that resolution produced. */
export interface OperandResolution {
  operandText: string
  layer: OperandResolutionLayer
  callCount: number
}

/** `AecoPostMortem.Rules.RuleSetVersionId` (S-20) — a rule set's identity: the repository it was in
 * force for plus the content hash of its block set. */
export interface RuleSetVersionId {
  repository: string | null
  hash: string
}

/**
 * `AecoPostMortem.Findings.AdherenceFigure` (S-24, issue #38, FR-33). `percentage`, `adherentCalls`
 * and `totalCalls` are computed server-side from `adherent`/`divergent`, so they always agree with
 * the operands and can never arrive without them — the same guarantee the .NET type makes at compile
 * time, which is why this client has no code path that could render a bare figure.
 *
 * `percentage` is `null`, never `0`, when the rule had no calls either way (PRD §5.5 tolerates zero
 * occurrences): the figure still states its resolution and says plainly that there is no percentage.
 */
export interface AdherenceFigure {
  ruleVersion: RuleSetVersionId
  adherent: OperandResolution
  divergent: OperandResolution[]
  operands: OperandResolution[]
  adherentCalls: number
  totalCalls: number
  percentage: number | null
}

/** `SuggestionEnvelope`'s two explicit states (S-50, issue #13) — "no suggestion template" is a
 * named state, never a missing or nullable field. */
export type SuggestionEnvelope = { state: 'present'; text: string } | { state: 'absent' }

interface FindingEnvelopeBase {
  class: FindingClass
  provenance: Provenance
  /** Mockup parity item #5 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`):
   * a full written sentence naming the problem — the mockup's own `t` field. Replaces
   * `recurrence.key` (a raw tool name or a rule's own text) as `FindingRow`'s visible headline. */
  headline: string
  evidence: EvidenceItem[]
  recurrence: Recurrence
  /** FR-41 (S-36): the distinct-session count `ProcessDigest.Build` ranked `rankedFindings` by.
   * Served rather than re-derived from `recurrence.occurrences` here — a client counting its own
   * copy could silently disagree with the order it is rendering. */
  sessionsAffected: number
  suggestion: SuggestionEnvelope
  operatorResponse: OperatorResponse
}

/** `FindingEnvelope`'s closed shapes (`kind: "general"` / `"adherence"`) — only the adherence shape
 * carries a `figure`, and it always does (FR-33, S-24). The percentage, the per-operand resolution
 * and the rule version all ride on that one member, so the union has no variant in which a client
 * could receive a percentage on its own. */
export type FindingEnvelope =
  | (FindingEnvelopeBase & { kind: 'general' })
  | (FindingEnvelopeBase & { kind: 'adherence'; figure: AdherenceFigure })

/** `RepositoryScopeEnvelope` (FR-41 part 2, S-54): PRD Part 8 Q5's default-one-repository,
 * selectable decision — `availableRepositories` is the seam a later cross-repository view switches
 * through. */
export interface RepositoryScopeEnvelope {
  selectedRepository: string | null
  availableRepositories: string[]
  /** Every session id in this scope, chronologically ordered — mirrors
   * `AecoPostMortem.Findings.RepositoryScope.SessionIds`. A per-finding session strip needs this:
   * which of the scope's own sessions a finding's `recurrence.occurrences` touched, and in what
   * position, not only how many. */
  sessionIds: string[]
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
  /** FR-48 (issue #52, S-42): every `Provenance.Inferred` finding, served separately from
   * `rankedFindings` and never interleaved by rank with it — `AecoPostMortem.Findings.Digest.cs`'s
   * own remarks say ranking a hypothesis by `sessionsAffected` "would dress the hypothesis up with
   * the same measured-looking number that ranks Observed and Derived findings." This field mirrors
   * `DigestEnvelope.InferredFindings` (`src/AecoPostMortem.Api/DigestEnvelope.cs`) exactly — same
   * `FindingEnvelope` shape as `rankedFindings`, just a different, unranked list. */
  inferredFindings: FindingEnvelope[]
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
