# Mockup parity — how far the built UI is from its own mockups

**Date:** 2026-08-21 · **Status:** measurement record, nothing built
**Commissioned by:** the operator, asking how far the Digest, Session view and Rules Inventory
pages have drifted from the two approved mockups.

> **What this is.** An element-by-element comparison of
> `docs/product-superpowers/discovery/mockups/{digest,flight-recorder}.html` against the live app
> (`/`, `/sessions/:id`, `/rules`), checked against a real `aecopostmortem serve` instance over the
> user's own 35-session Copilot corpus, in both light and dark theme. Every claim below was verified
> by reading source directly — `FindingRow.tsx`, `SuggestionBlock.tsx`, `SessionPage.tsx`,
> `Tape.tsx`, `RulesInventoryPage.tsx` and their matching `ApiHost` endpoints — nothing here is
> inferred from screenshots alone.
>
> **What this is not.** Not a plan, not a requirement, not approved scope. It records what was
> measured and gives product-discovery a starting inventory to prioritize from; it does not amend
> the PRD or commit to building any of it.

A rendered version with a visual parity summary is published as an Artifact:
https://claude.ai/code/artifact/b04c237a-6676-4e39-8487-86405cdf6ea8

---

## Method

Both mockup files were served locally (`python -m http.server`) and screenshotted directly in a
real browser tab. The built product was screenshotted the same way, side by side, against a real
`serve` process over the live corpus, toggling `document.documentElement.dataset.theme` to check
both themes. Where a screenshot alone couldn't settle whether something was missing vs. just not
wired up, the relevant source file was read to confirm (e.g., confirming `FindingEnvelope` has no
title field at all, not just that `FindingRow.tsx` doesn't render one).

Four statuses are used throughout:

| Status | Meaning |
|---|---|
| **Present** | Matches the mockup closely enough that the difference isn't worth tracking |
| **Partial** | The concept is built, but meaningfully thinner than what the mockup draws |
| **Missing** | Not built at all |
| **Added** | Real and useful, but the mockup never drew it — the built product does more here |

---

## Summary

| Page | Present | Partial | Missing | Added | Read |
|---|---|---|---|---|---|
| Process Digest (`/`) | 4 | 3 | 6 | 1 | the ranked list works; almost nothing explains itself |
| Session view (`/sessions/:id`) | 2 | 2 | 7 | 0 | the inspector is real; the transcript is a raw log |
| Rules Inventory (`/rules`) | 2 | 0 | 2 | 1 | closest of the three; one status never fires |

Three further divergences are **deliberate**, documented at build time, and excluded from the counts
above — see [Part 4](#part-4-by-design-not-a-gap).

---

## Part 1: Process Digest (`/`)

The mockup's whole pitch is that a finding is a written sentence about a problem —
*"The sessionStart hook fails in almost every session"* — with a measured stat line under it. The
built row shows the same underlying data, but the headline *is* the raw key the finding is grouped
by: a tool name, a rule's own text, verbatim. **There is no title field in the wire contract at
all** — `FindingEnvelope` has no separate name or summary, only `recurrence.key`, confirmed by
reading `FindingRow.tsx` directly. That is a data-model gap, not a rendering one — closing it needs a
new field on the server contract, not a frontend change.

| Mockup element | Mockup | Built product | Status |
|---|---|---|---|
| Finding headline | A written sentence naming the problem, plus a measured stat sub-line | The bare `recurrence.key` — a tool name or a rule's own text — no sentence, no sub-line | Missing |
| Per-finding session strip | A 35-cell bar — lit cells mark which sessions this finding touched, at a glance, on the collapsed row | Not rendered collapsed; expanding shows a literal grid of every session's UUID instead | Missing |
| Ranked, expandable findings | Click a row for evidence + a suggested change | Same interaction, same data underneath | Present |
| Provenance badges | Observed / Derived / Inferred, three distinct colors + a dashed border for inferred | Observed / Derived render correctly — but no finding ever arrives as Inferred (see below) | Partial |
| Inferred findings | Dashed-border callout boxes — the contradiction check, the subagent-cost "reading, not the numbers" | `DigestEnvelope.InferredFindings` is computed and served — the frontend type just never declares the field, so it's dropped on arrival | Missing |
| "Checks that found nothing" | A section naming every clean check, so silence reads as "checked" not "never looked" | `SilentCheckEnvelope.From(CheckRegistry)` exists and works — nothing calls it | Missing |
| Suggested-change block | Labeled `Suggested change` above the sentence | Same sentence, same accent-bordered box — no label above it | Partial |
| Rule coverage | A proportional four-color bar in the masthead — watched / checkable / unobservable / not-a-rule, with a legend | Plain text: *"Rules not yet analysed"* | Partial |
| Masthead stat strip | Sessions / Span / Repositories / Events / Tool calls / Subagents — 6 cells | Same 5 cells, minus Subagents | Partial |
| Methodology footer | What was measured, what's a placeholder, how recurrence positions are sourced | No footer | Missing |
| Every-rule table on this page | A filterable, union-of-all-versions rule table lives at the bottom of the digest itself | Moved to its own route, `/rules`, scoped to one version — deliberate, see Part 4 | Redesigned |
| Repository selector | Not drawn — the mockup assumes one corpus | A real, working `<select>` over every repository in the corpus | Added |

**Incidental, not a mockup gap:** the evidence block for the `sessionStart` hook failure currently
renders its stderr capture with raw, unstripped ANSI escape sequences (literal `␛[31;1m…␛[0m` runs)
— the mockup's own evidence text is always clean. Worth a follow-up regardless of mockup parity.

---

## Part 2: Session view (`/sessions/:id`)

"Flight Recorder" is the mockup's own framing device: a readable transcript grouped by turn, a
black-box minimap, subagent work nested where it actually happened. The built page keeps the
black-box *data* — every step is there, correctly ordered, correctly attributed — but presents it as
one flat, virtualized list rather than a reconstructed recording.

One naming collision is worth flagging on its own: in the mockup, `.tape` is a decorative minimap
sitting *beside* the actual transcript. In the built app, `Tape.tsx` *is* the transcript itself — the
name survived, the concept it named did not.

| Mockup element | Mockup | Built product | Status |
|---|---|---|---|
| Tape | A sticky vertical minimap — tick marks for all 764 calls, colored highlight bars at flagged moments — a decorative overview | Same name, different thing: `Tape.tsx` is the actual scrollable, virtualized step list itself. No overview exists | Missing |
| Turn grouping | Steps nested under `TURN 1`, `TURN 2`… headers, each timestamped into the session | One flat, chronological list — no turn boundary is shown anywhere | Missing |
| Prose in the transcript | "you said" / "agent said" quoted blocks, readable inline, no click required | A generic `PROMPT — Completed/Aborted` row; the actual sentence only appears after selecting it and reading the Detail tab | Missing |
| Step glyphs | A one-character mark per step kind — `■` tool, `M` mcp, `S` skill, `▶/◀` agent start/end | A plain uppercase text label (`HOOK`, `TOOL CALL`) in the kind column | Missing |
| Finding chip bar | Chips (rule adherence %, re-read count, failed calls, aborts, hook failures) — click to filter the transcript to just those steps | `ApiHost.GetSession` passes a hardcoded empty list — the chip UI exists and renders nothing; no filtering interaction exists to build on yet | Missing |
| Inline per-step flagbox | The rule explanation, provenance and suggestion sit directly under the offending step, in place | No field on `SessionTapeStepEnvelope` to attach a finding to one step at all | Missing |
| Subagent lanes | Nested inline in the transcript, indented under the parent turn, at the exact point spawned | A separate section between the chip bar and the tape — correlated to tape rows only by matching border color, deliberately (see Part 4) | Partial |
| Masthead | A human title ("Money movements — confirm flow"), wall-clock start→end range, then the stat strip | The same stats as a vertical key/value list — no title field exists on `SessionMasthead`, no wall-clock range, only elapsed duration | Partial |
| Inspector — Detail / Thinking / Raw | Three tabs, one step selected at a time | Same three tabs, same empty-state copy almost verbatim ("Pick a step…") | Present |
| Step selection | Click a row, see its detail | Same, plus full keyboard reachability (roving tab stop, arrow/home/end/page) the mockup doesn't attempt | Present |
| Methodology footer | What's measured vs. representative, per-model thinking-readability context | No footer | Missing |

---

## Part 3: Rules Inventory (`/rules`)

The closest of the three pages to its mockup, and the one place the built product does more than the
mockup drew. The core four-way status split (watched / checkable-not-built / not-checkable /
not-a-rule) is real on both sides. The built table adds version-scoping columns the mockup never
needed at its own single-snapshot granularity — but drops the violation count into the Digest
instead of keeping it here, and one whole status has never once fired against the real corpus.

| Mockup element | Mockup | Built product | Status |
|---|---|---|---|
| Four-way status | watched / checkable, not built / not checkable / not a rule | Identical vocabulary, identical styling intent (no color read as "problem" per `data-emphasis="neutral"`) | Present |
| Rule / Source / Sessions / Status columns | Core table shape | Same core columns, same intent | Present |
| Violations column | A per-rule violation count sits directly in this table | Not here — violation counts live in the Digest's `RuleAdherenceToolChoice` findings instead, one hop away | Missing |
| "Not checkable" status | 9 of 22 real rules render this way — normative but nothing observable | The classifier never emits it — every unmatched-but-directive statement maps to `CheckableNotYetBuilt` instead, by deliberate scope cut | Missing |
| In-force window & retirement | Not drawn — the mockup's flat table has no version-lifecycle concept | Two real columns: the window a statement was in force, and its retirement date if superseded — visible, legible, never dimmed | Added |

---

## Part 4: By design, not a gap

Three places the built product structurally disagrees with the mockup on purpose, each decided and
documented at build time rather than drifted into. Excluded from the counts above.

1. **One rule-set version at a time, not the union of every version ever seen.** The mockup's own
   footer text calls its all-versions table a simplification for the purpose of the mockup — the
   built product scoped to one version with a picker, per FR-40's actual Gherkin scope.
2. **Subagent lanes as a parallel section, not inline nesting.** The tape is one flat,
   wall-clock-ordered list because concurrent subagents genuinely interleave in time — a
   block-grouped renderer (what the mockup draws) would either misattribute an interleaved row or
   have to re-sort around it. Lanes correlate to tape rows by a shared border color instead.
3. **Every-rule table moved off the Digest page onto its own route.** Splitting "what went wrong"
   from "every rule that exists" into `/` and `/rules` keeps each page answering one question — the
   Digest's own finding rows already name the rule text they're about via `recurrence.key`.

---

## Cross-reference

Four of the gaps above are already tracked, one item each, in the project's mockup gap backlog
(memory: `aecopostmortem-mockup-gap-backlog`): the silent-checks section, invisible inferred
findings, empty session finding chips, and no per-step finding attachment. This document adds, on
top of that backlog: the missing finding-title field (a new, more foundational gap than the backlog
previously named), the session-view turn/prose/glyph/minimap gaps (not previously catalogued at
all), the Rules Inventory violations-column and not-checkable gaps, and the incidental ANSI-escape
rendering bug.
