import type { RepositoryScopeEnvelope } from '../api/digest'
import './RepositorySelector.css'

interface RepositorySelectorProps {
  scope: RepositoryScopeEnvelope
  onSelect?: (repository: string) => void
}

/** Scenario 3 (issue #45) / PRD Part 8 Q5: the digest shows one repository at a time, selectable.
 * `availableRepositories` is the seam a later cross-repository view switches through — selecting a
 * different one reports the choice to the caller, who decides what (if anything) happens next; no
 * caller re-fetches cross-repository data yet, so the seam is exercised without a live re-fetch
 * behind it (this story's own edge case: implement the default, keep the selector as a seam). */
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
