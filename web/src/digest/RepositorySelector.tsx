import type { RepositoryScopeEnvelope } from '../api/digest'
import './RepositorySelector.css'

interface RepositorySelectorProps {
  scope: RepositoryScopeEnvelope
  onSelect?: (repository: string) => void
}

/** Scenario 3 (issue #45) / PRD Part 8 Q5: the digest shows one repository at a time, selectable.
 *
 * This shipped as a display-only seam — `onSelect` reported the choice and `DigestPage` overlaid it
 * on the served scope, so the `<select>` changed and nothing else did. That left every repository
 * but the server's own most-sessions default unreachable through the whole product, behind a control
 * that looked live. `DigestPage` now re-fetches on the reported choice
 * (`ApiHost.RepositoryParameter`), so this component is unchanged but no longer a seam.
 *
 * It stays a pure presenter — `DigestPage` decides which repository name to show. During a re-fetch
 * that is the *requested* one rather than the served one, so the control never snaps back to the
 * previous repository while the new digest is in flight; see `DigestPage`'s own remarks. */
export function RepositorySelector({ scope, onSelect }: RepositorySelectorProps) {
  if (scope.availableRepositories.length === 0) {
    return null
  }

  return (
    <label className="repository-selector">
      Repository
      <select
        // `?? ''` never actually selects a blank option here: `RepositoryScope`'s own contract
        // (`Digest.cs`) only allows a null `SelectedRepository` when `AvailableRepositories` is
        // also empty, and this component already returns null above in that case before reaching
        // the `<select>`.
        aria-label="Repository"
        value={scope.selectedRepository ?? ''}
        onChange={(event) => onSelect?.(event.target.value)}
      >
        {scope.availableRepositories.map((repository) => (
          <option key={repository} value={repository}>
            {repository}
          </option>
        ))}
      </select>
    </label>
  )
}
