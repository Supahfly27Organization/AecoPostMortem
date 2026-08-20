import type { Provenance } from '../api/digest'
import './ProvenanceBadge.css'

const LABELS: Record<Provenance, string> = {
  observed: 'Observed',
  derived: 'Derived',
  inferred: 'Inferred',
}

/** PRD §3.8's three provenance levels, rendered distinguishably — a badge naming which one applies
 * to this finding, not one shared "trust me" indicator. */
export function ProvenanceBadge({ provenance }: { provenance: Provenance }) {
  return (
    <span className="provenance-badge" data-provenance={provenance}>
      {LABELS[provenance]}
    </span>
  )
}
