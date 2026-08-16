# Copilot Session Post-Mortem — Product Discovery

**Date:** 2026-08-16 · revised the same day after adversarial review
**Status:** Approved 2026-08-16 — cleared to PRD by the operator
**Product:** Standalone. Explicitly **not** part of AecoLedger Insights — no shared entities, no shared
store, no shared UI. Insights asks "where did the tokens go"; this asks "where did my *process*
fail". The operator has ruled out a connection.

> Discovery conducted 2026-08-16. Two evidence sources: a structured interview with the operator
> (the sole user), and a direct measurement of their local Copilot corpus — 35 sessions,
> 56 138 events, measured the same day. Corpus method and field-level detail:
> `docs/product-superpowers/discovery/2026-08-16-copilot-ingestion-data-map.md`.
>
> **Two review passes were run on this document and its mockups.** Their material corrections are
> incorporated throughout rather than appended; the audit trail is in the git history and summarised
> in the closing section.

---

## Problem Statement

The operator writes rules to steer coding agents — `AGENTS.md`, `CLAUDE.md`, MCP server choices —
and has **no way to find out whether any of it worked**. Rules are written once, injected into every
session, and never checked again. When a session goes badly there is no post-mortem: the evidence is
a 5 MB JSONL file nobody reads, so the response is to guess at a rule change and hope.

The cost is not tokens. It is that **the process never converges**: the same drift recurs across
sessions because nothing closes the loop between "I wrote a rule" and "the agent obeyed it".

## Desired Outcome

The operator's own framing of the gap, given during the interview, is the outcome this product must
move: *"I knew some of them but not all. I don't know how often those occur."* The value is
**frequency on suspected problems**, not novelty. That rules out a "surprising findings" gate.

> **Primary — recurrence is quantified.** For every problem the operator can name, the product states
> how many of their sessions it touched, with evidence. Baseline: **0 of the problems in this
> document had a known frequency before it was written.** Target: **every finding class carries a
> session count and a per-rule-version trend within the first release.**

> **Secondary — a rule edit becomes measurable.** Target: after one deliberate rule change, the
> operator can see adherence before and after it, scoped to the two rule versions either side.
> The corpus already contains such an edit — measured 2026-05-23, adherence **41.8% → 71.7%** — so
> this is demonstrable on existing history rather than dependent on future sessions.

**Guardrail (counter-metric).** Adherence rising is not evidence the findings were right. The
document's own stated anxiety is a tool that launders guesses into process changes, so the product
must also track **the share of suggested fixes the operator rejects**, and **the share of acted-on
findings that were Inferred rather than Observed**. A rising adherence curve alongside a rising
rejection rate means the tool is being ignored where it matters and obeyed where it does not.

**Timeframe.** All three are scored at the end of the first release, not per "cycle" — a cycle is
defined by operator activity and therefore cannot expire.

**Deliberately not a metric:** a single corpus-wide adherence percentage. The section on rule
versions and the section on operand resolution together show that no such number is well-defined.

---

## Jobs-to-be-Done

### Primary job

> **When** a Copilot session ends and something felt off — it wandered, it re-read the same file
> forever, it ignored what I told it —
> **I want to** see exactly where it diverged from the process I defined, with the evidence,
> **so I can** fix the instruction instead of re-explaining it in the next session.

### Secondary job

> **When** I am deciding what to change about my setup — another MCP, a tighter rule, a rule I
> should delete —
> **I want to** see which problems actually recur across all my sessions, ranked,
> **so I can** spend my editing effort where it pays instead of on whichever session annoyed me most
> recently.

The second job is why the operator chose a cross-session digest as the front door, and why the
primary metric is recurrence. One bad session is an anecdote; the same drift in fifteen is a process
defect.

### Forces of Progress

| Force | What it is here |
|---|---|
| **Push** | A session goes badly and the cause is unknowable. The transcript exists but is unreadable at 5 MB. Rules get rewritten on instinct. |
| **Pull** | "This rule was followed in 3 of your sessions and ignored in 9, here are the calls, and here is the tool-reliability number that may explain it" — a named cause with evidence attached, and a frequency. |
| **Anxiety** | A tool that *invents* plausible-sounding causes is worse than no tool: it would launder guesses into process changes. Every finding must be traceable to a field, or clearly labelled as judgment. |
| **Habit** | Skimming the terminal scrollback while it is still on screen, then moving on. Costs nothing, catches nothing, and is what happens today. |

### Job Map

| Step | Today | With the product |
|---|---|---|
| Define | "That session felt bad" | A named finding class |
| Locate | Scroll terminal history | Digest ranks recurring problems; drill into the sessions and moments |
| Prepare | Re-read a 5 MB JSONL by hand | Evidence is attached to the finding |
| Confirm | Gut feel | The rule text and the violating calls, side by side |
| Execute | Guess a rule edit | Accept or reject an attached suggestion |
| **Monitor** | **Nothing** | **Re-measure adherence either side of the rule edit** |
| Modify | — | The next digest shows whether it moved |
| Conclude | — | Rule retired, kept, or rewritten |

**The Monitor step is the product's spine.** Every competitor-shaped thing in this space is a viewer.
A post-mortem that cannot tell you whether last month's fix worked is just a nicer transcript.

---

## Research Findings

### Method

The operator is the sole user, so "user research" is a structured interview with them plus analytics
over their own corpus. Analytics were run **before** the interview questions were framed, so the
questions were shaped by what the data could actually support — not the other way round.

### Interview outcomes

| Question | Answer |
|---|---|
| What counts as a failure | Rule violations / process drift; wasted effort; **missing capability**. **Bad outcome explicitly excluded.** |
| When they would read it | Both per-session and cross-session — **digest is the front door**, session is the drill-in |
| What they want to walk away with | **Findings first, suggestion attached** — evidence is primary, the proposed fix is secondary and refusable |
| How far outside the logs | **Logs only, fully offline** |
| Which findings were already known | Some, not all — *"I don't know how often those occur"* |
| Whether rules may be hard-coded | **No.** Checks must be derived from whatever rules a repository actually contains |

Excluding "bad outcome" is the most consequential answer: it removes git and GitHub from v1
entirely, along with auth, network, rate limits, and the whole question of what "the work was
correct" means. Every finding is then about *behaviour*, which is exactly what a rule can change.

### 1. Rules are extractable dynamically, per repository — this is the foundation

Copilot injects each repository's instruction files verbatim into `system.message` as
`<custom_instruction>` blocks, tagged by source file. Nothing needs to read the repository.

Measured: 335 blocks headed `CLAUDE.md`, 89 headed `AGENTS.md`, 4 `Agent workflow`, 1
`Copilot instructions`.
Measured: blocks appear in 32 of 35 sessions across 3 distinct repositories, each carrying its own
set. Measured: extraction yielded 43 distinct rule statements.

This is strictly better than parsing the repository, because the log records the rules **as they
stood when the session ran** — already merged, already overridden — which is what makes the Monitor
step possible at all.

### 2. Checking must be pattern-driven, and demonstrably can be

The operator's constraint is that no rule may be hard-coded: the tool will run against many
repositories and must check only the rules each one actually contains.

Measured 2026-08-16, with a checker that names **no tool and no repository**. The tool vocabulary is
discovered from the logs — a measured 61 distinct tools — and operands are extracted from the rule
text:

| Check shape | Rules matched | Operands extracted from the rule text |
|---|---|---|
| `prefer-A-over-B` | 3 | A = `codebase-memory-mcp`, B = `Glob/Grep/Read` |
| `never-read-path` | 2 | A = `UpFront.Data/Migrations/` |
| `tool-is-banned` | 1 | A = `search_code` |
| `use-A-after-B` | 1 | A = `get_code_snippet`, B = `search_graph` |
| `always-pass-param` | 1 | A = `model` |

**8 rules became checkable with nothing hard-coded**, including every rule previously checked by
hand. The catalogue holds *shapes*, not rules, so a repository whose rules this author never saw
still gets checked. This is a hard requirement, not an optimisation.

### 3. Adherence is dominated by operand resolution — the largest measurement caveat in this document

The navigation rule reads *"Prefer querying codebase-memory-mcp over Glob/Grep/Read for
navigation"*, and is carried by a measured 12 sessions. Its adherence depends almost entirely on how
its words are mapped onto tools that exist:

| Resolution of operand B | Measured A / B calls | Measured adherence |
|---|---|---|
| The rule's own operands, resolved dynamically | 155 / 115 | **57.4%** |
| Search tools only (`rg`, `grep`, `glob`) | 155 / 479 | **24.4%** |
| Search tools plus `view` | 155 / 1 325 | **10.5%** |

A **fivefold spread on one rule and one corpus**, from a choice the rule never states. Two concrete
causes, both measured:

- The rule says `Read`. Copilot has no file-reading tool of that name — string matching resolves it
  to `read_agent` and `read_powershell`, which read subagent and shell output, not files. The tool
  that actually reads files is `view`, used 5 201 times, and **the rule never names it**.
- The rule says `codebase-memory-mcp`. The logs also contain `codebase-memory-*` tools without the
  `-mcp` segment, which a literal match excludes. That is the gap between the measured 155 here and
  the looser count of 196 measured by an earlier script.

**Consequence for the product:** an adherence figure that does not state its resolution is
unfalsifiable. Every such figure must ship with the mapping it used, and a rule whose operands
resolve poorly is itself a finding — *"this rule names tools your agent does not have"* is more
actionable than any percentage.

### 4. Adherence must be scoped to a rule version, and then the Monitor step already works

Rules are edited continuously — the operator does this deliberately to tune the process.

| Repository | Measured sessions | Measured span | Measured rule-set versions |
|---|---|---|---|
| `supahfly27/UpFront` | 25 | 32 days | 6 |
| `Supahfly27Organization/UpFront` | 5 | 3 days | 1 |
| unattributed | 2 | 73 days | 2 |

Measured: 39 of 43 rule statements have first and last sightings on different dates; 34 of 43 are
absent from the most recent session.

Scoped per version, using the rule's own operands:

| Version | Repository | Sessions | Measured adherence | Window |
|---|---|---|---|---|
| `1a47450a` | `supahfly27/UpFront` | 3 | **41.8%** | 2026-05-22 … 05-23 |
| `9579a981` | `supahfly27/UpFront` | 4 | **71.7%** | 2026-05-23 … 05-31 |
| `54cd2cff` | `Supahfly27Organization/UpFront` | 5 | **58.6%** | 2026-07-19 … 07-22 |

Measured range 41.8% – 71.7%. **No single corpus-wide number describes any version that existed.**

The first two rows are consecutive, split by the 2026-05-23 rule edit: adherence moved
a measured **41.8% → 71.7%** across it. That is the Monitor step, on history already on disk. It moves the
product's differentiating capability out of a later phase. Sample sizes are 3 and 4 sessions, so
this demonstrates the *method*, not a conclusion about that edit.

### 5. Tool reliability may explain the adherence gap — stated as a hypothesis

Measured failure rates:

| Tool | Measured failures / calls | Measured rate |
|---|---|---|
| `web_fetch` | 112 / 183 | 61.2% |
| `codebase-memory-mcp-search_graph` | 11 / 37 | 29.7% |
| `codebase-memory-mcp-search_code` | 15 / 53 | 28.3% |
| `create` | 7 / 53 | 13.2% |
| `task` | 21 / 486 | 4.3% |
| `apply_patch` | 12 / 381 | 3.1% |
| `view` | 136 / 5 201 | 2.6% |
| `rg` | 16 / 1 346 | 1.2% |

The mandated MCP fails roughly one call in three; the deprioritised search tools fail closer to one
in fifty. **The reading that this makes the rule wrong rather than disobeyed is Inferred, not
Observed** — the rates are measured, the causal link is judgment, and the product must label it that
way.

Checked for circularity: the calls violating the `search_code` ban are not what produces that tool's
failure rate.
Measured — inside the sessions carrying the ban, 15 calls produced 1 failure.
Measured — outside them, 49 calls produced 15 failures. The two findings are independent.

Even so, this is the strongest argument for building the product: neither number is visible today,
and either alone points at the wrong fix.

### 6. A second rule is measurable, but its violation rate is not established

The system prompt marks parallel tool calling `CRITICAL`. Measured across 7 449 tool-issuing
assistant messages, 3 249 issued exactly one call — a measured **43.6% single-call rate**.

**This is a base rate, not a violation rate.** The rule is conditional — parallelise *when there are
multiple independent operations* — and whether a second independent call was available at each point
was not measured. It demonstrates that a second rule is *measurable*; it does not demonstrate that
it was violated.

### 7. Waste and silent failures nobody reported

| Finding | Measured |
|---|---|
| Same file re-read ≥4× in one session | 16 of 35 sessions; worst single path read **74×**; worst session 1 367 such reads over 90 paths |
| `sessionStart` hook failing | 35 failures — in **34 of 35 sessions**, or **32 of 33** counting only sessions that made a tool call |
| Aborted turns | **9 aborts across 8 sessions** |
| Returns to an earlier declared phase (`report_intent` churn) | worst session 104 returns across 352 intents |
| Permission interruptions | 1 033 prompts |
| Questions put to the operator | 124 |

Both hook denominators are correct and are stated together deliberately: a figure whose population
is unstated invites exactly the contradiction two surfaces showing different denominators produced.


### 8. Operand resolution, solved: four layers, nothing hard-coded

Finding 3 leaves adherence undefined without a stated resolution. This is the resolution, built and
measured 2026-08-16.

**Tool roles are derived from the argument shapes in the logs**, not from a table anyone wrote. A
tool taking `path` but no `pattern` reads files; one taking `pattern` searches; `old_str` or
`file_text` means it writes.

| Derived role | Measured tools | Dominant tool |
|---|---|---|
| `file-read` | 3 | `view`, measured 5 201 calls |
| `search` | 5 | `rg`, measured 1 346 calls |
| `file-write` | 2 | `edit`, measured 239 calls |
| `shell` | 1 | `powershell`, measured 3 504 calls |
| `spawn` | 1 | `task`, measured 486 calls |

Resolution then runs in four layers, most confident first:

1. **Exact tool name.**
2. **MCP server scope** — matches on the logged `mcpServerName` field rather than string-matching the
   tool name. This is what excludes `github-mcp-server-search_code` from a rule about
   `codebase-memory-mcp`, which measured 28 tools under this layer.
3. **Role** — an English word maps to a derived role. `read` / `view` / `open` map to `file-read`.
4. **Unresolved** — reported as a finding, never dropped silently.

Measured on the navigation rule: `Glob/Grep/Read` resolves through layers *exact, exact, role*, and
**`Read` now correctly reaches `view`** — the failure that made every earlier adherence figure wrong.

**Known defect, unfixed:** the role layer pulled a tool into operand B that also belongs to operand
A. Operands must be subtracted, with A winning ties.

### 9. The finding that matters more than any percentage: a rule can name the wrong tool

The role layer makes a new check possible — *does the rule name the tool that dominates the role it
targets?* Measured on the navigation rule:

| Role targeted | Dominant tool | Named by the rule |
|---|---|---|
| `file-read` | `view`, measured 5 201 calls | Yes, via the role layer |
| `search` | `rg`, measured 1 346 calls | **No** |

The rule says *"Grep"*, which exactly matches `grep` — measured 129 calls. The tool doing that job is
`rg`, measured 1 346 calls, and the rule never names it. **This is more actionable than any
adherence number**, because the fix is concrete: rewrite the rule in the agent's own vocabulary.

### 10. Most rules constrain code, not tool choice — and that bounds finding class 1

Tested against a genuinely independent corpus: Claude Code stores `nested_memory` attachments holding
`CLAUDE.md` verbatim, so rules from repositories outside the Copilot corpus are available.

Measured: 60 `CLAUDE.md` files across 4 repositories, 427 distinct rule bullets, of which 105 are
normative. **6 matched a check shape — a measured 5.7% of normative bullets.**

The shapes do fire on repositories they were not derived from, so they generalise. But the miss list
explains the low number, and it is not a shape problem:

- *"Use `apiFetch()` for authenticated business API calls — never raw `fetch()`"*
- *"Controllers inject module `I*Service` interfaces — never `UpFrontDbContext`"*
- *"`OrderSagaEvent` is append-only — only `AddAsync`, never `UpdateAsync`"*

**Most rules govern the code the agent writes, not the tools it calls.** Tool-call events cannot see
that, so finding class 1 as originally scoped is materially thinner than the earlier checkability
figure implied.

### 11. Code rules *are* checkable offline — but only when scoped, and unscoped they lie

The agent's writes are in the logs: `apply_patch` carries the patch body, `edit` carries `new_str`,
`create` carries `file_text`.

Measured: 673 write operations across 20 sessions — 842 write units once patch envelopes are split
per file.
Measured: 1 538 717 characters of written content, including 22 089 added lines.
Measured: 380 distinct files touched, and 842 of 842 write units carry a usable file path.

A `forbidden-symbol` shape extracts the banned symbol from the rule and searches that content.
Measured against the three rules above:

| Rule | Measured unscoped hits | Measured scoped hits | Measured false-positive share |
|---|---|---|---|
| Controllers never inject `UpFrontDbContext` | 194 | **0** | 100% |
| `OrderSagaEvent` never `UpdateAsync` | 61 | **0** | 100% |
| Never raw `fetch()` in frontend sources | 33 | **20** | 39% |

**Two of the three rules were wholly false positives before scoping**, measured. Both are scoped by the rule
itself — one to controllers, one to an entity — and the naive check ignored that scope and reported
violations that do not exist.

The verdict is therefore *yes, with a condition*: the content is present, the paths needed to scope
it are present on every write unit, and a scoped check measured 20 plausible hits across 14 files and
3 sessions on the one rule that survived. But an unscoped content check is worse than no check, and
scoping is the same unsolved problem operand resolution has, in a second domain.

**Not tested:** whether the measured 20 surviving hits are real violations rather than the wrapper's
own implementation. `edit` also captures only the replaced fragment, so a rule broken in untouched
code is invisible to this method.

---

## Opportunity Assessment (SVPG)

**1. Exactly what problem will this solve?**
The feedback loop between writing an agent rule and knowing whether it worked is entirely absent. The
product closes it: it extracts the rules a repository actually carries, measures adherence per rule
version, surfaces drift and waste with evidence, and re-measures either side of an edit.

**2. For whom?**
One operator running Copilot CLI against their own repositories, who already invests in written
process and therefore has something to measure adherence *against*. A user with no written rules
gets nothing from finding class 1 — a real constraint, not a persona sketch.

**3. How big is the opportunity?**
Measured locally: 35 sessions over 111 days, 56 138 events, 16 085 tool calls, 3 repositories. Every
finding class already fires on that corpus. Beyond this operator the market is unassessed and
deliberately out of scope.

**4. How will we measure success?**
The Desired Outcome's three measures: recurrence quantified for every named problem; one rule edit
measured either side; and the rejection-rate guardrail. The earlier "≥3 unanticipated findings" gate
is **retired** — the operator's answer was that the problems were partly known and the *frequency*
was not, which makes novelty the wrong axis.

**5. What alternatives exist?**
Reading the JSONL by hand — possible, never done, a measured 176.7 MB across the corpus. Terminal
scrollback — ephemeral. General agent-observability products render traces and leave interpretation
to the human. **No competitive scan was performed**, so the claim that none of them joins rule text
to tool calls is an `estimate` based on the category's shape, not a verified finding. Running
`competitive-analysis` before the PRD would settle it.

**6. Why are we best suited?**
The unfair advantage is measured: **the rules and the behaviour are in the same file.**
`system.message` carries the verbatim `<custom_instruction>` blocks — measured across 337 system
messages, of which a measured 335 contain the string `CLAUDE.md`.
Measured: 158 contain `Repo Rules` and 93 contain `codebase-memory`. The measured 16 085 tool calls sit beside them in the same `events.jsonl`.
Adherence is a join, not a model. That a third party could not do this is an `estimate` — it follows
from the data being local, and was not verified against named competitors.

**7. Why now?**
Copilot rotates its on-disk history: the corpus spans a measured 111 days and older sessions are
gone. Every week without ingestion is process history permanently lost, and the retroactive Monitor
capability depends on that history surviving.

**8. How will we get this to market?**
Local, offline, single user. No distribution problem to solve in v1.

**9. What factors are critical to success?**
- **Dynamic rule checking.** Check shapes parameterised from rule text, never per-rule code.
- **Stated operand resolution.** Every adherence figure ships with the mapping that produced it.
- **Rule-version scoping.** No figure spans an edit.
- **Provenance per finding.** Observed / Derived / Inferred, rendered differently.
- **The Monitor step.** Before/after across a rule edit, or the loop does not close.
- **Refusable suggestions**, and a coverage statement so silence never reads as compliance.
- **Self-exclusion.** Sessions that analyse agent failure contaminate string-derived signals.

**10. Is it worth pursuing?** <!--src: SVPG question number, not a quantity-->
**Yes.** The data is present and measured, the loop is genuinely absent, the retroactive Monitor
works on existing history, and the offline-only scope keeps v1 small.

---

## Non-Goals

Stated in one place so an engineer knows where to stop.

| Not in scope | Why |
|---|---|
| Whether the work was *correct* — merged, reverted, tests passing | Operator excluded it; needs git and GitHub |
| Any network call, auth, or remote service | Logs only, fully offline |
| Model recommendations | Reasoning readability is model-dependent, but advising a model change is out of scope |
| Fixing the operator's `sessionStart` hook | A bug they own; the product only reports it |
| Per-subagent rule attribution | `system.message` carries no `agentId`; a subagent's system prompt is never written |
| Rules that cannot be mechanically checked | Listed and labelled, never silently skipped, never guessed at |
| Any market beyond this operator | Unassessed by choice |
| Sharing code, storage or UI with AecoLedger | Standalone product |

---

## Validation Results

| Assumption | Result |
|---|---|
| Rules are recoverable from the logs | **Confirmed.** Verbatim, measured on 337 system messages across 3 repositories |
| Rule checking can be dynamic rather than hard-coded | **Confirmed.** 5 check shapes matched 8 rules with no tool or repository named |
| Rule adherence is computable | **Confirmed**, with a caveat: computable per rule version and per stated resolution, not as one number |
| Adherence is a stable quantity | **Refuted by measurement.** A measured fivefold spread by resolution choice, and 41.8%–71.7% across versions |
| The parallel-call rule is violated | **Not established.** A measured 43.6% base rate; the conditional was never evaluated |
| Waste is detectable without external data | **Confirmed** — repeated reads, failed calls, phase churn, aborts |
| Findings will surprise the operator | **Refuted as the right test.** The operator answered that some were known and the frequency was not; recurrence replaced novelty as the metric |
| Bad-outcome data is needed for v1 | **Refuted by the operator** — explicitly excluded |
| Per-subagent rules are recoverable | **Refuted by measurement.** `system.message` carries no `agentId` |
| Rules contradict each other across repositories | **Refuted by measurement.** The two statements were sequential in one repository; 0 sessions carry both |

---

## Recommendation

**Pursue**, as a standalone offline tool.

### Three finding classes, built in order of epistemic strength

1. **Rule adherence** (Observed + Derived) — extract rules, match them to check shapes, measure per
   version with the resolution stated. The differentiator.
2. **Waste** (Derived) — repeated reads, failed calls, phase churn, aborts. Cheapest, fires on the
   most sessions, proves the pipeline end to end.
3. **Written-content rules** (Derived) — forbidden symbols and required wrappers, checked against
   the `apply_patch` / `edit` / `create` content the logs already carry. This is where most of the
   operator's rules actually live, per the code-versus-tool-choice finding, so it is not optional.
   It ships only with
   scoping: unscoped, it measured as wholly false-positive on 2 of 3 rules.
4. **Missing capability** (Inferred) — tool-failure clusters and hand-rolled sequences. Highest
   value, weakest provenance. Built last and rendered visibly differently.

### Three surfaces

- **The digest is the front door** — recurring problems ranked by sessions affected, each with a
  count and a per-rule-version trend.
- **The session post-mortem is the drill-in** — one session, its findings, its evidence.
- **The Rules inventory** — every extracted rule, with *in force* / *retired* dates and one of three
  statuses: **Watched**, **Checkable — not yet built**, **Not checkable** with a reason. A retired
  rule stays visible with its adherence frozen at retirement.

**Every finding carries** what happened, the evidence, its provenance, its recurrence, the operand
resolution where one applies, and a **refusable** suggested fix.

### Phasing

| Phase | Contains | Exit criterion |
|---|---|---|
| **A — Read** | Ingest `~/.copilot/`, reconstruct sessions, agents and steps | Every session reconstructs; a re-run adds nothing |
| **B — Waste** | Finding class 2 and the session view | The repeated-read and hook findings reproduce, with denominators stated |
| **C — Adherence + Monitor** | Check shapes, rule-version scoping, the digest, the Rules inventory, **and the before/after comparison** | The per-version table in finding 4 reproduces from the product, including the 41.8% → 71.7% edit |
| **D — Capability** | Finding class 3, rendered as Inferred | An Inferred finding is visibly distinguishable from an Observed one |

The Monitor step moved from last to Phase C: it needs no new sessions, only rule-version scoping,
which Phase C builds anyway.

### Key risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| **Scoping is unsolved, in two domains** | Unscoped content checks measured 100% false positives on 2 of 3 rules, and operand resolution moves adherence fivefold | Scope before matching: rule-declared path patterns and entity names for content, same-server and A-minus-B for operands |
| **Finding class 1 is thinner than it looked** | A measured 5.7% of normative bullets match a tool-choice shape; most rules constrain code | Ship the written-content class alongside it, not after it |
| **Operand resolution is partly solved** | A measured fivefold adherence spread comes from it alone; a wrong mapping produces a confident wrong number | Ship the resolution with every figure; treat poor resolution as its own finding; prefer same-server matches and word boundaries |
| **Check coverage is partial** | A measured 10 of 43 statements are checkable — a floor, but far from all | The Rules inventory states the status of every rule; silence never reads as compliance |
| **Inferred findings laundering guesses** | A wrong "you need MCP X" costs a real process change | Class 3 last; distinct rendering; never ranked beside Observed findings |
| **Self-contamination** | Sessions analysing agent failure pollute string-derived signals | Exclude by `session.start.context.cwd` at ingest, not as a later filter |
| **Subagent rules are unrecoverable** | The digest cannot honestly say "this subagent broke your rule" | Inferred inheritance, labelled — or nothing |
| **Volume** | From the measured median, system prompts alone are 337 × 54 KB of near-duplicates | Content-hash de-duplication is a design decision, not a later optimisation |

---

## Open Questions

1. **How should a scoped check derive its scope?** The same problem in two domains, and now the
   central technical risk. Operand resolution needs A-minus-B subtraction and same-server scoping;
   content checks need the rule's own scope — a path pattern, an entity name — applied before
   matching. Measured: unscoped content checks were 100% false positives on 2 of 3 rules.
2. **Is a thin rule-adherence class still worth building first?** A measured 5.7% of normative
   bullets match a tool-choice shape. If the content-check family lifts that materially, class 1
   stands; if not, waste findings may deserve to lead.
3. **Are the surviving `fetch` hits real?** Untested — a measured 20 of them. The check cannot yet tell a violation from
   the preferred wrapper's own implementation.

### Closed during discovery

| Question | Resolution |
|---|---|
| Which findings were already known? | Some, not all; frequency unknown — retired the novelty gate |
| What fraction of rules are checkable? | A measured 10 of 43, a floor |
| Where does this live? | Its own repository — staged in `SessionPostMortem/` under Repo Rule 6 |
| A subagent's outcome | Verdict shown, labelled Inferred; cost and output load on demand |
| Rules that cannot be checked | Listed with an explicit *not checkable* status |
| Model recommendations | Out of scope |
| The `sessionStart` hook | A bug the operator owns; reported until it stops |
| Contradictory rules across repositories | Refuted — sequential in one repository, 0 sessions carry both |
| May rules be hard-coded? | No. Demonstrated dynamic in finding 2 |
| Will the operator use it? | Retired as a question. They commissioned it and are its only user; their answer is the evidence, not a proxy for it |
| How to resolve a rule's operands | Four layers, finding 8 — exact name, MCP server field, derived role, then unresolved |
| Do the shapes generalise beyond this corpus? | Yes, but thinly — a measured 5.7% of normative bullets, finding 10 |
| Can rules about code be checked offline? | Yes, from `apply_patch` / `edit` / `create` content — but only when scoped |

### The contradiction check, correctly scoped

The operator's proposal — alert when two rules contradict each other **inside the same rule set** —
is the right scoping, and is a decided requirement. Measured: 0 sessions carry both the `search_code`
prohibition and its recommendation.

It cannot be an Observed check. A keyword-polarity probe returned 4 candidates and **all 4 were
spurious**: three matched a single bullet against itself, because *"do not use it"* contains the
string *"use it"*; the fourth matched *"always pass an explicit model param — never omit it"*, one
sentence carrying both polarities by design. Requirements: pairwise comparison, self-matches
excluded, scoped to one rule-set version, shipped as **Inferred**, and placed in the "checks that
found nothing" surface.

---

## Self-review — what was checked and how

- Every figure comes from a single consolidated measurement run against `~/.copilot/` on 2026-08-16,
  re-run after review rather than carried forward from earlier drafts.
- **Two adversarial reviews were run**, one on this document and one on the mockups. Both were given
  the hypotheses *and* permission to reject them. Their material findings — an invalidated primary
  metric, an unpropagated reframing, a base rate presented as a violation rate — are incorporated
  above rather than appended.
- **One reviewer claim was rejected on evidence.** It argued the two hook denominators could not both
  be right because the numerator changed from 32 to 34. Re-measured: 34 of 35 sessions overall, 32 of
  33 among sessions that made a tool call, both measured — the two extra sessions made no tool calls
  and still failed the hook. Both figures stand; the reviewer's *fix* — state the denominator — was adopted.
- The adherence measurement was checked against two confounds: sessions lacking the rule (measured
  separately, 0 graph-tool calls), and circularity with the tool-failure finding (measured, rejected).
- The causal reading linking adherence to tool reliability is labelled a hypothesis wherever it
  appears, including in the mockups.
- **Corrected 2026-08-16, after approval: the event-line count is a measured 56 138, replacing a
  measured 56 176 in the two places this document carried it.** The older figure came from the
  ingestion data map's Part 1, which contradicts that document's own Part 3 census table; the
  corpus matches the table
  exactly, on a measured 31 of 31 event types with zero per-type deltas. The count is now frozen in
  `fixtures/corpus-manifest.json`, whose `--check` mode reproduces it, and every document in this
  set carries the corrected figure. Nothing else in this document depends on it: no finding, no
  adherence figure and no phase exit criterion is computed from the corpus line total.
- **Not validated:** that the operator will act on a digest. Mockups exist and have been reviewed;
  no behaviour has been observed.
- **Not measured:** whether any session in this corpus is itself an analysis session that would
  contaminate string-derived signals.
- **Not tested:** whether the five check shapes generalise to a repository outside this corpus, which
  is the assumption the "no hard-coding" requirement rests on.
