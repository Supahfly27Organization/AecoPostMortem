# Mockup parity gaps — scored

**Date:** 2026-08-21 · **Input:** `docs/product-superpowers/discovery/2026-08-21-ui-mockup-parity.md`
**Framework:** a custom 3-axis score, per the operator's own axes (Feasibility / Effort / Must-have)
rather than RICE/ICE — there's no reach or confidence data to estimate yet, so a lighter score fits
better; see `prioritization` skill's framework-choice table.

> Effort estimates are a single-session self-estimate against a codebase this session already knows
> in depth (every file cited in the discovery doc was read directly), not a second engineer's
> independent estimate — flagged per the skill's "effort must come from engineering, not PM alone"
> principle. Treat effort as medium-confidence, not measured.

## Scoring rubric

| Axis | Scale | Meaning |
|---|---|---|
| **Feasibility** | 1 (hard/new architecture) – 5 (trivial, data already exists) | How much of what's needed is already in hand |
| **Effort (ease)** | 1 (XL, weeks+) – 5 (XS, <1 day) | Inverted so higher is always better, matching the other two axes |
| **Must-have** | 5 Must / 3 Should / 1 Could / 0 Won't | MoSCoW against this tool's actual value prop — the Digest's ranked, corpus-wide findings, not session-view polish |

**Total = Feasibility + Effort(ease) + Must-have, max 15.** Ties broken by Must-have, then
Feasibility, then left as a genuine tie (called out below, per the skill's "ties to discuss" step).

---

## Scored and ranked, highest total first

| # | Item (page) | Feasibility | Effort | Must-have | **Total** | Status |
|---|---|---|---|---|---|---|
| 1 | **Inferred findings surfaced** (Digest) | **5** — server already computes and serves `DigestEnvelope.InferredFindings`; nothing new to build server-side, and the dashed "inferred" badge CSS already exists | **5 (XS–S)** — declare the missing TS field, add one render branch reusing existing `ProvenanceBadge`/callout styling | **5 Must** — real, already-computed data currently silently dropped on arrival; near-zero cost to stop throwing it away | **15** | ✅ Done — [PR #114](https://github.com/Supahfly27Organization/AecoPostMortem/pull/114) |
| 2 | **Per-finding session strip** (Digest) | **4** — `RecurrenceEnvelope.occurrences` already carries every session id; only the corpus-wide session ordering needs threading in | **4 (S)** — pure frontend, one small component (fixed-width bar, N cells) + CSS, no backend change | **3 Should** — a real scanability win, but the expanded row's session grid already gives the same information, just less compactly | **11** | ✅ Done — [PR #115](https://github.com/Supahfly27Organization/AecoPostMortem/pull/115) (effort estimate was wrong — needed a real backend field, see the memory note) |
| 3 | **Suggested-change block label** (Digest) | **5** — the text is already rendered; this is a missing `<span>` | **5 (XS)** — a one-line JSX/CSS change | **1 Could** — the sentence already conveys the meaning without a heading | **11** | ✅ Done — [PR #116](https://github.com/Supahfly27Organization/AecoPostMortem/pull/116) |
| 4 | **Finding chip bar — wire real findings + filter** (Session) | **3** — needs `ApiHost.GetSession` to actually run the existing check orchestrators scoped to one session (currently hardcoded `[]`); the check logic already exists, the per-session wiring doesn't | **2 (L)** — real orchestration work, the same "wire N checks into an endpoint" pattern each Digest piece took a full PR to do once | **5 Must** — the most-cited "known empty" gap across both the backlog and the parity report; a session's diagnostic value is undercut while it's always empty | **10** | ✅ Done — [PR #121](https://github.com/Supahfly27Organization/AecoPostMortem/pull/121) |
| 5 | **Finding headline (narrative title)** (Digest) | **3** — no title field exists anywhere in `FindingEnvelope`; all 10 check orchestrators would need a natural-language template, not just a data-model addition | **2 (L)** — touches all 10 finding builders + the envelope + the frontend row | **5 Must** — this sentence *is* the mockup's whole pitch and the Digest's stated value prop ("what went wrong, and how often") | **10** | ✅ Done — [PR #118](https://github.com/Supahfly27Organization/AecoPostMortem/pull/118) (covered 11 producers, not 10 — the estimate missed `ToolFailureClusterFinding`) |
| 6 | **"Checks that found nothing" section** (Digest) | **4** — `SilentCheckEnvelope.From(CheckRegistry)` already exists and works per the backlog note; just never wired in | **3 (M)** — backend wiring into `DigestEnvelope`/`GetDigest` plus a new frontend section | **3 Should** — important for trust ("clean vs. never-looked"), not blocking the ranked-findings value itself | **10** | ✅ Done — [PR #117](https://github.com/Supahfly27Organization/AecoPostMortem/pull/117) (cheaper than scored — the registry was already assembled, just discarded) |
| 7 | **Violations column** (Rules) | **4** — the counts already exist and are computed for the Digest's `RuleAdherenceToolChoice` findings; needs a lookup joining rule statement → violation count within the selected version | **3 (S–M)** — one new join plus a table column, no new computation | **3 Should** — consolidation, reduces the current "one hop away" friction; not blocking | **10** | ✅ Done — [PR #122](https://github.com/Supahfly27Organization/AecoPostMortem/pull/122) (real backend gap, not the "S–M" estimate: `GetRulesInventory` had never run any of the four piece-3 check orchestrators before, only classified statements) |
| 8 | **Masthead — Subagents count** (Digest) | **5** — the `Agent` table is already populated and counted elsewhere (session masthead shows it today) | **4 (XS–S)** — add the same corpus-wide count to `MastheadCounters` | **1 Could** — a nice-to-have stat, not decision-relevant | **10** | Open |
| 9 | **Methodology footer — Digest** (Digest) | **5** — mostly static content, no new data beyond what's already computed (corpus scope, ingest timestamp) | **4 (XS–S)** — write the copy, one new footer component | **1 Could** — transparency polish, not core | **10** | ✅ Done — [PR #119](https://github.com/Supahfly27Organization/AecoPostMortem/pull/119) |
| 10 | **Step glyphs** (Session) | **5** — pure frontend, a small icon map keyed by the `step.kind` already rendered as text today | **4 (S)** — one glyph-map component + CSS | **1 Could** — visual scanability, not functional | **10** | Open |
| 11 | **Methodology footer — Session** (Session) | **5** — same shape as the Digest footer, same "already-computed data" argument | **4 (XS–S)** — write the copy, reuse the footer pattern from #9 | **1 Could** — transparency polish, not core | **10** | Open |
| 12 | **Turn grouping** (Session) | **4** — turn boundaries are already known (`Turn` entity exists, steps are already ordered); the risk is architectural, not data | **2 (L)** — restructures `Tape.tsx`'s flat list into grouped sections, which complicates the deliberately-simple fixed-row-height virtualization math already in place | **3 Should** — meaningfully improves readability, but the flat list is still fully functional — the tool's core value lives on the Digest, not here | **9** | Open |
| 13 | **Prose in transcript** (Session) | **4** — the Detail tab already renders this same prose on selection; nothing new to fetch conceptually | **2 (M–L)** — today prose is fetched per-step on click (`useStepEvidence`); inlining for every visible row means eager-fetching the whole session or reshaping `SessionEnvelope` | **3 Should** — real readability win, same "not blocking core value" reasoning as turn grouping | **9** | Open |
| 14 | **Masthead — wall-clock start→end range** (Session) | **4** — session start/end timestamps already exist in the data | **4 (S)** — one new derived field + a masthead line | **1 Could** — nice context, not decision-relevant | **9** | Open |
| 15 | **Rule coverage bar** (Digest) | **3** — the four-way counts already exist per rule-set version, but `MastheadCounters` currently only stubs "Rules not yet analysed" corpus-wide | **3 (M)** — wire corpus-wide coverage counts into the masthead + build a new proportional-bar component | **1 Could** — the backlog itself already tags this "lower priority" | **7** | Open |
| 16 | **Tape minimap** (Session) | **3** — no new data (steps already in hand client-side), but a genuinely new visual component (canvas drawing, scroll-sync) | **2 (M–L)** — canvas drawing plus keeping it in sync with scroll position | **1 Could** — the mockup's own footer calls it "decorative... not interactive"; the real tape below it already gives full access | **6** | Open |
| 17 | **Inline per-step flagbox** (Session) | **2** — needs a domain-model concept that doesn't exist today: attaching one `Finding` to one specific `SessionTapeStep` (findings are corpus/session-scoped only, never step-scoped) | **1 (XL)** — a real design question, not just wiring; likely needs its own scoping pass before effort is even reliably estimable | **3 Should** — valuable, but sequenced behind #4 (you need real per-session findings before deciding how one attaches to a step) | **6** | Open |
| 18 | **"Not checkable" status** (Rules) | **2** — not a bug: nothing in `RulesInventoryClassifier` has ever decided which normative-but-unobservable *reason* to attribute to a statement — real design work, the same "don't guess field names" caution this project has hit before | **2 (L)** — per-statement reason classification, not a simple lookup | **1 Could** — the current fallback (`CheckableNotYetBuilt`) isn't wrong, just less precise; no user-facing harm today | **5** | ✅ Done — [PR #126](https://github.com/Supahfly27Organization/AecoPostMortem/pull/126) (deliberately narrow: one real, unambiguous statement reclassified — `"Read ONLY files directly needed for the current task"`, gated on task-relevance — out of 9 real `CheckableNotBuilt` statements found in the live store; the other 8 stay `CheckableNotYetBuilt`, either genuinely still-buildable (a repeated path, a call-count threshold) or genuinely ambiguous and left conservative per this row's own guidance, not a full taxonomy) |
| 19 | **Masthead — synthesized human title** (Session) | **2** — no such field exists anywhere in Copilot's own logs; would need real summarization (e.g. from the first user message), not extraction | **2 (L, uncertain)** — effort is genuinely unclear until the summarization approach is chosen | **0 Won't** — the data model doesn't support it without a new inference step; not scoped for now | **4** | Won't (for now) |
| 20 | **Subagent lanes — inline nesting** (Session) | — | — | **0 Won't** — Part 4 of the discovery doc documents this as deliberate: concurrent subagents interleave in real wall-clock time, so a mockup-style contiguous block would misattribute rows | **0** | Won't (deliberate) |
| 21 | **No way to click through from the Digest to a session** (Digest → Session) | **5** — `/sessions/:sessionId` already exists and fully works (masthead, tape, inspector); `RecurrenceStrip` already renders every session id a finding touched, as plain text | **5 (XS–S)** — wrap the existing session-id text in `RecurrenceStrip` (and/or the `SessionStrip` cells) in a `react-router-dom` `<Link to={\`/sessions/${id}\`}>` — the router and route are already wired, this is markup only | **5 Must** — not in either mockup (neither draws cross-page navigation), but discovered live: today the only way to open a session found via the Digest is copying a UUID out of the expanded row and hand-editing the URL bar. Blocks the tool's own core loop — "found a problem, now go look at the session" — with no workaround inside the UI itself | **15** | ✅ Done — [PR #120](https://github.com/Supahfly27Organization/AecoPostMortem/pull/120) |

**10 of 21 done** (#1, #2, #3, #4, #5, #6, #7, #9, #18, #21, plus the effort-estimate corrections logged inline above — #7 among them, a real backend gap rather than the "S–M, consolidation" estimate). #18 landed narrower than a full taxonomy — see its own row. Remaining open items, highest total first: **#8, #10, #11 (three-way tie, cheap polish)**, then #12 onward.

Row 20 is not a real candidate — Part 4 of the discovery doc documents it as a deliberate,
reasoned divergence (concurrent subagents interleave in real time; a mockup-style contiguous block
would misattribute rows). Scored 0 and listed only for completeness, not for scheduling.

---

## Quick wins (Must/Should, cheap, ship first)

0. **#21, Digest→Session link (15, new top open item)** — not a mockup gap (neither mockup draws
   cross-page navigation at all), found live instead: nothing on the Digest links a session id to
   `/sessions/:sessionId`, even though that page fully works. Same shape as #1's own story — the
   destination already exists and works, a UI affordance to reach it is just missing. Cheapest fix
   on the whole list with the highest possible score; do this before anything else still open.
1. **Inferred findings surfaced (15)** — the single highest-leverage item on this list. The server
   already computes and serves `DigestEnvelope.InferredFindings`; the frontend TypeScript type
   simply never declares the field, so real, already-computed data is silently dropped on arrival.
   Declare the field, add one render branch reusing the CSS that already exists for the dashed
   "inferred" badge. Sub-day work for a Must-have.
2. **Per-finding session strip (11)** and **Suggested-change block label (11)** — both pure
   frontend, both use data already on the wire (`RecurrenceEnvelope.occurrences`, the suggestion
   text itself). Ship together as one small PR.
3. **Masthead Subagents count / Step glyphs / both methodology footers (10, tied)** — four small,
   independent, purely additive items. Any order; batch as a single "digest & session polish" pass.

## Big bets (Must-have, but real effort — plan and sequence)

- **Finding chip bar wired for real (10)** — the highest-value item that isn't cheap. `ApiHost
  .GetSession` passes a hardcoded empty list where it needs to re-run the same check orchestrators
  already built for the Digest, scoped to one session. High leverage because the check logic already
  exists — this is orchestration, not new logic — but touches the same "wire N checks into an
  endpoint" pattern each digest piece already took a full PR to do once.
- **Finding headline / narrative title (10)** — a genuine data-model gap: `FindingEnvelope` has no
  title field at all, and closing it means every one of the 10 check orchestrators needs to produce
  a natural-language sentence, not just a `recurrence.key`. Worth scoping as its own multi-slice
  piece, the same way Piece 3's five rule shapes were sequenced one at a time rather than attempted
  together.
- **Inline per-step flagbox (6)** — scores low today specifically *because* it's the least
  understood: attaching one `Finding` to one `SessionTapeStep` is a domain-model question nothing in
  this codebase has answered yet (findings are corpus/session-scoped, never step-scoped). Sequence
  behind the chip bar above — you need real per-session findings before deciding how one attaches to
  a step.

## Deprioritized (Could, or cheap-but-low-value)

Rule coverage bar (7), tape minimap (6), "not checkable" status (5), and the synthesized session
title (4) all land in the bottom half — either the backlog already called them lower-priority (rule
coverage), the mockup itself calls the element decorative (tape minimap), the gap is a deliberate
scope cut rather than a bug (not-checkable), or the underlying data plain doesn't exist yet
(synthesized title — Copilot's logs carry no session-title field; this would need real
summarization, not extraction).

## Ties to discuss

- **#4–#5 (Finding chip bar vs. Finding headline, both 10, both Must):** identical score, both
  genuinely important, both L-effort. No feasibility tiebreak separates them (both score 3). Pick
  by sequencing preference, not score — the chip bar is arguably the more self-contained of the two
  (reuses existing check orchestrators wholesale), while the headline touches all 10 orchestrators'
  output shape at once.
- **#6–#7 (Checks-that-found-nothing vs. Violations column, both 10, both Should):** same total,
  different pages, no dependency between them — safe to run as two independent, parallel pieces of
  work rather than needing to sequence.
- **#8–#11 (four items, all 10, all Could):** a genuine four-way tie on cheap polish. Grouped as one
  batch above; order within the batch doesn't matter.

## Parallel-safety for the remaining open items (updated 2026-08-21, after #4/#7/#9/#21 merged)

The first round dispatched #4, #7, #9 and #21 as four parallel subagents, each in its own isolated
git worktree, chosen specifically because a file-ownership check found them fully disjoint —
result: PRs #121/#122/#119/#120, zero merge conflicts. The same method applied to what's left below.

Remaining open items land in four file clusters. **Within a cluster, items conflict on the same
file(s) and must be sequenced, not parallelized.** Across clusters, one item from each can run at
the same time.

| Cluster | Items | Shared file(s) |
|---|---|---|
| Digest masthead | #8 (Subagents count), #15 (Rule coverage bar) | `web/src/digest/Masthead.tsx` + backend `MastheadCounters` |
| Session masthead/footer (lighter) | #11 (Session methodology footer), #14 (wall-clock start→end range) | `web/src/routes/SessionPage.tsx` — different functions (`Masthead`/a new footer mount in `LoadedSession`), same file, lower risk than the tape cluster but not zero |
| Session tape (heavy) | #10 (step glyphs), #12 (turn grouping), #13 (prose in transcript), #16 (tape minimap) | `web/src/session/Tape.tsx` — #12 restructures the flat list into grouped sections, which #10/#13/#16 all render into; sequence #12 first (or last, deliberately) rather than mixing it into a parallel batch |
| Rules classifier | #18 ("Not checkable" status) | `src/AecoPostMortem.Api/RulesInventoryClassifier.cs` — fully independent of every other remaining item, since #7 already merged its own `RulesInventoryClassifier.cs`/`RulesInventoryEnvelope.cs`/`RulesInventoryPage.tsx` changes |

**#17 (inline per-step flagbox)** is left out of the table above on purpose: beyond the file-conflict
question, it needs its own scoping pass first (a real domain-model gap — attaching a `Finding` to one
`SessionTapeStep`, which the data model doesn't support today) per the "Big bets" section above. Don't
fold it into a parallel batch with the Session tape cluster until that design question is settled.

**A safe parallel batch right now**: one item per cluster — #18 (Rules, always safe), plus a pick
from each of the other three clusters (e.g. #8, #14, #10 — the four highest-scored, lowest-risk
picks) — dispatched the same way as the first round: one subagent per item, each in its own git
worktree, each briefed on which other files are off-limits this round.

## Framework note

This is not RICE: there's no reach or confidence estimate here because none of these are
user-tested — the "must-have" axis substitutes a product judgment call (does this serve the Digest's
own stated purpose — corpus-wide, ranked, frequency-first findings — or session-view polish on a
page whose own raw data is already fully inspectable via the working Detail/Raw tabs). If this list
is revisited after real usage data exists, RICE would be the better fit per the skill's own framework
table.
