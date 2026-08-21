import type { SessionMasthead } from '../api/session'
import './MethodologyFooter.css'

const count = new Intl.NumberFormat('en-GB')

const day = new Intl.DateTimeFormat('en-GB', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
})

function plural(n: number, singular: string, pluralForm = `${singular}s`): string {
  return `${count.format(n)} ${n === 1 ? singular : pluralForm}`
}

/**
 * Mockup parity item #11 (`docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`,
 * "Methodology footer — Session"): the session Flight Recorder's sibling to the Digest's own
 * `digest/MethodologyFooter.tsx` (item #9). The mockup's own footer (`flight-recorder.html`) states
 * one fixed set of numbers hand-typed for one frozen session on one frozen date — this component
 * states the same three things, but every figure is read straight off the `SessionMasthead` this
 * page already fetched, the same "nothing on this page counts anything" discipline `Masthead`
 * (`routes/SessionPage.tsx`) follows for its own fields. No new fetch, no recomputation.
 *
 * The third paragraph deliberately carries no live number: `readabilityByModel` is served per step,
 * on demand, once a Thinking-tab step is selected (`StepEvidenceEnvelope`, see
 * `AecoPostMortem.Api/CLAUDE.md`'s FR-23 remarks) — it is not part of `SessionMasthead`, so eager-
 * fetching it here would need a fetch this story doesn't call for. This paragraph instead explains
 * the general, always-true mechanics of that split, matching item #9's own "no new fetch" scope.
 *
 * `plural`/`day` are reimplemented locally rather than imported from `digest/MethodologyFooter.tsx`
 * — the same "don't share across page-specific components" precedent that file's own remarks
 * establish for why it reimplemented its own formatters rather than importing from `Masthead.tsx`.
 */
export function MethodologyFooter({ masthead }: { masthead: SessionMasthead }) {
  return (
    <footer className="methodology-footer">
      <p>
        This session was measured from its own masthead — {plural(masthead.turnCount, 'turn')},{' '}
        {plural(masthead.toolCallCount, 'tool call')}, {plural(masthead.subagentCount, 'subagent')}{' '}
        and {plural(masthead.skillCount, 'skill invocation')}, recorded {day.format(new Date(masthead.startedAt))}.
        Every figure here is a served count, never recomputed on this page. The tape below lays out
        real steps in the order Copilot recorded them; each row is representative of what the log
        carries, not a verbatim transcript of every field a step recorded.
      </p>
      <p>
        Rule findings on this page are tool-choice checks — which tool was called, with which
        operand, in what order — never a check against the content of code the agent wrote. That is
        not a gap specific to this session: every rule check this app runs today is shaped that way,
        so a code-content violation could not surface here regardless of what this session's own
        findings happen to show.
      </p>
      <p>
        The Thinking tab&rsquo;s empty state, when it appears, measures rather than assumes: opening
        Thinking on any selected step shows this session&rsquo;s own readable-versus-encrypted
        reasoning share for the model that produced it, computed per model and fetched only once a
        step is selected — never a corpus-wide constant, and never averaged across two models one
        session used.
      </p>
    </footer>
  )
}
