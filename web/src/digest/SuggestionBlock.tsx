import type { SuggestionEnvelope } from '../api/digest'
import './SuggestionBlock.css'

/** Scenario 4 (issue #45): a finding class with no suggestion template still expands with an
 * explicit statement that none is offered — FR-56's fallback, reusing `SuggestionEnvelope`'s
 * existing `absent` state (S-50, issue #13) rather than a new "no suggestion" representation or a
 * blank suggestion area. */
export function SuggestionBlock({ suggestion }: { suggestion: SuggestionEnvelope }) {
  if (suggestion.state === 'absent') {
    return <p className="suggestion-block suggestion-block--absent">No suggestion is offered.</p>
  }

  return (
    <div className="suggestion-block">
      <span className="suggestion-block__label">Suggested change</span>
      <p className="suggestion-block__text">{suggestion.text}</p>
    </div>
  )
}
