import type { SilentCheckEnvelope } from '../api/digest'
import { ProvenanceBadge } from './ProvenanceBadge'
import './CleanChecks.css'

/** A check's abstract `CheckId` (e.g. `hook-failure`) rendered as a title (`Hook Failure`) — a pure
 * display transform, not a new domain concept: `CheckRegistryEntry.CheckId`'s own remarks say it is
 * "an abstract identifier," so there is no served display name to read instead. */
function humanizeCheckId(checkId: string): string {
  return checkId
    .split('-')
    .map((word) => (word.length === 0 ? word : word[0].toUpperCase() + word.slice(1)))
    .join(' ')
}

/**
 * FR-42 (issue #46), mockup parity item #6: "Checks that found nothing"
 * (`docs/product-superpowers/discovery/mockups/digest.html`'s `.clean`/`.ck` grid). A check that
 * fires is a finding; a check that stays silent has to say so explicitly, naming its population and
 * its zero count, or "clean" is indistinguishable from "never looked" (PRD §3.9). Renders no section
 * at all when `checks` is empty — the same "no section at all" discipline the "Judgment calls"
 * section (`DigestPage.tsx`) already established for an empty `inferredFindings` list.
 */
export function CleanChecks({ checks }: { checks: SilentCheckEnvelope[] }) {
  if (checks.length === 0) {
    return null
  }

  return (
    <section className="clean-checks" aria-labelledby="clean-checks__heading">
      <h3 id="clean-checks__heading">Checks that found nothing</h3>
      <p className="digest-page__state">
        A check that fires is a finding. A check that stays silent has to say so, or you cannot tell
        the difference between &ldquo;clean&rdquo; and &ldquo;never looked&rdquo;.
      </p>
      <ul className="clean-checks__grid">
        {checks.map((check) => (
          <li key={check.checkId} className="clean-checks__card">
            <div className="clean-checks__title">{humanizeCheckId(check.checkId)}</div>
            <div className="clean-checks__stats">
              {check.findingCount} found &middot; {check.population.toLocaleString()} checked
            </div>
            <ProvenanceBadge provenance={check.provenance} />
          </li>
        ))}
      </ul>
    </section>
  )
}
