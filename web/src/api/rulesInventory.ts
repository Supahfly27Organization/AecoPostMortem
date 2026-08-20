// Mirrors AecoPostMortem.Api.RulesInventoryEnvelope (src/AecoPostMortem.Api/RulesInventoryEnvelope.cs)
// and the Rules-layer shapes it wraps (src/AecoPostMortem.Rules/RulesInventory.cs). Hand-kept in sync
// until a generated client exists — the same gap `web/src/api/appState.ts` and `digest.ts` document.
//
// `/api/rules-inventory` is not served by `ApiHost` yet: resolving a whole store's RawEvents into
// SessionRuleSets at scale (FR-26's extraction run over every session) is wiring no story has done.
// `fetchRulesInventory`/`useRulesInventory` target the route ahead of it, the same seam
// `fetchDigest`/`useDigest` established for `/api/digest`.

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

export interface RuleSetVersionEnvelope {
  repository: string | null
  hash: string
  firstSessionId: string
  lastSessionId: string
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
