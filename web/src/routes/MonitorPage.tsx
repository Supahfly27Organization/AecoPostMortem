import { useState } from 'react'
import { useRulesInventory } from '../api/useRulesInventory'
import { useMonitorComparison } from '../api/useMonitorComparison'
import type { RuleSetVersionEnvelope } from '../api/rulesInventory'
import { MonitorComparisonBlock } from '../digest/MonitorComparisonBlock'
import './MonitorPage.css'

/**
 * FR-39's Monitor comparison (S-35, issue #43), given its own reachable door: a fourth routed
 * surface, `/monitor`. It does not live as a section on the Digest or the Rules Inventory --
 * Rules Inventory commits hard to "exactly one version at a time" (Scenario 6,
 * `web/CLAUDE.md`'s "Switching rule-set versions is a new request" note), a constraint a
 * version-*pair* view would break if grafted on, and the Digest ranks findings within one scope
 * rather than comparing two. This mirrors how Rules Inventory itself arrived (S-22): a new FR got
 * its own page and its own nav entry, not a bolt-on section.
 *
 * Reuses `RulesInventoryEnvelope.availableVersions` (fetched via the same `useRulesInventory` hook
 * `RulesInventoryPage` uses, called with no version -- the default fetch already carries the full,
 * chronologically ordered list) as the source of version identities, per the task's own framing.
 * The operator picks *both* sides freely from two independent selects, defaulting to the two most
 * recent versions (the newest edit). Every refusal is served by the endpoint itself
 * (`MonitorComparisonResultEnvelope`), so this page renders one message per served `kind` and
 * derives nothing.
 *
 * Two workarounds are gone with that change, both of which existed only because the endpoint used to
 * answer one bodyless 404 for three different reasons: `useMonitorComparison` re-implementing the
 * server's own adjacency rule in TypeScript, and this page refusing to reach the hook at all when no
 * repository resolved (so the remaining 404 could be labelled unambiguously). The repository-less
 * scope is now just another served reason, stated in the same place as the other two.
 */
export function MonitorPage() {
  const inventoryQuery = useRulesInventory(null)
  const [beforeHash, setBeforeHash] = useState<string | null>(null)
  const [afterHash, setAfterHash] = useState<string | null>(null)

  // Defaults derived at render time, not via an effect + extra state: the previous version needed
  // a `useEffect` plus a `beforeHash !== null || afterHash !== null` guard to set the default
  // exactly once, which also forced a stable-reference workaround downstream in
  // `useMonitorComparison` (see that file's own doc comment, code review round 1). Deriving here
  // instead removes both -- an explicit operator selection (`beforeHash`/`afterHash` state) always
  // wins over the derived default, and there is no render where the two selects could show a stale
  // or mismatched value the way an effect-driven default briefly can.
  const versions = inventoryQuery.status === 'loaded' ? inventoryQuery.inventory.availableVersions : []
  const defaultBeforeHash = versions.length >= 2 ? versions[versions.length - 2].hash : null
  const defaultAfterHash = versions.length >= 2 ? versions[versions.length - 1].hash : null
  const effectiveBeforeHash = beforeHash ?? defaultBeforeHash
  const effectiveAfterHash = afterHash ?? defaultAfterHash

  const comparisonQuery = useMonitorComparison(effectiveBeforeHash, effectiveAfterHash)

  if (inventoryQuery.status === 'loading') {
    return (
      <div className="monitor-page">
        <h2>Monitor</h2>
      </div>
    )
  }

  if (inventoryQuery.status === 'error') {
    return (
      <div className="monitor-page">
        <h2>Monitor</h2>
        <p role="alert">
          Could not reach the local API. Is <code>aecopostmortem serve</code> running?
        </p>
      </div>
    )
  }

  if (versions.length < 2) {
    return (
      <div className="monitor-page">
        <h2>Monitor</h2>
        <p className="monitor-page__empty">
          Not enough rule-set versions in this repository to compare — the Monitor needs at least
          two adjacent versions, and this repository has only {versions.length}.
        </p>
      </div>
    )
  }

  return (
    <div className="monitor-page">
      <h2>Monitor</h2>

      <VersionPairPicker
        versions={versions}
        beforeHash={effectiveBeforeHash}
        afterHash={effectiveAfterHash}
        onSelectBefore={setBeforeHash}
        onSelectAfter={setAfterHash}
      />

      {comparisonQuery.status === 'notAdjacent' && (
        <p className="monitor-page__refusal" role="status">
          These two versions are not adjacent
          {comparisonQuery.intervening.length > 0 &&
            ` — ${comparisonQuery.intervening.length} other rule-set ${
              comparisonQuery.intervening.length === 1 ? 'version was' : 'versions were'
            } in force between them`}
          . The Monitor only compares a rule-set edit directly against the version immediately before
          it — pick two versions next to each other in the lists above (the numbering states each
          one's own chronological position).
        </p>
      )}

      {comparisonQuery.status === 'noRepository' && (
        <p className="monitor-page__refusal" role="status">
          No repository is recorded for any session in this store, so the Monitor has nothing to
          scope a comparison to.
        </p>
      )}

      {comparisonQuery.status === 'noComparableRule' && (
        <p className="monitor-page__refusal" role="status">
          No comparable rule was found for this pair. The newer version carries no statement of the
          shape this comparison can measure (a "prefer one tool over another" rule).
        </p>
      )}

      {comparisonQuery.status === 'error' && (
        <p role="alert">
          Could not reach the local API. Is <code>aecopostmortem serve</code> running?
        </p>
      )}

      {comparisonQuery.status === 'loaded' && (
        <MonitorComparisonBlock comparison={comparisonQuery.comparison} />
      )}
    </div>
  )
}

/** Two independent selects, both populated from the identical ordered `availableVersions` list --
 * mirroring `RulesInventoryPage`'s own single version picker, applied twice, so the operator can
 * freely explore any pair (including a deliberately non-adjacent one, to see the honest refusal)
 * rather than being restricted to a derived "previous version" the page alone chooses.
 *
 * Each option is numbered by its own chronological position (code review, round 2, Minor): hashes
 * are opaque, and the whole premise of this page is adjacency, so leaving the operator to guess
 * which pairs are next to each other by trial and error is a real, avoidable gap -- the same
 * "hashes are opaque without a marker" reasoning `RulesInventoryPage`'s own "— most recent" suffix
 * already states for its single picker. */
function VersionPairPicker({
  versions,
  beforeHash,
  afterHash,
  onSelectBefore,
  onSelectAfter,
}: {
  versions: readonly RuleSetVersionEnvelope[]
  beforeHash: string | null
  afterHash: string | null
  onSelectBefore: (hash: string) => void
  onSelectAfter: (hash: string) => void
}) {
  return (
    <section className="monitor-page__picker" aria-label="Rule-set version pair">
      <VersionSelect
        label="Before"
        versions={versions}
        value={beforeHash}
        onSelect={onSelectBefore}
      />
      <VersionSelect label="After" versions={versions} value={afterHash} onSelect={onSelectAfter} />
    </section>
  )
}

function VersionSelect({
  label,
  versions,
  value,
  onSelect,
}: {
  label: string
  versions: readonly RuleSetVersionEnvelope[]
  value: string | null
  onSelect: (hash: string) => void
}) {
  return (
    <label className="monitor-page__picker-field">
      <span>{label}</span>
      <select aria-label={label} value={value ?? ''} onChange={(event) => onSelect(event.target.value)}>
        {versions.map((version, index) => (
          <option key={version.hash} value={version.hash}>
            {index + 1}. {version.hash} ({version.sessionCount} session
            {version.sessionCount === 1 ? '' : 's'})
            {index === versions.length - 1 ? ' — most recent' : ''}
          </option>
        ))}
      </select>
    </label>
  )
}
