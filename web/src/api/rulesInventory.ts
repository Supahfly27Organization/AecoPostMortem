// Mirrors AecoPostMortem.Api.RulesInventoryEnvelope (src/AecoPostMortem.Api/RulesInventoryEnvelope.cs)
// and the Rules-layer shapes it wraps (src/AecoPostMortem.Rules/RulesInventory.cs). Hand-kept in sync
// until a generated client exists — the same gap `web/src/api/appState.ts` and `digest.ts` document.
//
// `/api/rules-inventory` is served for real by `ApiHost.GetRulesInventory`: SessionRuleSetLookup
// resolves a whole store's RawEvents into SessionRuleSets at scale, and RulesInventoryClassifier
// classifies every statement (see AecoPostMortem.Api/CLAUDE.md for what it does and does not
// classify yet).

export const RulesInventoryRoute = '/api/rules-inventory'

/** The query parameter naming which rule-set version to render. FR-40 shows exactly one version at
 * a time (Scenario 6), so the version is part of the request, not a client-side filter over a
 * response carrying several. */
export const VersionParameter = 'version'

/** `RulesInventoryState` carries its own `[JsonConverter(typeof(JsonStringEnumConverter))]` with no
 * naming policy, so it serialises as its exact member name — the same choice `DigestState` makes. */
export type RulesInventoryState = 'NoInstructionBlocks' | 'BlocksCarriedNoStatements' | 'Listed'

/** FR-40's four statuses. A discriminated union rather than a status string plus an optional reason:
 * only `notCheckable` carries a `reason`, and the server contract makes that structural too. */
export type RuleStatementStatusEnvelope =
  | { status: 'watched'; label: string }
  | { status: 'checkableNotYetBuilt'; label: string }
  | { status: 'notCheckable'; label: string; reason: string }
  | { status: 'notARule'; label: string }

/** Two explicit states, never a nullable date — "still in force" and "the date went missing" must
 * not look the same on the wire. */
export type RuleRetirementEnvelope = { state: 'inForce' } | { state: 'retired'; retiredAt: string }

/** Mockup parity item #7 (Part 3's "Violations" column): a Watched row's own violation count,
 * sourced from whichever of the four piece-3 checks that actually produce one matches this row's
 * shape. `counted` carries a real number — including a real zero, a check that ran and genuinely
 * found nothing — and `notAvailable` states plainly that the matched shape (e.g. `PreferAOverB`, the
 * one Watchable shape with no Finding-producing orchestrator today) has no check to draw a count
 * from. Never a fabricated or zero-by-default number for that second case. */
export type RuleViolationCountEnvelope = { kind: 'counted'; count: number } | { kind: 'notAvailable' }

/** `firstSessionStartedAt` (the Monitor comparison's missing-door task) is the identical sort key
 * `Rules.RuleSetVersionAdjacency.RequireAdjacentPair` itself orders by (ordinal string comparison —
 * ISO-8601 timestamps sort correctly as plain strings), added so `api/useMonitorComparison.ts`'s
 * client-side adjacency check is a real port of that ordering rather than a client that merely
 * trusts this array's own order was never disturbed in transit. */
export interface RuleSetVersionEnvelope {
  repository: string | null
  hash: string
  firstSessionId: string
  lastSessionId: string
  firstSessionStartedAt: string
  sessionCount: number
}

export interface RulesInventoryRowEnvelope {
  sourceFile: string
  text: string
  status: RuleStatementStatusEnvelope
  sessionIds: string[]
  inForceFrom: string
  inForceUntil: string
  retirement: RuleRetirementEnvelope
  adherenceFrozenAt: string | null
  /** `null` for every status but `watched` — a row that is not Watched has no check running against
   * it at all, a different fact from a Watched row whose shape has no built check. */
  violationCount: RuleViolationCountEnvelope | null
}

export interface RulesInventoryStatusCountsEnvelope {
  watched: number
  checkableNotYetBuilt: number
  notCheckable: number
  notARule: number
  total: number
}

export interface RulesInventoryEnvelope {
  selectedVersion: RuleSetVersionEnvelope
  availableVersions: RuleSetVersionEnvelope[]
  state: RulesInventoryState
  rows: RulesInventoryRowEnvelope[]
  statusCounts: RulesInventoryStatusCountsEnvelope
}

/** Throws on a non-2xx response or a network failure; `useRulesInventory` turns that into a state a
 * component can render rather than an unhandled rejection. */
export async function fetchRulesInventory(
  versionHash: string | null,
  signal?: AbortSignal,
): Promise<RulesInventoryEnvelope> {
  const url =
    versionHash === null
      ? RulesInventoryRoute
      : `${RulesInventoryRoute}?${VersionParameter}=${encodeURIComponent(versionHash)}`

  const response = await fetch(url, { signal })

  if (!response.ok) {
    throw new Error(`GET ${url} failed with status ${response.status}`)
  }

  return (await response.json()) as RulesInventoryEnvelope
}
