import { useEffect, useState } from 'react'
import { useRulesInventory } from '../api/useRulesInventory'
import { useMonitorComparison } from '../api/useMonitorComparison'
import type { RuleSetVersionEnvelope } from '../api/rulesInventory'
import { MonitorComparisonBlock } from '../digest/MonitorComparisonBlock'
import './MonitorPage.css'

// A stable, shared reference for "no version list yet" -- a fresh `[]` literal inline at the call
// site would change identity every render, and `useMonitorComparison`'s effect depends on this
// array by reference, so a new array every render would re-run that effect (and its own `setQuery`)
// forever.
const NoVersions: readonly RuleSetVersionEnvelope[] = []

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
 * recent versions (the newest edit). `useMonitorComparison` resolves adjacency locally before ever
 * calling the server -- see its own doc comment for why that is what lets this page state two
 * honest, distinct refusals rather than one bare 404 collapsing both.
 */
export function MonitorPage() {
  const inventoryQuery = useRulesInventory(null)
  const [beforeHash, setBeforeHash] = useState<string | null>(null)
  const [afterHash, setAfterHash] = useState<string | null>(null)

  // Defaults to the two most recent versions the moment the version list first loads, and never
  // again once a selection exists -- an operator's own choice is never overwritten by this effect
  // re-running for an unrelated reason (e.g. a future re-fetch of the inventory).
  useEffect(() => {
    if (inventoryQuery.status !== 'loaded') return
    if (beforeHash !== null || afterHash !== null) return

    const versions = inventoryQuery.inventory.availableVersions
    if (versions.length < 2) return

    setBeforeHash(versions[versions.length - 2].hash)
    setAfterHash(versions[versions.length - 1].hash)
  }, [inventoryQuery, beforeHash, afterHash])

  const availableVersions =
    inventoryQuery.status === 'loaded' ? inventoryQuery.inventory.availableVersions : NoVersions
  const comparisonQuery = useMonitorComparison(availableVersions, beforeHash, afterHash)

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

  const { availableVersions: versions } = inventoryQuery.inventory

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
        beforeHash={beforeHash}
        afterHash={afterHash}
        onSelectBefore={setBeforeHash}
        onSelectAfter={setAfterHash}
      />

      {comparisonQuery.status === 'notAdjacent' && (
        <p className="monitor-page__refusal" role="status">
          These two versions are not adjacent. The Monitor only compares a rule-set edit directly
          against the version immediately before it — pick two versions next to each other in the
          lists above.
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
 * rather than being restricted to a derived "previous version" the page alone chooses. */
function VersionPairPicker({
  versions,
  beforeHash,
  afterHash,
  onSelectBefore,
  onSelectAfter,
}: {
  versions: RuleSetVersionEnvelope[]
  beforeHash: string | null
  afterHash: string | null
  onSelectBefore: (hash: string) => void
  onSelectAfter: (hash: string) => void
}) {
  return (
    <section className="monitor-page__picker" aria-label="Rule-set version pair">
      <label className="monitor-page__picker-field">
        <span>Before</span>
        <select
          aria-label="Before"
          value={beforeHash ?? ''}
          onChange={(event) => onSelectBefore(event.target.value)}
        >
          {versions.map((version) => (
            <option key={version.hash} value={version.hash}>
              {version.hash} ({version.sessionCount} session{version.sessionCount === 1 ? '' : 's'})
            </option>
          ))}
        </select>
      </label>

      <label className="monitor-page__picker-field">
        <span>After</span>
        <select
          aria-label="After"
          value={afterHash ?? ''}
          onChange={(event) => onSelectAfter(event.target.value)}
        >
          {versions.map((version) => (
            <option key={version.hash} value={version.hash}>
              {version.hash} ({version.sessionCount} session{version.sessionCount === 1 ? '' : 's'})
            </option>
          ))}
        </select>
      </label>
    </section>
  )
}
