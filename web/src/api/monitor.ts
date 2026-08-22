// Mirrors AecoPostMortem.Api.MonitorComparisonEnvelope (src/AecoPostMortem.Api/MonitorComparisonEnvelope.cs)
// and the Findings-layer shape it wraps (src/AecoPostMortem.Findings/MonitorComparison.cs). Hand-kept
// in sync until a generated client exists -- the same gap `web/src/api/appState.ts`, `digest.ts` and
// `rulesInventory.ts` document.
//
// `/api/monitor-comparison` is served for real by `ApiHost.GetMonitorComparison` (piece 4, wired in
// PR #108, its version-ordering defect fixed in PR #112) -- resolving a whole store's RawEvents into
// SessionRuleSets, then picking a real PreferAOverB statement and a real ToolInvocationShape corpus
// per side. `routes/MonitorPage.tsx` (`web/CLAUDE.md`) is the caller: it fetches this route through
// `api/useMonitorComparison.ts`'s hook, which resolves the endpoint's own two distinct refusals
// (a non-adjacent pair vs. an adjacent pair with no comparable rule -- both collapse to a bare 404 on
// the wire, `AecoPostMortem.Api/CLAUDE.md`'s `GetMonitorComparison` remarks) before ever calling
// `fetchMonitorComparison` below.

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

/** Throws on a non-2xx response or a network failure; `api/useMonitorComparison.ts`'s hook turns
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
