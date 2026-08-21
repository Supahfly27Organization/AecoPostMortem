import { useState } from 'react'
import { useRulesInventory } from '../api/useRulesInventory'
import type {
  RuleSetVersionEnvelope,
  RuleStatementStatusEnvelope,
  RuleViolationCountEnvelope,
  RulesInventoryRowEnvelope,
  RulesInventoryStatusCountsEnvelope,
} from '../api/rulesInventory'
import './RulesInventoryPage.css'

/**
 * The Rules Inventory (FR-40, S-22, issue #35): every extracted statement with exactly one status,
 * the file it came from, the sessions carrying it, its in-force window, and — for a statement gone
 * from the repository's most recent rule-set version — the date its adherence froze.
 *
 * The surface renders exactly one rule-set version at a time and names it. That is the story's
 * sharpest constraint, not a simplification: a measured 34 of 43 statements are absent from the most
 * recent session, so the union-of-all-versions table the digest mockup showed would render three
 * quarters of its rows as though they were still in force (PRD Part 4).
 *
 * `/api/rules-inventory` is not served by `ApiHost` yet (`web/src/api/rulesInventory.ts` documents
 * why), so against a real browser this page renders its own "could not reach the local API" state —
 * the same seam `DigestPage` uses for `/api/digest`.
 */
export function RulesInventoryPage() {
  const [requestedVersion, setRequestedVersion] = useState<string | null>(null)
  const query = useRulesInventory(requestedVersion)

  if (query.status === 'loading') {
    return (
      <div className="rules-inventory">
        <h2>Rules Inventory</h2>
      </div>
    )
  }

  if (query.status === 'error') {
    return (
      <div className="rules-inventory">
        <h2>Rules Inventory</h2>
        <p role="alert">
          Could not reach the local API. Is <code>aecopostmortem serve</code> running?
        </p>
      </div>
    )
  }

  const { inventory } = query

  return (
    <div className="rules-inventory">
      <h2>Rules Inventory</h2>

      <VersionScope
        selected={inventory.selectedVersion}
        available={inventory.availableVersions}
        onSelect={setRequestedVersion}
      />

      <StatusBreakdown counts={inventory.statusCounts} />

      {inventory.state === 'NoInstructionBlocks' && (
        <p className="rules-inventory__empty">
          No rules were found — no session in this rule-set version carried an instruction block.
        </p>
      )}

      {inventory.state === 'BlocksCarriedNoStatements' && (
        <p className="rules-inventory__empty">
          No rules were found — instruction blocks were carried, but they carried no list item for the
          extractor to read.
        </p>
      )}

      {inventory.state === 'Listed' && <StatementTable rows={inventory.rows} />}
    </div>
  )
}

/** Scenario 6: names the one version showing, and offers the others to switch to — the switch is a
 * new request (`useRulesInventory`), never a widening of what is on screen. */
function VersionScope({
  selected,
  available,
  onSelect,
}: {
  selected: RuleSetVersionEnvelope
  available: RuleSetVersionEnvelope[]
  onSelect: (hash: string) => void
}) {
  return (
    <section className="rules-inventory__scope" aria-label="Rule-set version">
      <p className="rules-inventory__scope-statement">
        Showing rule-set version <code>{selected.hash}</code> of{' '}
        {selected.repository ?? 'no recorded repository'} — {selected.sessionCount} session
        {selected.sessionCount === 1 ? '' : 's'}, {selected.firstSessionId} to {selected.lastSessionId}.
      </p>

      <label className="rules-inventory__scope-picker">
        <span className="rules-inventory__scope-picker-label">Rule-set version</span>
        <select
          aria-label="Rule-set version"
          value={selected.hash}
          onChange={(event) => onSelect(event.target.value)}
        >
          {/* `availableVersions` arrives in the repository's own chronological order, so the last
              entry is the most recent — the one version in which nothing is retired (FR-40,
              Scenario 5). Hashes are opaque, so an unmarked list gives the operator no way to tell. */}
          {available.map((version, index) => (
            <option key={version.hash} value={version.hash}>
              {version.hash} ({version.sessionCount} session{version.sessionCount === 1 ? '' : 's'})
              {index === available.length - 1 ? ' — most recent' : ''}
            </option>
          ))}
        </select>
      </label>
    </section>
  )
}

const StatusOrder = [
  { key: 'watched', label: 'Watched' },
  { key: 'checkableNotYetBuilt', label: 'Checkable — not yet built' },
  { key: 'notCheckable', label: 'Not checkable' },
  { key: 'notARule', label: 'Not a rule' },
] as const

/**
 * FR-40's breakdown — the measured 4 / 9 / 9 / 21 on the reference corpus.
 *
 * Every tile carries `data-emphasis="neutral"`, and that is the whole point of this component. "Not
 * a rule" is the largest bucket by a wide margin, and it is not a failure: the extraction unit is a
 * markdown list item (FR-26), so most list items in a `CLAUDE.md` were never going to be rules. A
 * tile styled like a problem count would turn the corpus's own shape into an accusation.
 */
function StatusBreakdown({ counts }: { counts: RulesInventoryStatusCountsEnvelope }) {
  return (
    <section className="rules-inventory__breakdown" aria-label="Status breakdown">
      {StatusOrder.map(({ key, label }) => (
        <span
          key={key}
          className="rules-inventory__count"
          data-testid={`status-count-${key}`}
          data-status={key}
          data-emphasis="neutral"
        >
          <strong>{counts[key]}</strong> {label}
        </span>
      ))}
      <span className="rules-inventory__count-total">of {counts.total} extracted statements</span>
    </section>
  )
}

function StatementTable({ rows }: { rows: RulesInventoryRowEnvelope[] }) {
  return (
    <table className="rules-inventory__table">
      <thead>
        <tr>
          <th scope="col">Rule</th>
          <th scope="col">Source file</th>
          <th scope="col">Status</th>
          <th scope="col">Violations</th>
          <th scope="col">Sessions carrying it</th>
          {/* "In force in this version", not "In force": the window is the selected version's own,
              so on an older version it closes in the past while the Retirement column may still say
              "In force" — true statements that read as contradictory under a bare header. */}
          <th scope="col">In force in this version</th>
          <th scope="col">Retirement</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          // Length-prefixed so the two fields cannot run together: ("a", "b:c") and ("a:b", "c")
          // share a key under a plain delimiter, the same collision RuleSetVersionHasher avoids.
          <StatementRow key={`${row.sourceFile.length}:${row.sourceFile}:${row.text}`} row={row} />
        ))}
      </tbody>
    </table>
  )
}

function StatementRow({ row }: { row: RulesInventoryRowEnvelope }) {
  const retired = row.retirement.state === 'retired'

  return (
    // aria-label rather than letting the row be named by its contents: a retired row and an in-force
    // row must be addressable the same way, and the rule text itself is already in the first cell.
    <tr aria-label={`Statement: ${row.text}`} data-retired={retired ? 'true' : 'false'}>
      <td className="rules-inventory__text">{row.text}</td>
      <td>{row.sourceFile}</td>
      <td>
        <StatusCell status={row.status} />
      </td>
      <td>
        <ViolationCountCell violationCount={row.violationCount} />
      </td>
      {/* One text node, not one element per session: the sessions carrying a statement are its
          reach (Scenario 2), stated in full rather than as a count. */}
      <td className="rules-inventory__sessions">{row.sessionIds.join(', ')}</td>
      <td className="rules-inventory__window">
        {row.inForceFrom} → {row.inForceUntil}
      </td>
      <td className="rules-inventory__retirement">
        {row.retirement.state === 'retired'
          ? `Retired — adherence frozen at ${row.retirement.retiredAt}`
          : 'In force'}
      </td>
    </tr>
  )
}

/** Exactly one status per statement, and the reason travels with "Not checkable" rather than being
 * a separate column that could be filled in for the wrong row. */
function StatusCell({ status }: { status: RuleStatementStatusEnvelope }) {
  return (
    <>
      <span className="rules-inventory__status" data-testid="rule-status" data-status={status.status}>
        {status.label}
      </span>
      {status.status === 'notCheckable' && (
        <p className="rules-inventory__reason">{status.reason}</p>
      )}
    </>
  )
}

/**
 * Mockup parity item #7: a per-rule violation count sits directly in this table, rather than one hop
 * away on the Digest. `null` (every status but Watched) renders a plain dash — no check runs against
 * a row that is not Watched at all. A Watched row with `notAvailable` (the matched shape has no
 * Finding-producing orchestrator, e.g. `PreferAOverB`) renders a stated "No check built" instead of a
 * dash, so the two absences — "not applicable" and "not yet built" — never read the same way. `0` is
 * rendered as a real number, never mistaken for either kind of absence: a check that ran over this
 * statement and genuinely found nothing is a different fact from one that never ran at all.
 */
function ViolationCountCell({ violationCount }: { violationCount: RuleViolationCountEnvelope | null }) {
  if (violationCount === null) {
    return (
      <span className="rules-inventory__violation-count" data-violation="not-applicable">
        —
      </span>
    )
  }

  if (violationCount.kind === 'notAvailable') {
    return (
      <span className="rules-inventory__violation-count" data-violation="no-built-check">
        No check built
      </span>
    )
  }

  return (
    <span className="rules-inventory__violation-count" data-violation="counted">
      {violationCount.count} {violationCount.count === 1 ? 'violation' : 'violations'}
    </span>
  )
}
