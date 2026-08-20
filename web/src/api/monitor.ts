// Mirrors AecoPostMortem.Api.MonitorComparisonEnvelope (src/AecoPostMortem.Api/MonitorComparisonEnvelope.cs)
// and the Findings-layer shape it wraps (src/AecoPostMortem.Findings/MonitorComparison.cs). Hand-kept
// in sync until a generated client exists -- the same gap `web/src/api/appState.ts`, `digest.ts` and
// `rulesInventory.ts` document.
//
// `/api/monitor-comparison` is not served by `ApiHost` yet: resolving a whole store's RawEvents into
// SessionRuleSets at scale, then picking the two adjacent versions and the operand pair to compare, is
// wiring no story has done (the same not-yet-wired gap `/api/digest` and `/api/rules-inventory`
// document). `fetchMonitorComparison` targets the route ahead of it, the same seam those two
// established.

import type { AdherenceFigure } from './digest'
import type { RuleSetVersionEnvelope } from './rulesInventory'

export const MonitorComparisonRoute = '/api/monitor-comparison'

/**
 * FR-39's served comparison (S-35, issue #43): adherence for one rule, before and after an adjacent
 * rule-set-version edit, under one shared resolution. `beforeVersion`/`afterVersion` reuse
 * `RuleSetVersionEnvelope` (S-22) rather than a bare hash, so `sessionCount` -- Scenario 2's sample
 * size -- travels on the same object as everything else describing that side. `before`/`after` reuse
 * `AdherenceFigure` verbatim, the same domain shape `FindingEnvelope.Adherence.figure` already
 * carries -- there is no separate figure shape to keep in sync with a second one.
 */
export interface MonitorComparisonEnvelope {
  beforeVersion: RuleSetVersionEnvelope
  afterVersion: RuleSetVersionEnvelope
  before: AdherenceFigure
  after: AdherenceFigure
}

/** Throws on a non-2xx response or a network failure; a future `useMonitorComparison` hook turns
 * that into a state a component can render, the same shape `useDigest`/`useRulesInventory` use. */
export async function fetchMonitorComparison(
  before: string,
  after: string,
  signal?: AbortSignal,
): Promise<MonitorComparisonEnvelope> {
  const url = `${MonitorComparisonRoute}?before=${encodeURIComponent(before)}&after=${encodeURIComponent(after)}`
  const response = await fetch(url, { signal })

  if (!response.ok) {
    throw new Error(`GET ${url} failed with status ${response.status}`)
  }

  return (await response.json()) as MonitorComparisonEnvelope
}
