# Copilot Session Post-Mortem v1 — PRD

**Date:** 2026-08-16
**Status:** Draft — awaiting operator approval
**Format:** Amazon PR/FAQ (adapted for internal tooling)
**Product:** Standalone. Not part of AecoLedger, not part of AecoLedger Insights. No shared code,
no shared store, no shared contract. It lives in its own repository, `AecoPostMortem` (§3.1).
**Scope:** GitHub Copilot CLI only. Offline only. Behaviour only — never whether the work was correct.

**Evidence base** — every factual claim in this document traces to one of:

- `docs/product-superpowers/discovery/2026-08-16-copilot-session-postmortem.md` — the approved
  discovery, its interview and its corpus measurements. Cited as *discovery finding N* or
  *discovery §Section*.
- `docs/product-superpowers/discovery/2026-08-16-copilot-ingestion-data-map.md` — the field-level
  map of what Copilot writes to disk. Cited as *data map Part N*.
- `docs/product-superpowers/discovery/mockups/digest.html` and `…/flight-recorder.html` — the two
  reviewed surface mockups. Cited as *digest mockup* / *recorder mockup*. **Current for layout only.**
  The digest mockup's content-check evidence panel still renders discovery finding 11's measured
  194 / 61 / 33 against 0 / 0 / 20, which the FP measurement supersedes, and it shows none of the
  refusal behaviour in FR-37. Treat its layout as the reference and its figures as superseded.
- `docs/product-superpowers/discovery/2026-08-16-content-check-false-positives.md` — a measurement
  run commissioned against Part 8 Q1, over a measured 14 testable content rules. Cited as *FP
  measurement Part N*. It supersedes discovery finding 11 on every figure the two both state.
- `docs/product-superpowers/research/2026-08-16-scope-resolution-design.md` — the scope-resolution
  mechanism that answers Part 8 Q1, built over that measurement. Cited as *scope design Part N*.

Every corpus figure below was measured on the reference machine on 2026-08-16 against `~/.copilot/`:
a measured 35 sessions, 56,138 events, 16,085 tool calls, 3 repositories, 176.7 MB, spanning a
measured 111 days. Where a figure here is a goal rather than a finding it is labelled **target**;
where it is a judgment it is labelled **estimate**.

---

## Part 1: Press Release

### Session Post-Mortem tells you whether the rules you wrote for your coding agent were actually followed — and how often they weren't

**For an engineer who steers Copilot CLI with written rules.** Session Post-Mortem reads Copilot's
own event logs from disk and reports where each session diverged from the process you defined: which
of your rules were followed, which were ignored, where effort was wasted, and — the part we believe
nothing else does (**estimate**; no competitive scan has been run, Part 8 Q7) — whether last month's
rule edit moved the number. Nothing leaves the machine.

You already write `AGENTS.md` and `CLAUDE.md`, choose MCP servers, and tune all of it continuously.
Until now none of that has ever been checked. A rule is written once, injected into every session,
and never looked at again. When a session goes badly the evidence is a JSONL file too large to read,
so the response is to guess at a rule change and hope. Session Post-Mortem closes that loop.

**The problem today.** The rules and the behaviour are already in the same file, and nobody has ever
joined them. Copilot inlines your instruction files verbatim into every session's system prompt —
measured across 337 system messages, 335 of which contain the string `CLAUDE.md` (discovery finding
1). The tool calls sit beside them in the same `events.jsonl`. But reading it by hand means reading
a measured 176.7 MB, which has never once been done. So the answer to *"is my agent doing what I told
it?"* has stayed a feeling.

**What that costs.** Not tokens — convergence. The same drift recurs across sessions because nothing
closes the loop between "I wrote a rule" and "the agent obeyed it". The measured evidence that this
is real: one `sessionStart` hook has been failing in a measured 34 of 35 sessions, and nothing in the
CLI ever said so (discovery finding 7).

**How it works.** Session Post-Mortem ingests `~/.copilot/session-state/` into a durable local store,
extracts the rules each session was actually given from its `<custom_instruction>` blocks, and
matches them against a catalogue of parameterised **check shapes** — never against hard-coded rules,
because the shapes must fire on repositories this author has never seen. It then measures adherence
**per rule-set version**, with the tool-name resolution it used printed alongside, because a measured
fivefold spread on one rule came from that choice alone (discovery finding 3). Findings are ranked by
how many of your sessions each one touched, and every one of them carries its evidence, its
provenance — **Observed**, **Derived** or **Inferred**, rendered differently — and a suggested fix
you can refuse.

**And it works backwards.** Because the log records the rules *as they stood when the session ran*,
the before/after comparison runs against history already on disk rather than waiting for new
sessions. On the existing corpus, adherence across the 2026-05-23 rule edit measured **41.8% → 71.7%**
(discovery finding 4) — on a measured 3 and 4 sessions, so that demonstrates the method, not a
conclusion about that edit.

> *"I knew some of them but not all. I don't know how often those occur."*
> — the operator, discovery interview, 2026-08-16. Real, not illustrative: this product has one user
> and their own words are available, so a fictional quote would be strictly worse evidence.

**Getting started.** Run the ingest command; it finds `~/.copilot/` itself. Open the Process Digest.
Start at the top — that list is ordered by how many of your sessions each problem touched.

---

## Part 2: FAQ — Operator

**What exactly does it do?**
Three surfaces. The **Process Digest** is the front door: every recurring problem across every session
on the machine, ranked by sessions affected, each with a count, a recurrence strip showing which
sessions, the evidence, and a refusable suggestion. The **Session Flight Recorder** is the drill-in:
one session as a time-ordered tape of hooks, prompts, skills, tool calls, MCP calls and subagent
lanes, with an inspector showing the detail, the readable reasoning where it exists, and the raw
event. The **Rules Inventory** lists every rule statement extracted from your instruction files, each
with an in-force window and one of four statuses, so that an empty violation count can never be
mistaken for compliance.

**How is this different from AecoLedger Insights?**
Different question, different product, no shared code. Insights asks *where did the tokens go*. This
asks *where did my process fail*. They share no entities, no store and no UI — a deliberate decision
recorded in discovery §Problem Statement and enforced by the repository boundary (§3.1).

**Does it need my repositories?**
No, and it deliberately never reads them. It reads what the agent was actually given, which is
better: the log holds your instruction files as they stood *at the time the session ran*, already
merged and already overridden. That is what makes measuring a rule edit possible at all (discovery
finding 1).

**Are my rules hard-coded into this thing?**
No — this was a hard requirement from the interview, not an optimisation. The catalogue holds check
*shapes*, and their operands come out of your rule text. A measured 8 rules became checkable with no
tool name and no repository name written into the tool (discovery finding 2), and the shapes were then
tested against a genuinely independent corpus of a measured 60 `CLAUDE.md` files across 4 repositories
and did fire there (discovery finding 10).

**So what fraction of my rules can it actually check?**
A minority, and it says so on every screen. Three different figures circulate in the evidence base
and they answer three different questions, so this document uses one boundary throughout:
**checkable-today**, meaning a statement that matches a built check shape. Measured on this corpus
that is 4 of 43 (the Watched status in FR-40). The measured 8 in discovery finding 2 counts rules a
shape *could* match once built; the measured 10 in discovery §Key risks counts checkable-in-principle;
the mockup's 13 of 22 counts against real rules rather than all extracted statements. FR-40's four
statuses are the authoritative breakdown and every coverage figure derives from them.

**And a warning about the other corpus.** A measured 6 of 105 normative bullets in the *Claude Code*
corpus matched a tool-choice shape, which is where "most rules govern the code the agent writes" comes
from (discovery finding 10). That is true of those repositories and **not** of the ones this product
reads, where a measured 0 of 43 statements are content-shaped. Content checks are therefore gated out
of v1 (§3.4.3), and the Rules Inventory labels every unchecked rule rather than staying quiet.

**Why do I see a range of adherence numbers instead of one?**
Because one number would be a fabrication. Adherence depends on which tools a rule's words are taken
to name, and that choice moved a measured single rule on a single corpus from 57.4% to 24.4% to 10.5%
(discovery finding 3). It also depends on which version of your rule set was in force, and that
changed a measured 6 times in 32 days in your main repository, spanning a measured 41.8%–71.7%
(discovery finding 4). Every figure therefore ships with the resolution and the rule version that
produced it, and there is deliberately no corpus-wide percentage anywhere in the product.

**What if a rule names a tool my agent doesn't have?**
That is itself a finding, and it is more useful than any percentage. Measured on your navigation rule:
it says `Read`, and Copilot has no file-reading tool by that name — the one it uses is `view`, called
a measured 5,201 times. It says `Grep`, which matches `grep` at a measured 129 calls, while `rg` does
that job a measured 1,346 times and the rule never names it (discovery finding 9). The suggested fix
is a one-line rewrite in the agent's own vocabulary.

**Will it invent causes?**
That is the stated anxiety in discovery §Forces of Progress, and the design answers it three ways.
Every finding is labelled Observed, Derived or Inferred and rendered differently. Inferred findings
are never ranked alongside Observed ones. And a hypothesis is written as a hypothesis: the reading
that the mandated MCP's measured 29.7% failure rate makes your rule *wrong* rather than *disobeyed* is
labelled judgment everywhere it appears, because the rates are measured and the causal link is not
(discovery finding 5).

**If a check finds nothing, does that mean I'm compliant?**
Only if the product says the check ran. There is a dedicated "checks that found nothing" surface which
states the denominator for each — a measured 0 contradictions found across 35 sessions checked, a
measured 0 unresolvable subagent spawns out of 470, a measured 0 malformed lines out of 56,138. A
check that never ran appears in the Rules Inventory as *not checkable*, with the reason.

**Does it tell me whether the work was any good?**
No, by your own decision in the interview. Merged, reverted, tests passing, review outcome — all
excluded, because all of them need git and GitHub and the product is offline-only. Every finding here
is about *behaviour*, which is the thing a rule can change (discovery §Interview outcomes).

**Does anything leave my machine?**
No. There is no network call in v1. The logs contain your source code, your prompts and possibly
secrets; the store is a local file whose location, permissions and erasure are specified (FR-11).

**Will it slow down or interfere with my agent runs?**
No. It reads the logs after the fact, never writes to them, and is never in the execution path.

**How far back can it see?**
As far as Copilot still keeps. Measured: 111 days on this machine, and it is a rotating window —
older sessions are already gone. From the first ingest onward the product keeps its own copy, so the
horizon grows instead of sliding. This is the argument for shipping ingestion before anything visual.

**What does it cost me to be wrong about a suggestion?**
Nothing — every suggestion is refusable, and the product tracks how often you refuse. A rising
adherence curve alongside a rising rejection rate is the product's own failure signal, and it is a
tracked guardrail (§5.4), not a footnote.

---

## Part 3: FAQ — Engineering

### 3.1 Where the code lives

Everything, backend and frontend, is created at the root of this repository, `AecoPostMortem`, and
nothing references `AecoLedger.Core` or `AecoLedger.Insights.*`.

**This supersedes the containment rule as originally written**, and the change is a completion of it
rather than a relaxation. Earlier drafts confined the product to a `SessionPostMortem/` subtree
inside the AecoLedger repository so that `git subtree split` could one day lift it out. That lift has
happened: the product now owns its repository outright, so the subtree rule has nothing left to
protect and the directory level it required is gone. What the rule existed to guarantee is unchanged
and still binding — **no dependency on AecoLedger, in either direction** — and it is now enforced by
the repository boundary itself rather than by a path convention. The assembly prefix moves with it,
from `SessionPostMortem.*` to `AecoPostMortem.*`, so that one name identifies the repository, the
solution and every project in it.

```
AecoPostMortem/                            ← the repository root
  AecoPostMortem.sln
  src/
    AecoPostMortem.Data/                   ← the DbContext, the entity model and the EF Core
                                             migrations; the only project that owns the schema
    AecoPostMortem.Ingestion/              ← path discovery, event-line reader, RAW store,
                                             session/turn/agent reconstruction, self-exclusion
    AecoPostMortem.Rules/                  ← <custom_instruction> extraction, rule-set versioning,
                                             tool-vocabulary and role derivation, operand
                                             resolution, the check-shape catalogue
    AecoPostMortem.Findings/               ← the four finding classes, provenance, recurrence,
                                             the Monitor comparison, suggestions
    AecoPostMortem.Api/                    ← endpoints for the three surfaces
  test/
    AecoPostMortem.Data.Tests/
    AecoPostMortem.Ingestion.Tests/
    AecoPostMortem.Rules.Tests/
    AecoPostMortem.Findings.Tests/
    AecoPostMortem.Api.Tests/
  web/                                     ← the React project: React + TypeScript + Vite,
                                             the three surfaces; all frontend commands run here
  docs/                                    ← this document and its evidence base
  fixtures/                                ← the frozen corpus manifest (FR-55)
  scripts/                                 ← the document and corpus checkers
```

**Why `Rules` is its own project.** It is the "no hard-coding" boundary made structural. Nothing in
`AecoPostMortem.Rules` may name a tool, an MCP server or a repository. That is a compile-unit-level
invariant a reviewer can check by reading one project's source, rather than a convention that erodes.
**This invariant is unaffected by the move**: it was never about where the code sits, and it is the
one the operator called non-negotiable in the discovery interview.

**Tech.** .NET 10, C#, xUnit; React + TypeScript + Vite. Same stack as the rest of the machine's work
so the operator has one toolchain, but zero shared projects.

**Store.** One local SQLite file, no server, accessed through **EF Core**. Earlier drafts specified
no ORM, on the grounds that the workload is bulk append plus graph queries rather than an object
graph. That still describes the workload accurately; the decision it produced has been reversed
deliberately, because EF Core puts the schema, the migration path and the model in one place, and
that is worth more here than the overhead it costs. One carve-out, stated now so it is not
discovered under load: **the RAW append path bypasses change tracking**, using a batched raw-SQL
insert, because a measured 56,138 rows arrive in a single full ingest and per-entity tracking is the
wrong shape for that. Everything else — NORMALIZED, FINDINGS, and every query behind the three
surfaces — goes through the `DbContext`.

The statelessness rule that governed the ccusage-port projects and Insights belonged to the
AecoLedger repository and does not follow the product here; this product owns its store outright,
and §3.8's rebuildability requirement is what disciplines it instead.

### 3.2 The data source, and the three layers

Everything comes from `~/.copilot/session-state/<sid>/events.jsonl` — measured 35 files, 56,138 lines,
0 malformed (data map Part 1). `rewind-snapshots/index.json` is a secondary source for file-change
history. `session-store.db` is **excluded from v1** (FR-10).

| Layer | Contents | Property |
|---|---|---|
| **RAW** | the Copilot event verbatim, plus provider version, source file, byte offset, content hash | immutable; never discards unknown JSON |
| **NORMALIZED** | session, turn, tool call, agent, skill, hook, permission, **file change**, rule statement, rule-set version | the execution record and the rules beside it. *Write unit* belongs to this layer too but arrives with Phase E (FR-36), not v1 |
| **FINDINGS** | finding instances with class, provenance, evidence, recurrence, resolution used, suggestion, and the operator's accept/reject | fully re-derivable from RAW |

**Byte offsets are safe as identity.** Measured: on all 8 events carrying `eventsFileSizeBytes`, the
declared value equals the byte offset at which that event begins, delta 0 in every case (data map
self-review). A file rewritten rather than appended to could not hold that relationship.

**The parser hazard that must not be missed.** `tool.execution_start.data.arguments` is polymorphic:
an object for every tool measured except `apply_patch`, where it is a **string** carrying a patch
envelope — measured on 381 of 381 `apply_patch` calls (data map Part 5). A projection that assumes an
object silently drops every patch. **The justification is RAW fidelity, not any one consumer** —
§3.2 requires RAW to preserve the provider event verbatim and never discard unknown JSON, and a patch
parsed as an object is discarded content. That is why FR-4 stays a Phase A gate even though finding
class 3, its eventual consumer, is gated to Phase E.

**Subagent attribution is Observed, not heuristic.** Measured on the largest session: 115 distinct
`agentId` values on tool events, and a measured 115 of 115 are `subagent.started.toolCallId` values
from the same file (data map Part 3). Absence of `agentId` means "main thread", exactly, and a
measured 470 of 470 spawns resolve to their `task` call.

### 3.3 The four finding classes

Ordered by epistemic strength. That was also the build order until §3.4.3 gated class 3 out of v1;
it is now 2, 1, 4, with 3 waiting on input.

| # | Class | Provenance | What it rests on |
|---|---|---|---|
| 1 | **Rule adherence — tool choice** | Observed + Derived | rules extracted from `<custom_instruction>`, matched to check shapes, measured per rule version with the resolution stated |
| 2 | **Waste** | Derived | repeated reads, failed calls, hook failures, phase churn, aborts, permission interruptions |
| 3 | **Rule adherence — written content** | Derived | forbidden symbols and required wrappers checked against `apply_patch` / `edit` / `create` content, **scoped by the rule's own scope**. **Gated out of v1 — see §3.4.3** |
| 4 | **Missing capability** | Inferred | tool-failure clusters and hand-rolled sequences |

### 3.4 Three contradictions, resolved here

Recorded rather than silently fixed, per evidence discipline. The third **reverses the second**, on
evidence that arrived after it was written.

**1. The class count.** Discovery §Recommendation is headed *"Three finding classes"* and then lists
four. This PRD uses **four**, as enumerated in §3.3, and the prose heading is the stale half.

**2. Where class 3 lands.** The discovery's phasing table puts *"Finding class 3, rendered as
Inferred"* in Phase D, but its own risk table says written-content checks must ship *"alongside"*
class 1, *"not after it"* — and class 3 is Derived, not Inferred. Resolved: **written-content checks
ship in Phase C beside tool-choice adherence**, and **Phase D is the missing-capability class alone**,
which is the Inferred one. The phasing table's "class 3" is stale numbering from an earlier draft.

**3. Two measurements describe disjoint corpora, and the second one revokes resolution 2.**
Discovery finding 10 — *"most rules govern the code the agent writes, not the tools it calls"* — was
measured over **60 `CLAUDE.md` files across 4 repositories recovered from the Claude Code corpus**.
The FP measurement asked the same question of the corpus this product actually reads and found the
opposite: **a measured 0 content-shaped rules exist in the Copilot corpus** — of its measured 43
extracted statements, the measured 8 that are normative-negative all constrain tool choice or agent
behaviour, and none of the 43 is content-shaped (FP measurement Part 2). The other 35 are not
counter-examples; per FR-40's breakdown a measured 21 of them are not rules at all.

Both are true. They are different repositories. But resolution 2 moved the written-content class
*forward* into Phase C on the strength of discovery finding 10 — a figure from a corpus this product
cannot read — and that reasoning does not survive. Concretely: the measured 14 testable content rules were
recovered from `~/.claude/projects/*/*.jsonl`, Part 7 excludes any tool other than Copilot CLI, and
FR-26 forbids parsing a repository's markdown, so **nothing in v1 can reach them**.

Resolved: **the written-content class is gated out of v1** into Phase E, whose entry condition is
that at least one content-shaped rule appears in a Copilot repository's instruction files —
currently a measured 0 of 43. The scope-resolution work behind it (FR-50 … FR-54) is not discarded;
it is measured, designed, and waiting for input. What ships in v1 is what has input today.

### 3.5 Delivery phases

Four phases in v1, landing in order, each with its own exit criterion, plus one gated phase that is
**not** in v1.

| Phase | Contents | Exit criterion |
|---|---|---|
| **A — Read** | FR-1 … FR-14, **FR-55**, **FR-58** | Every session reconstructs; a re-run adds no duplicate events; RAW replays byte-identically; and the event census reproduces **the frozen fixture corpus's post-exclusion census (FR-55)** — not live `~/.copilot/` counts, which rotate and which FR-7 removes sessions from |
| **B — Waste** | FR-15 … FR-25, **FR-59** | The repeated-read and hook findings reproduce with **both** denominators stated, and the Flight Recorder renders a real session end to end |
| **C — Adherence + Monitor** | FR-26 … FR-35, FR-39 … FR-45, **FR-56**, **FR-57** | The per-version table in discovery finding 4 reproduces **from the product**, including the measured 41.8% → 71.7% edit; the digest and Rules Inventory render against the frozen fixture corpus (FR-55) |
| **D — Capability** | FR-46, FR-48, FR-49 | An Inferred finding is visibly distinguishable from an Observed one, and is not ranked in the same list |
| **E — Content rules** *(gated, not in v1)* | FR-36 … FR-38, FR-50 … FR-54 | **Entry condition:** at least one content-shaped rule appears in a Copilot repository's instruction files — a measured 0 of 43 today (§3.4.3). Exit: no content check runs unscoped **or** on an ambiguous scope, and the scope-resolution fixture (FR-54) passes |

**Phase A ships first regardless of schedule pressure.** The corpus spans a measured 111 days and
rotates; every week without ingestion is process history permanently lost, and the retroactive Monitor
capability depends on that history surviving.

**Releases.** The four v1 phases ship as three releases, **cut along dependency lines rather than
surface lines** — a release that ships a surface without the requirements that surface needs is not a
release. Two consequences worth stating, because both were got wrong once: FR-56, FR-57 and FR-59 are
prerequisites of the digest and therefore ship with it, and the digest's **rule-coverage bar and
per-rule-version trend do not ship in Release 1**, because FR-26, FR-27 and FR-40 are Release 2. In
Release 1 the digest ranks waste findings and states its own coverage as "rules not yet analysed".

| Release | Contains | Delivers |
|---|---|---|
| **1 — Prove the loop** | Phase A entire (FR-1 … FR-14, FR-55, FR-58); the waste findings FR-15 … FR-17; and the digest with everything it actually needs — FR-41, FR-42, FR-45, **FR-56, FR-57, FR-59** | **§5.1 for the waste class**, with a session count and evidence on every finding. **Not the per-rule-version trend**, which needs FR-27 and arrives in Release 2. Tests this document's largest unvalidated assumption — that the operator acts on a digest (Part 6) — before the expensive work is built |
| **2 — Close the loop** | The rest of Phase B, and Phase C's rule extraction, adherence and Monitor | **§5.2 in full**, and **§5.1 completed** — the rule-coverage bar and the per-rule-version trend both arrive here |
| **3 — Judgment** | Phase D | Inferred findings, visibly separated |

Phase E is not scheduled. It enters the plan when its entry condition is met, and not before.

### 3.6 Functional requirements

#### Phase A — Read

- **FR-1** Discover `~/.copilot/session-state/*/events.jsonl`. A missing directory is reported, never
  thrown. Classify sibling `session.db`, `rewind-snapshots/index.json` and `workspace.yaml` — classify
  only. **Per-session `session.db` is not ingested in v1**: it holds todo rows that discovery found
  sparse and success-biased, and FR-19 derives phase churn from `report_intent` instead. It is
  classified so the coverage report can say it was seen and skipped.
- **FR-2** Persist every line to RAW verbatim with identity `(source_file, byte_offset, content_hash)`.
  Unknown fields are preserved, never dropped.
- **FR-3** Read provider version from `session.start.data.copilotVersion` on line 1 — measured present
  on 35 of 35 sessions — and register parsers against `session.start.data.version`, a value measured
  as `1` on all 35 of a measured 35 sessions. Do not scan for a first-declared version.
- **FR-4** Parse `tool.execution_start.data.arguments` **polymorphically**: object normally, string for
  `apply_patch`. A build that cannot round-trip a measured 381 of 381 `apply_patch` envelopes fails
  Phase A.
- **FR-5** Ingestion is idempotent. Re-running over the same logs adds no events. Growth is by append
  only; a resumed session continues the same byte stream.
- **FR-6** Tolerate malformed lines per-line, recording a skipped-line count per file. Measured 0 of
  56,138 today; the tolerance is for tomorrow's CLI version, of which a measured 14 distinct ones
  already appear in this corpus. **Ingestion stops at the last newline-terminated line** and records
  that high-water offset: `events.jsonl` is live-written, so a trailing partial line is not malformed,
  it is unfinished. A skipped line is **retried on the next run**, never recorded as permanently bad.
  The measured 0 is a reading of closed files and is no evidence at all about the live case.
- **FR-7** **Exclude this product's own analysis sessions at ingest** — at ingest, not as a later
  filter, because sessions that analyse agent failure contaminate every string-derived signal. The
  key is `session.start.data.context.cwd`, and the comparand is an **operator-configured exclusion
  list**, defaulting to this product's own repository root. Two things this must get right: a path
  match alone would exclude ordinary feature work done *inside* this repository, which the operator
  wants measured, while missing an analysis session run from anywhere else — so the list is
  configurable and the ingest report names every session it excluded and why (FR-14). Whether any
  session in the reference corpus is such a session was **never measured**, so the first run of this
  requirement is also its first test.
- **FR-8** Reconstruct the execution record: `id` / `parentId` from the envelope, which is
  measured present on 100% of events; turn boundaries from `assistant.turn_start` / `turn_end`; tool
  calls from the `tool.execution_start` / `execution_complete` pair, measured 16,076 pairs.
- **FR-9** Reconstruct agents from `subagent.started`, `subagent.completed` and `subagent.failed`.
  `agentId` is the subagent handle; its absence means main thread. Nesting is derived from the
  `agentId` on the spawning `task` call, measured 178 of 470 nested.
- **FR-10** **Do not ingest `session-store.db` in v1.** It is live-written, WAL-dependent, covers a
  measured 7 of 40 sessions, and everything it uniquely offers is per-request latency and nano-AIU —
  none of which any finding class needs. Recorded as a decision so it is not rediscovered.
- **FR-11** Store at a single local SQLite file in a documented per-user location, created with
  owner-only permissions, with a documented purge command. No server, no account, no network call.
  The schema is created and advanced by **EF Core migrations applied automatically on first use**, so
  the operator never runs a database command and a store from an older build is usable without one.
- **FR-12** De-duplicate system-prompt text by content hash. Measured: 337 system messages, of a
  measured median 54,335 characters each, mostly near-duplicates; storing them naively stores the same
  prompt hundreds of times.
- **FR-13** Ingest `rewind-snapshots/index.json` under the one-file-one-event rule — **its consumer
  is the file-change history in §3.2's NORMALIZED layer, and nothing else in v1 reads it; if that
  stays true through Phase A, defer it.** Keep both
  versions when the file is rewritten in place — it is rewritten as the session grows, unlike an
  append-only stream, and this must be a stated rule rather than an inherited accident.
- **FR-14** Emit a coverage report per ingest run: sessions found, sessions ingested, sessions
  excluded and why, lines parsed, lines skipped, events by type.
- **FR-55** **Freeze a fixture corpus, and state every exit criterion against it.** Snapshot the
  reference sessions — their bytes or their hashes plus a checked-in census — at a stated date. Live
  `~/.copilot/` cannot serve as a gate: §3.5 argues Phase A must ship *because the window rotates*,
  and FR-7 removes sessions from the census by design, so a criterion pinned to live counts is either
  unachievable or vacuous and nobody can tell which. The frozen census is the **post-exclusion** one.
  **Where it lives, decided:** the session bytes are **not** checked in. They hold the operator's
  source code and possibly secrets (§3.8), and committing 176.7 MB of them would bloat a repository
  whose entire source tree is a fraction of that. What is checked in is a manifest — per session, the file's content hash, its size, and
  the post-exclusion event census — plus a small hand-picked set of redacted fixtures for parser
  tests. The full corpus stays on the machine, referenced by hash, so a mismatch is detectable without
  the bytes being in the repository.
- **FR-58** **The command surface**, enumerated here rather than deferred, because Part 1 tells the
  operator to "run the ingest command" and something has to say what that is:
  - `ingest` — optional path override, defaults to the resolved Copilot directory; writes FR-14's
    coverage report to stdout and exits non-zero if no source was found.
  - `purge` — deletes the store (FR-11); reports and exits zero when there is nothing to purge.
  - `serve` — starts the local API and web shell on a stated default port, and opens nothing by
    itself.

  The web shell is built as part of the solution and served by `serve` from the same process; there
  is no separate dev server in the shipped product and no build step the operator runs by hand.

#### Phase B — Waste

- **FR-15** **Repeated file reads.** Group `view.arguments.path` per session — measured present on
  5,201 of 5,201 `view` calls. Report paths read 4 or more times. Measured: 16 of 35 sessions qualify;
  worst single path a measured 74 reads; worst session a measured 1,367 such reads across 90 paths.
- **FR-16** **Failed tool calls,** from `tool.execution_complete.data.success` — a field whose
  presence is measured at 16,076 of 16,076 completions. Report per tool as failures / calls with the
  rate, never the rate alone.
- **FR-17** **Hook failures,** from `hook.end.data.success`. Measured 35 failures across a measured
  3,027 hook pairs. Both denominators are reported together and always: a measured 34 of 35 sessions
  overall, and a measured 32 of the 33 that made a tool call. A single figure whose population is
  unstated is what produced two surfaces contradicting each other during discovery.
- **FR-18** **Aborted turns,** from `abort.data.reason` — measured 9 across 8 sessions. State
  explicitly that no rollback event is recorded, so what was already changed on disk is unknown.
- **FR-19** **Phase churn,** from `report_intent` tool calls — measured 2,167 of them; worst session
  measured 104 returns across 352 intents. **"An earlier phase" requires a vocabulary and an
  ordering, and neither may be hard-coded:** derive the phase vocabulary from the corpus the way
  FR-29 derives the tool vocabulary, derive the ordering from the sequence in which phases are first
  declared across sessions, and **record both with the finding** the way FR-33 records a resolution.
  Without that, two implementations produce two different churn counts from one corpus and §3.8's
  determinism cannot tell them apart, because both are deterministic. A legitimate iteration is
  indistinguishable from drift on this data, so the finding is **Derived**, and says so.
- **FR-20** **Interruption load:** permission prompts from `permission.requested` / `completed` —
  measured 1,033 and 1,031 — and questions put to the operator from `ask_user`, a measured 124 asked
  against a measured 124 answered. `permission.completed.data.result.kind` is an enum, so denial is
  Observed rather than string-matched.
- **FR-21** **The Session Flight Recorder** (recorder mockup): a masthead carrying session identity,
  repository, branch, CLI version, elapsed time, turns, tool calls, subagents, skills, models and
  context size at end; a row of finding chips; a time-ordered tape with a lane per subagent; and an
  inspector with **Detail**, **Thinking** and **Raw** tabs.
- **FR-22** **Subagent output is reconstructed from the subagent's own message stream**, never from the
  parent's tool result. Measured on `read_agent` completions: 200 results, median 48 characters,
  ending in the literal marker `(Full response provided to agent)`. A reader following the parent sees
  a stub.
- **FR-23** **The Thinking lane states why it is empty when it is.** `reasoningText` is readable —
  measured on 1,252 messages — while `reasoningOpaque` is provider-encrypted, measured on 6,627. The
  split is model-dependent: measured 3.5% readable on `gpt-5.4` against a measured 88.2% on
  `claude-sonnet-4.5` (recorder mockup). Report the measured share for the session's own models rather
  than rendering an unexplained blank.
- **FR-24** Token figures shown are session-scoped and Observed: `session.shutdown.data.modelMetrics`,
  measured present on 31 of 35 sessions, and `assistant.message.data.outputTokens`, whose presence
  is measured at 8,232 of 8,261. **No currency, no pricing engine, no per-turn apportionment.**
  Copilot prices in premium requests and nano-AIU, and no local file states a conversion rate;
  apportioning a session total across turns is Inferred and this product does not do it.
- **FR-25** Skills are rendered as first-class steps from `skill.invoked` — measured 794 events, of
  which a measured 445 are scoped to a subagent — carrying name, plugin and version.
- **FR-59** **The API response envelope carries provenance.** Every finding served carries its
  provenance level, and every adherence figure its resolution and rule version, in the response
  contract itself rather than in the client. FR-33's refusal and §3.8's "provenance is structural"
  are claims about an API surface that must therefore be specified. **The provenance clause binds from
  Phase B; the adherence-resolution clause binds from Phase C**, when adherence figures first exist.
  FR-59 is the contract; **FR-33 is its Phase C rendering rule** and does not restate the refusal —
  where the two could be read as conflicting, FR-59 is authoritative.

#### Phase C — Adherence and Monitor

- **FR-26** **Extract rule statements** from `<custom_instruction>` blocks in `system.message`, tagged
  by their source file. Measured: 335 blocks headed `CLAUDE.md`, 89 `AGENTS.md`, 4 `Agent workflow`,
  1 `Copilot instructions`, present in a measured 32 of 35 sessions across 3 repositories, yielding a
  measured 43 distinct statements. **Never parse a repository's markdown files.**
  **Define the extraction unit:** a statement is one markdown list item, normalised by stripping the
  marker and trimming, and deduplicated verbatim across sessions. This is not academic — two
  extractors over the same 60 files measured 427 bullets and 550 bullets respectively (discovery
  finding 10 against FP measurement Part 2), differing only in their filters. Rule-set hashing
  (FR-27), status counts (FR-40) and the coverage bar (FR-41) are all downstream of this definition,
  and the measured 43 came from the list-item convention.
- **FR-27** **Version the rule set** by content hash of its block set, per repository. A version's
  window is the contiguous run of sessions carrying it. Measured on `supahfly27/UpFront`: 6 versions
  over a measured 32 days across a measured 25 sessions.
- **FR-28** **Every adherence figure is scoped to one rule-set version and one repository.** A figure
  that spans a rule edit must be impossible to compute, not merely discouraged.
- **FR-29** **Discover the tool vocabulary from the logs** — a measured 61 distinct tools — rather than
  from any table in the source.
- **FR-30** **Derive tool roles from argument shapes.** A tool taking `path` but no `pattern` reads
  files; one taking `pattern` searches; `old_str` or `file_text` writes. Measured roles and their
  dominant tools: `file-read` 3 tools, `view` at 5,201 calls; `search` 5 tools, `rg` measured at 1,346;
  `file-write` 2 tools, `edit` measured at 239; `shell` 1 tool, `powershell` measured at 3,504; and
  `spawn` 1 tool, `task` measured at 486.
- **FR-31** **Resolve a rule's operands in four layers, most confident first:** exact tool name; the
  logged `mcpServerName` field; the derived role; then **unresolved, reported as a finding and never
  dropped silently**. Layer 2 is a field match, not a string match — that is what excludes
  `github-mcp-server-search_code` from a rule about `codebase-memory-mcp`, measured 28 tools under this
  layer.
- **FR-32** **Operands are subtracted, A winning ties.** The known unfixed defect in discovery
  finding 8 is that the role layer pulled a tool into operand B that also belongs to operand A. Phase C
  does not exit with that defect present.
- **FR-33** **Every adherence figure renders with the resolution that produced it** — the layer used
  per operand and the resulting call counts — because a measured fivefold spread on one rule came from
  that choice alone. A figure rendered without its resolution is a defect, not a cosmetic issue.
- **FR-34** **The check-shape catalogue is parameterised and names no tool, MCP server or
  repository.** Those three, exactly, are what may not appear in `AecoPostMortem.Rules`. Argument
  *field* names — `path`, `pattern`, `old_str` — are how FR-30 derives roles from shapes and are
  permitted; the invariant is about the vocabulary of a particular machine's tools, not about the
  provider's event schema. The shapes measured to fire are
  `prefer-A-over-B` (3 rules), `never-read-path` (2), `tool-is-banned` (1), `use-A-after-B` (1),
  `always-pass-param` (1) — a measured 8 rules checkable with nothing hard-coded — plus
  `forbidden-symbol` for content (FR-37).
- **FR-35** **"This rule names a tool your agent does not have"** is a first-class check, run per rule
  regardless of adherence. Measured on the navigation rule: the rule says `Grep`, matching `grep` at a
  measured 129 calls, while the tool dominating that role is `rg` at a measured 1,346 and is never
  named. This is more actionable than any percentage, because its fix is one line.
- **FR-39** **The Monitor comparison.** Given two adjacent rule-set versions of the same repository,
  report adherence either side under one stated resolution, with the session count on each side. It
  must reproduce the measured 41.8% → 71.7% across the 2026-05-23 edit, and must render sample sizes —
  a measured 3 and 4 sessions — as prominently as the percentages.
- **FR-40** **The Rules Inventory** lists every extracted statement with its source file, its sessions,
  its in-force and retired dates, and exactly one status: **Watched**, **Checkable — not yet built**,
  **Not checkable** with a stated reason, or **Not a rule**. Measured on this corpus: 4 / 9 / 9 / 21
  (digest mockup's rule table, the only place this breakdown is enumerated).
  A retired rule stays visible with its adherence frozen at retirement.
- **FR-41** **The Process Digest** (digest mockup): corpus masthead, a rule-coverage bar, then findings
  ranked by sessions affected, each carrying a recurrence strip of which sessions, a provenance badge,
  the evidence, and a refusable suggestion.
- **FR-42** **The "checks that found nothing" surface** states each silent check with its denominator —
  a measured 0 contradictions across 35 sessions checked, a measured 0 unresolvable spawns of 470,
  and a measured 0 malformed lines of 56,138. Silence is never indistinguishable from compliance.
- **FR-43** **The contradiction check** is pairwise, excludes self-matches, is scoped to a single
  rule-set version, and ships as **Inferred** in the silent-checks surface. A keyword-polarity first
  pass returned a measured 4 candidates and all 4 were spurious — three matched a bullet against
  itself, because *"do not use it"* contains *"use it"*.
- **FR-44** **Conditional rules are reported as base rates and explicitly labelled as such.** The
  parallel-tool-calling rule measured a 43.6% single-call rate across 7,449 tool-issuing messages —
  3,249 of them — and whether a second independent call was available at each point was never measured.
  It is not a violation rate and must never be rendered as one.
- **FR-45** **Record the operator's response to every suggestion** — accepted, rejected, or ignored.
  This is not a nicety: it is the input to the §5.4 guardrail, which is the product's own failure
  detector.
- **FR-56** **Suggestions are deterministic templates bound to a check shape**, populated from the
  same operands and resolution the finding used. §3.8 forbids a model call, so they cannot be
  generated; nothing else in this document said how they arise, yet the press release, Part 4, FR-41
  and the whole of §5.4 depend on them. FR-35 is the worked example — *"rewrite the rule in your
  agent's own vocabulary: name `rg`"* — and it generalises. **A finding class with no template ships
  with its evidence and no suggestion**, never a generic one.
- **FR-57** **Define the recurrence key per finding class.** "Ranked by sessions affected" (FR-41)
  and §5.1's primary metric both require knowing what makes two occurrences in two sessions *the same
  finding*. State the key for each class — for a rule finding, the rule statement; for repeated reads,
  the path — and state how a finding whose rule spans several rule-set versions is counted, since
  FR-28 fragments identity across versions and FR-41 needs it stable across the corpus.
  **Decided, so it is not discovered per surface:** a finding's identity is `(class, class-specific
  key)` and is **version-independent** — the rule statement for a rule finding, the file path for a
  repeated read, the tool name for a failure cluster, the hook identity for a hook failure. The
  per-version breakdown is carried as an **attribute of that one finding**, not as several findings.
  That satisfies both constraints at once: ranking is stable across the corpus because identity does
  not fragment, and no *figure* spans a rule edit because every figure inside the finding is
  per-version.

#### Phase D — Missing capability (Inferred)

- **FR-46** **Tool-failure clusters.** **Match tool names exactly, and state the convention on the
  table** — FR-33's principle applies here too. Re-measurement moved one row of the figures below by a
  measured 11 calls purely by matching substring instead of exact name (FR-48), so a failure-rate
  table without its convention is the same defect as an adherence figure without its resolution. The
  figures below are exact-match, and the rows other than `search_code` were measured before that
  convention was fixed and are **re-measured before they are shipped**.
  Measured: `web_fetch` failed 112 of 183 calls, 61.2%.
  Measured: `codebase-memory-mcp-search_graph` 11 of 37, 29.7%.
  Measured: `codebase-memory-mcp-search_code` 15 of 53, 28.3%.
  Measured, for contrast: `rg` 16 of 1,346, 1.2%.
- **FR-48** **Inferred findings render visibly differently and are never ranked in the same list as
  Observed ones.** The hypothesis linking the mandated MCP's failure rate to adherence is carried as a
  hypothesis, and its independence was checked — then **re-measured 2026-08-16, because discovery
  finding 5's two figures could not both describe one population.** Both were right about different
  populations: the failure-rate table matched the tool name exactly, measured at 53 calls and 15
  failures, while the circularity split matched the substring `search_code`, measured at 64 calls and
  16 failures — the difference being a measured 9 calls to a *different MCP server's* tool and 2 to a
  variant name. **The discovery's own circularity check committed the failure FR-31 layer 2 exists to
  prevent.** Rescoped to exact matching the conclusion strengthens: measured inside the sessions
  carrying the ban, 15 calls produced 1 failure; measured outside them, 38 calls produced 14. The two
  findings are not circular.
- **FR-49** **Subagent rule attribution is inheritance, and is labelled Inferred or omitted.**
  `system.message` carries no `agentId`, so a subagent's own system prompt is never recorded. What can
  be shown Observed instead: `subagent.started.agentDescription`, the `task.arguments.prompt`, and the
  `skill.invoked` events carrying that `agentId`.

#### Phase E — Content rules *(gated, not in v1)*

**Entry condition:** at least one content-shaped rule appears in a Copilot repository's
instruction files. Measured today: 0 of 43 (§3.4.3). Until then none of the following is built,
and the measurement and design behind them stand ready rather than discarded.

- **FR-36** **Extract write units** from `apply_patch`, `edit` and `create`, **separating content the
  agent added from pre-existing content the write carried along** — `+` lines against context and `-`
  lines for a patch, `new_str` against `old_str` for an edit, all of `file_text` for a create.
  Measured: 673 write operations across a measured 20 sessions; a measured 842 write units once patch
  envelopes are split per file; a measured 380 distinct files; and a measured 842 of 842 units
  carrying a usable file path. Content checks read **added content only** — measured to remove 21.0%
  of hits on its own (FP measurement Part 3).
- **FR-37** **Content checks run only when scoped**, from the rule's own scope — a path pattern, an
  entity name, a directory — and run behind two scope-independent filters, measured to remove a
  cumulative 61.8% of hits before any scope is applied (FP measurement Part 3):
  - **Documentation files are excluded.** Measured to remove 51.3% of what survives FR-36 — the
    single largest source of false positives, because a rule quoted in a spec file matches itself.
  - **Lines carrying an external URL are excluded.** Measured at only 0.7%, kept because it is free
    and both of its measured hits were genuine false positives.

  Two refusals, not warnings. **An unscoped content check must be refused.** And **a check whose
  scope is ambiguous must also be refused** — where two plausible mechanisms disagree, report the
  disagreement instead of picking one. Measured on one rule: path-scoping returns 9 hits and
  entity-scoping returns 0, and all 9 are false positives (FP measurement Part 5).
- **FR-38** Content-check hits are labelled **Derived and unconfirmed**, and the known
  false-positive causes are named on the finding rather than left for the operator to rediscover:
  - **The wrapper's own implementation.** A rule preferring a wrapper cannot forbid the wrapper's own
    guts. Measured: 2 of the 3 surviving `fetch()` hits are the auth plumbing `apiFetch()` is built
    on (FP measurement Part 5). A definition-site test is the intended detector.
  - **Sibling entities.** A rule naming one entity does not govern its siblings. Measured: all 9
    path-scoped `UpdateAsync` hits were on three sibling entities the rule never names.
  - **Untouched code is invisible.** `edit` captures only the replaced fragment, so a rule broken
    outside what the agent edited cannot be seen by this method at all.

##### Scope resolution — the answer to Part 8 Q1

Deliberately the same ladder as FR-31's operand resolution, because it is the same problem in a
second domain. Full derivation and evidence: *scope design*.

- **FR-50** **Filters run before scope resolution, never after.** Ordering is load-bearing, not
  stylistic: measured on the controllers rule, four scope mechanisms disagree 29 against 0 on raw
  content and **agree at 0** once FR-37's documentation filter has run — a measured 29 of 29 outlier
  hits were markdown design documents carrying example code (scope design Part 2). A design that
  tests for agreement first chases artifacts.
- **FR-51** **Parse each rule statement into subject, alternative and banned symbol** by position:
  the subject leads the statement, the alternative follows *use* / *only* / *prefer* / *instead of*,
  the banned symbol follows *never* / *not* / *avoid* / *must not*. A statement with no subject
  symbol has **no scope**, and must reach FR-53 rather than borrow the alternative as one — the
  measured failure case is *"Use `apiFetch()` … never raw `fetch()`"*, where treating `apiFetch()` as
  a scope is wrong and reporting the rule as not checkable is right. **Known limit:** the parse was
  tested only against rules written as list items; whether it survives rules written as prose is
  untested (scope design Part 7).
- **FR-52** **Resolve scope in four layers, most confident first:** the subject symbol, then a
  category word resolved against **naming conventions derived from this corpus**, then a literal path
  fragment, then unresolved. Layer 2 derives conventions the way FR-30 derives tool roles — from what
  the corpus contains, never from a table naming a directory. **Layer 1's co-occurrence window is an
  explicit, recorded parameter** — same line, same declaration, or same file — defaulting to same
  declaration; the scope design leaves it unspecified and this corpus does not discriminate between
  the three, so it must be a stated choice rather than an accident of implementation. **Precedence is load-bearing:** the
  measured 9 false `UpdateAsync` hits came from consulting path-scoping for a rule whose subject
  symbol had already answered the question more precisely.
- **FR-53** **An unresolvable scope produces a *not checkable* rule**, listed in the Rules Inventory
  with its reason, never a check that runs anyway.
- **FR-54** **A labelled fixture is the exit criterion, not an argument.** Run the measured 14
  testable rules through FR-50 … FR-52, have the operator adjudicate every surviving hit once, and
  freeze the verdicts as a regression fixture every later resolver change runs against. A seed of
  a measured 11 already-adjudicated hits exists in the FP measurement — 6 sibling-entity, 2
  wrapper-implementation, 2 external-API, 1 unresolved. **The agreement test's firing
  rate is tracked as a health signal** — a backstop that fires constantly means the resolver above it
  is broken. **State the limitation in the fixture itself:** the ground truth is labelled once, by the
  operator, who also wrote the rules being checked and chose the resolver design, with no second
  adjudicator and no blind labelling. Verdicts therefore carry a confidence and a one-line reason, so
  a later re-read can find the shaky ones. This is the one place where "one user" stops being a market
  fact and becomes a measurement limit.


### 3.7 Scale

| Property | Measured today | Design target |
|---|---|---|
| Sessions | 35 | 500 (target) |
| Events | 56,138 | 1,000,000 (target) |
| `events.jsonl` bytes | 176.7 MB | 2 GB (target) |
| Repositories | 3 | 20 (target) |
| Rule statements | 43 | 500 (target) |

The store accumulates beyond the source's rotating window, so it grows monotonically. Full ingest of
the measured 176.7 MB corpus in under 3 minutes and an incremental re-ingest in under 15 seconds are
**targets**, not measurements — and **FR-55's fixture is what they are measured against**, so a miss
is visible rather than absorbed. The design-target column is not tested in v1; that is recorded in
Part 8 rather than left to be discovered.

### 3.8 Non-functional requirements

- **Offline.** No network call in v1. Not "no telemetry" — no socket.
- **Read-only against the source.** The product never writes to `~/.copilot/`.
- **Rebuildable.** NORMALIZED and FINDINGS rebuild from RAW alone. Deleting them and re-deriving must
  produce identical output. **The store carries a schema version**, and a version change triggers
  re-derivation rather than migration — the store outlives the source's rotating window, so it will
  outlive several derived schemas, and rebuildability is the answer to that rather than a migration
  path per change. RAW itself is never rewritten. **EF Core does not soften this — it splits it, and
  the split is the rule.** RAW is the only layer that gets a migration: it holds provider events that
  no longer exist at the source once the window rotates, so its schema moves forward under EF Core
  migrations and its rows are never rewritten. The derived layers get none. A schema change there
  drops and re-derives them, which is cheap precisely because RAW survived. **A migration authored
  against NORMALIZED or FINDINGS is a defect, not a shortcut** — it would preserve a derived row the
  product is required to be able to reproduce from scratch, and hide a rebuild bug behind it.
- **Provenance is structural.** Every finding record carries its level; the API cannot serve a finding
  without one; the UI renders the three levels distinguishably.
- **Determinism.** The same store produces the same findings, in the same order. No sampling, no
  model call, and **no wall-clock dependency** — all temporal ordering derives from event timestamps,
  which is what FR-27's in-force windows and FR-40's retirement dates are built from.
- **Secret hygiene.** The store holds prompt and patch text. Owner-only file permissions, a documented
  location, a purge command, and no export path in v1.
- **Single user, local.** No auth, no multi-tenancy, no account.

### 3.9 Common failure modes

| Failure | Consequence | Guard |
|---|---|---|
| `arguments` parsed as an object always | every `apply_patch` silently dropped, so RAW is not verbatim and is no longer replayable | FR-4, with a round-trip test over the measured 381 calls |
| An adherence figure rendered without its resolution | an unfalsifiable number that moved a measured fivefold from that choice alone | FR-33; the API refuses to serve one |
| An unscoped content check | measured 100% false positives on 2 of 3 real rules under the FP measurement's own convention | FR-37; refuse, don't warn |
| A scoped check whose scope mechanism was picked arbitrarily | measured 9 hits against 0 on one rule, all 9 false | FR-37; refuse on ambiguity too |
| A content check run over pre-existing content | a measured 21.0% of hits are code the agent never wrote | FR-36; added content only |
| A content check run over documentation | a measured 51.3% of surviving hits, including rules matching their own text | FR-37 |
| A figure spanning a rule edit | describes a version that never existed; the measured range is 41.8%–71.7% | FR-28 |
| A base rate rendered as a violation rate | the measured 43.6% single-call rate presented as disobedience | FR-44 |
| Silence read as compliance | a rule nobody checks looks like a rule nobody breaks | FR-40, FR-42 |
| Analysis sessions ingested | this product's own sessions contaminate every string-derived signal | FR-7, at ingest |
| A subagent's report read from the parent | a measured median of 48 characters instead of the real output | FR-22 |
| An Inferred finding ranked beside an Observed one | a guess laundered into a process change | FR-48 |

---

## Part 4: The three surfaces

Both mockups are current as of this commit and are the reference for layout; this section states what
is load-bearing about them rather than restating them.

**Process Digest** — the front door, because the operator's second job is *"which problems actually
recur"*. Ordering is by sessions affected, never by severity, and never by recency. Every row carries
the count, a recurrence strip, a provenance badge, expandable evidence quoting the actual event
fields, and a suggestion. Below the ranked list sit the "checks that found nothing" surface and the
Rules Inventory. The digest's own mockup notes that its corpus scope, adherence figures, rule-version
table and all 43 rule statements are measured, while a handful of per-session counts are placeholders
— the shipped product has no placeholders.

**Session Flight Recorder** — the drill-in. A masthead of identity and totals, finding chips, then a
time-ordered tape with a lane per subagent and an inspector carrying Detail / Thinking / Raw. The Raw
tab is not a debugging affordance; it is the provenance guarantee made clickable — every claim on
screen is one click from the event that produced it.

**Rules Inventory** — the honesty surface. Four statuses, in-force windows, retired rules frozen. It
exists because a measured 10 of 43 statements are checkable, and a product that showed only the
checkable ones would be lying by omission.

**One simplification in the mockup that the product must not ship.** The digest mockup's rule table is
the union of every rule ever seen across all versions, and says so. The shipped Rules Inventory scopes
to a rule-set version (FR-28), because a measured 34 of 43 statements are absent from the most recent
session.

---

## Part 5: Success Metrics

Each metric is scored once, at the end of the release that delivers it — §5.1's waste half at
Release 1 and its per-version half at Release 2, §5.2 and §5.3 at Release 2. There is no per-cycle
scoring, because a cycle here is defined by operator activity and therefore cannot expire.

### 5.1 Primary — recurrence is quantified

| Metric | Baseline | Target | Measurement |
|---|---|---|---|
| Named problems carrying a session count | 0 of the problems in the discovery had a known frequency before it was written | every finding class carries a session count and a per-rule-version trend | the product's own digest, read against the discovery's finding list |

**Scored on the outcome, not the build.** Shipping FR-41 satisfies the target's letter while
teaching the operator nothing, so the metric carries a scoring step: after the first digest, the
operator lists the problems they could already name, and records for each whether the frequency the
product reported **changed what they did next**. That is n=1 and subjective; it is still about the
outcome rather than about whether a column rendered.

The operator's framing is the metric: *"I knew some of them but not all. I don't know how often those
occur."* Value is frequency on suspected problems, not novelty. The earlier "≥3 unanticipated
findings" gate is **retired** — novelty is the wrong axis (discovery §Opportunity Assessment Q4).

### 5.2 Secondary — a rule edit becomes measurable

| Metric | Baseline | Target | Measurement |
|---|---|---|---|
| Rule edits with a before/after adherence pair | 0 — no such comparison has ever been produced | 1 rule edit measured either side, scoped to the two rule versions | the Monitor comparison (FR-39) reproducing the measured 41.8% → 71.7% across the 2026-05-23 edit |

This is demonstrable on existing history rather than dependent on future sessions, which is why it
lands in Phase C rather than last.

### 5.3 Coverage, stated not maximised

| Metric | Baseline | Target | Measurement |
|---|---|---|---|
| Extracted rule statements carrying an explicit status | measured 43 statements, none with a status today | 100% carry exactly one of the four statuses | the Rules Inventory (FR-40) |

Deliberately not a target: the *share* of rules that are checkable. Measured at 10 of 43 here, and at
a measured 6 of 105 normative bullets on the second corpus. Driving that number up would reward
inventing shapes that fire rather than shapes that are right.

### 5.4 Guardrail — the product's own failure detector

Adherence rising is not evidence the findings were right. Two tracked figures — each with a minimum
sample below which it is **not read at all**, because at one user and suggestions in the low tens the
statistic is noise before it is signal:

| Guardrail | Reading | Source |
|---|---|---|
| Share of suggestions the operator rejects | rising rejection **alongside** rising adherence means the tool is obeyed where it does not matter and ignored where it does | FR-45 |
| Share of acted-on findings that were Inferred rather than Observed | a rising share means guesses are being laundered into process changes — the stated anxiety in discovery §Forces of Progress | FR-45 plus the finding's provenance level |

**Minimum sample (target): 20 adjudicated suggestions** before either figure is read. **Threshold:** rejection
above half, sustained across two digests. **Action when it trips:** the check shapes behind the
rejected suggestions are suspended, not tuned — a shape whose suggestions are refused most of the
time is a shape that should stop firing (§5.3's reasoning, applied to the product's own output).
Both the sample floor and the threshold are **targets chosen here, not measurements**.

### 5.5 Counter metrics — things that could get worse

| Counter metric | Why it might move | Threshold |
|---|---|---|
| A finding the operator cannot trace to an event | the product's whole claim is that every figure is one click from its source; a finding whose evidence does not resolve is worse than no finding | zero tolerated in any release |
| Findings whose recurrence count changes between runs on an unchanged store | FR-57's recurrence key is a decision, and a wrong key shows up here first — as a ranking that moves when nothing moved | zero tolerated; §3.8's determinism makes it detectable |
| *(Phase E only)* False-positive content-check hits | scoping is the central unsolved risk; unscoped it measured 100% false positives on 2 of 3 rules | any confirmed false positive on a scoped check blocks Phase E exit |
| *(Phase E only)* Per-rule filter and scope shrinkage | a computable proxy needing no adjudication: a rule losing most hits to the documentation filter was matching prose | measured median 63% per-rule reduction from filters alone (FP measurement Part 3) |
| Adherence figures rendered without a resolution | the fastest way to make the product unfalsifiable | zero tolerated; the API refuses to serve one (FR-33) |
| Time from "session ended" to "post-mortem read" | a tool nobody opens has no guardrail at all | not targeted in v1; recorded as unvalidated (§Appendix) |

### 5.6 Explicitly not a metric

**A single corpus-wide adherence percentage.** Discovery findings 3 and 4 together show that no such
number is well-defined: it moved a measured fivefold by resolution choice, and across rule versions it
spans a measured 41.8%–71.7%. It must not appear in the product, in a report, or in a metric.

---

## Part 6: Assumptions, Constraints, Dependencies

### Assumptions

| Assumption | If wrong |
|---|---|
| Copilot keeps writing `events.jsonl` in this shape | parsers break; a measured 14 CLI versions already appear in this corpus, so FR-3's version registration is the mitigation, not an optimisation |
| The operator keeps writing rules in markdown that Copilot inlines | finding classes 1 and 3 have no input; class 2 still works, which is why it is Phase B. Measured: 32 of 35 sessions carry blocks, so **the product must render a coherent empty state for a repository with no rules** — discovery §Opportunity Q2 names that user as getting nothing from class 1 |
| `<custom_instruction>` blocks stay verbatim rather than becoming summarised | the whole rule-extraction premise fails; measured verbatim across 337 system messages today |
| The operator will act on a digest | **unvalidated.** Mockups were reviewed; no behaviour has been observed |
| Byte offsets remain stable identity | measured true today, delta 0 on all 8 events carrying `eventsFileSizeBytes`; the content hash in FR-2 is the second line of defence |

### Constraints

- **Offline.** No network call, no auth, no remote service. From the interview, not a preference.
- **Nothing hard-coded.** No tool, MCP server or repository name in the checking layer.
- **Containment.** Its own repository, `AecoPostMortem`; no reference to any AecoLedger project, in
  either direction; no shared type, store or contract (§3.1).
- **Behaviour only.** No git, no GitHub, no notion of whether the work was correct.
- **No currency.** Copilot prices in premium requests and nano-AIU; no local file states a conversion
  rate, so no dollar figure appears anywhere.
- **One machine, one corpus.** Everything measured is an observation about this corpus across a
  measured 14 CLI versions — never a format guarantee.

### Dependencies

- `~/.copilot/session-state/` existing and readable. No other external dependency.
- **No dependency on AecoLedger, Insights, or any shared type.** Duplicating a type is the correct
  call here: a shared type would reintroduce the coupling the separate repository exists to prevent,
  and the two products answer different questions over different entities (§3.1, Part 2).
- The mockups as the layout reference for FR-21 and FR-41 — layout only, per the evidence base.
- **The operator's own time**, on the critical path twice: the §5.1 scoring conversation after the
  first digest, and — if Phase E is ever entered — FR-54's adjudication sitting, which gates that
  phase's exit. No one else can supply either.

---

## Part 7: Out of Scope

| Not in scope | Why |
|---|---|
| Whether the work was *correct* — merged, reverted, tests passing | Excluded by the operator in the interview; needs git and GitHub |
| Any network call, auth, or remote service | Logs only, fully offline |
| Model recommendations | Reasoning readability is model-dependent (FR-23) but advising a model change is out |
| A dollar cost, or any pricing engine | No local nano-AIU conversion rate exists |
| Per-turn token apportionment | Session totals are Observed; splitting them across turns is Inferred |
| Fixing the operator's `sessionStart` hook | Their bug; the product reports it until it stops |
| Per-subagent rule attribution as an Observed fact | `system.message` carries no `agentId` (FR-49) |
| `session-store.db` ingestion | Measured 7 of 40 sessions, WAL-dependent, offers nothing a finding needs (FR-10) |
| Rules that cannot be mechanically checked | Listed and labelled, never silently skipped, never guessed at |
| Hand-rolled tool sequences as a finding | Cut from v1. It was the only requirement in the document with no measured backing — no definition of "repeated", no minimum length, no threshold, no candidate pattern measured on the corpus. Phase D stands on tool-failure clusters alone until one is measured |
| Rules about the code the agent writes | Gated out of v1 into Phase E, §3.4.3. A measured 0 of 43 statements in the Copilot corpus are content-shaped, so the class has no input here yet |
| Any tool other than Copilot CLI | Claude Code and Codex are not in v1 and are not planned here |
| Any market beyond this operator | Unassessed by choice |
| Sharing code, storage or UI with AecoLedger | Standalone product in its own repository (§3.1) |
| Editing the operator's markdown files | The product reports and suggests; it never writes a rule |

---

## Part 8: Open Questions

Carried from discovery, plus what this PRD adds. None blocks Phase A.

1. **Closed as a question, converted to requirements — FR-50 … FR-54.** Q1 asked how a check derives
   its scope. The answer is *scope design*: filter first (measured to make four disagreeing
   mechanisms agree at 0), parse the statement into subject / alternative / banned, resolve in four
   layers with precedence, and validate against a labelled fixture rather than an argument.
   - **What remains open, and is now narrow enough to measure:** how often layer 2 derives the wrong
     naming convention for a category word. "Controllers" resolves cleanly in this corpus;
     "services", "handlers" or "models" may correlate with several conventions or none. FR-54's
     fixture measures this directly and nothing else will.
2. **Closed — the surviving `fetch` hits are mostly not real.** Measured: of the 3 that survive
   scoping and filtering, 2 are the auth plumbing `apiFetch()` is built on and 1 is unresolvable
   without reading the repository (FP measurement Part 5). The wrapper's-own-implementation cause is
   now named on the finding itself (FR-38); the residual question is only whether a definition-site
   test can detect it mechanically.
3. **Closed by §3.4.3 — tool-choice adherence is not "thin", it is all there is.** The measured 6 of
   105 figure came from the Claude Code corpus. In the corpus this product reads, a measured 43 of 43
   statements, none is content-shaped and every normative-negative one constrains tool choice or agent
   behaviour, so the class the discovery worried was too thin to lead with is the only class with input.
4. **How is a rule-set version identified when a repository's blocks arrive in a different order?**
   FR-27 hashes the block set; whether ordering is stable across sessions was not measured.
5. **Digest scope — decided, with the seam kept.** **Default: one repository**, selectable, because
   ranking by sessions affected across repositories mixes rule sets that were never in force together
   (FR-28's reasoning applied to the surface). The measured corpus holds 3 repositories with one
   dominant at a measured 25 of 35 sessions, so a cross-repository view is a later option, not the
   default. Recorded here rather than left open, because FR-41 cannot be built without an answer and
   leaving it open means whoever builds it decides by accident.
6. **The §3.7 design targets are untested in v1.** FR-55's fixture is the reference corpus — a
   measured 35 sessions and 176.7 MB. The design targets of 500 sessions and 2 GB have no acceptance
   mechanism, and the store grows monotonically past the source's rotating window, so this is the
   property most likely to bite late rather than early. Recorded rather than quietly targeted.
7. **No competitive scan has been run.** Discovery §Opportunity Assessment Q5 labels the claim that no
   existing product joins rule text to tool calls an **estimate** based on the category's shape.
   Running `competitive-analysis` would settle it; it is not a blocker for a single-user offline tool.

---

## Appendix — Self-Review

What was checked in writing this document, and how.

- **Every figure traces to a named source.** Corpus figures come from the two discovery documents,
  both of which record measurements taken on 2026-08-16 against `~/.copilot/`. No figure was carried
  from memory or from an earlier draft. `python scripts/check-claims.py` was run against this file.
- **Two contradictions in the approved discovery were found and resolved in §3.4** rather than
  silently smoothed: the "three finding classes" heading over a four-item list, and a phasing table
  that places the written-content class in Phase D while the risk table requires it beside class 1.
  Both resolutions are stated with their reasoning so a reader of the discovery can see the delta.
- **The mockups were read, not assumed.** FR-21, FR-23 and FR-41 come from the mockups' own contents
  and mockup notes, including the model-dependent reasoning-readability split, which appears nowhere
  in the discovery document.
- **Requirements were checked against the "could someone build this cold?" gate,** not against
  internal consistency. Consequences of that pass: FR-4 (polymorphic `arguments`) and FR-13
  (`rewind-snapshots` rewritten in place) are stated as requirements rather than left as data-map
  trivia, because both silently destroy data if missed.
- **Where the discovery flags an unfixed defect, this PRD carries it as an exit criterion** rather than
  as prose — the operand-subtraction defect from discovery finding 8 is FR-32 and blocks Phase C exit.
- **Not validated:** that the operator will act on a digest. Mockups exist and have been reviewed; no
  behaviour has been observed. §5.5 records the "time to read" counter metric as untargeted for this
  reason rather than inventing a threshold for it.
- **Not measured:** whether any session in this corpus is itself an analysis session that would
  contaminate string-derived signals. FR-7 exists because it will be true from the first run of this
  product onward regardless.
- **Not tested:** whether the five check shapes generalise to a repository outside the two corpora
  already tried. The "no hard-coding" requirement rests on that assumption, which is why FR-34 makes it
  a structural property of a project rather than a coding convention.
- **Amended 2026-08-16 after a commissioned measurement.** Part 8 Q1 was reframed and Q2 closed on
  evidence from `docs/product-superpowers/discovery/2026-08-16-content-check-false-positives.md`,
  which measured 14 testable content rules against the write corpus. Three predictions made before
  that run were wrong and are recorded there rather than quietly dropped: the testable sample was
  predicted to stay in single digits and measured 14; "the agent didn't write it" was expected to be
  the dominant false-positive cause and measured second at 21.0% behind documentation files at 51.3%;
  and the Copilot corpus was assumed to carry content rules, where a measured 0 of its 43 statements
  do.
- **Corrected 2026-08-16 by the frozen fixture: the event-line count is a measured 56,138, replacing
  a measured 56,176 at every occurrence in this document.** The older figure comes from data map
  Part 1 and contradicts that same document's Part 3 census table, which sums to a measured 56,138.
  The live corpus matches that table exactly — a measured 31 of 31 event types, zero per-type
  deltas — verified by extracting the table from the document mechanically rather than by
  transcription, and the 2026-08-13 discovery independently measured the same total. Neither run
  behind the older figure recorded how it was derived, which is the defect FR-33 exists to prevent,
  committed one document upstream. The authority is now `fixtures/corpus-manifest.json`, whose
  `--check` mode reproduces the measurement on demand, and `fixtures/README.md` records it in full.
  The data map and the approved discovery were amended the same day, each logging the correction in
  its own self-review rather than applying it silently, so no document in the set now carries the
  older figure except where it is quoted as superseded.
- **A defect this PRD's own evidence base commits.** Discovery finding 11's measured 194 / 61 / 33
  could not be reproduced, because neither run recorded the regex it matched with — the exact failure
  FR-33 exists to prevent, one document upstream. Figures sourced from the FP measurement are
  internally consistent within a stated convention and were checked under a second one.
- **Revised 2026-08-16 after two independent adversarial reviews**, one of this document and one of
  the story breakdown under it. The reviews were given the hypotheses and explicit permission to
  reject them. Three of their findings changed the plan rather than the prose: the written-content
  class had **no input in the corpus this product reads** and is gated out of v1 (§3.4.3); **nothing
  generated the suggestions** the press release, the digest and the whole of §5.4 depend on (FR-56);
  and **finding identity across sessions was undefined** while "ranked by sessions affected" was the
  front door and the primary metric (FR-57).
- **One reviewer claim was rejected on measurement.** It argued that discovery finding 1 and data map
  Part 11 reporting identical figures for two different quantities implied one had mislabelled what
  its script counted. Re-measured 2026-08-16: blocks *headed* `CLAUDE.md` measured 335 and system
  messages *containing* the string also measured 335; `AGENTS.md` measured 89 both ways. They
  coincide legitimately, and **structurally rather than by luck**: each system message carries at
  most one block per source file, so "blocks headed `CLAUDE.md`" and "messages containing one" are the
  same count by construction, and they could only diverge if some message mentioned the string outside
  a block header. None does. Both documents stand.
- **One reviewer claim was confirmed and sharpened by re-measurement.** Discovery finding 5's two
  figures for `search_code` could not both describe one population. Re-measured: both are correct
  measurements of *different* populations, and the circularity check's population was built by
  substring match — pulling in a measured 9 calls to a different MCP server's tool. That is the exact
  failure FR-31 layer 2 exists to prevent, committed by this document's own evidence base. FR-48 now
  carries the rescoped figures.
- **Q1 closed 2026-08-16**, by `docs/product-superpowers/research/2026-08-16-scope-resolution-design.md`
  and the measurement under it. The mechanism reuses FR-31's resolution ladder rather than inventing
  a second idiom for the same problem, and its central claim was measured rather than reasoned: a
  measured 29 of 29 hits that made four scope mechanisms disagree were documentation artifacts, and
  the mechanisms agree once the documentation filter runs.
- **Not settled, and now the largest single risk in the content-check family:** whether FR-52's
  layer 2 generalises past "controllers" to category words with several naming conventions or none.
  It is deliberately left to FR-54's fixture rather than argued in this document, because a design
  note cannot answer it and a labelled fixture can.
