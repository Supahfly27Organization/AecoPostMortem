# Copilot Session Post-Mortem v1 — User Stories

**Date:** 2026-08-16
**Based on PRD:** `docs/product-superpowers/prds/2026-08-16-copilot-session-postmortem.md` (approved; FR-1 … FR-59, FR-47 cut, Phase E gated)
**Discovery:** `docs/product-superpowers/discovery/2026-08-16-copilot-session-postmortem.md`
**Supporting measurements:** `docs/product-superpowers/discovery/2026-08-16-content-check-false-positives.md`, `docs/product-superpowers/research/2026-08-16-scope-resolution-design.md`
**Status:** Approved 2026-08-16 by the operator at the User Review Gate — cleared to GitHub issues

**Persona.** One persona recurs throughout: **the operator** — the engineer who steers GitHub Copilot CLI with written rules in `AGENTS.md` and `CLAUDE.md`, edits those rules continuously to tune the process, and today has no way to find out whether any of it worked (PRD Part 1). They are the sole user. Where a story's direct beneficiary is a *downstream story builder* rather than the operator, that is stated honestly rather than dressed up as end-user value.

**Containment.** Every project named here is created at the root of this repository, `AecoPostMortem`, per PRD §3.1, and no story references `AecoLedger.Core` or `AecoLedger.Insights.*`. A story that appears to need an AecoLedger type copies it. **This replaces the earlier `SessionPostMortem/`-subtree rule**, which existed so the directory could one day be lifted into its own repository by `git subtree split`; that lift has happened, so the rule's purpose is served and its directory level is gone. What it guaranteed — no dependency on AecoLedger in either direction — is unchanged, and the assembly prefix is now `AecoPostMortem.*`.

**How this document treats dependencies.** The PRD's four phases (§3.5) are a *delivery* sequence with exit criteria, not a build order. Stories depend on **contracts** — a record shape, a response contract, an interface — not on other stories being finished, wherever that is genuinely true. Each `Depends on:` line is **labelled** — `contract X · implementation Y` — so the claim is checkable rather than aspirational. A contract dependency means the story can start the day that shape is committed, even if the producing story's tests are still red. An implementation dependency means it cannot, and says so. `Deps:` beneath it is the machine-readable union of both, and `Blocks:` is generated from it.

**Evidence discipline.** Every number below is cited to the PRD by FR number, or to a named measurement, or explicitly labelled an estimate. All sizes are **estimates** (S ≈ under a day, M ≈ one to two days, L ≈ two to four days — the calibration is itself an estimate, not a measurement).

---

## Epic overview

| Epic | Outcome | Stories | Phase |
|---|---|---|---|
| E1 — Durable ingestion | The rotating window stops costing history: every Copilot session on disk is preserved verbatim and reconstructible, in a solution that exists | S-01 S-02 S-03 S-04 S-05 S-06 S-07 S-45 S-47 S-48 S-49 S-50 | A |
| E2 — One session, readable | A single session becomes a thing a human can actually read, down to the raw event | S-08 S-09 S-10 S-11 S-12 S-52 S-53 | B |
| E3 — Waste, with denominators | Wasted effort is named and counted, and no figure appears without the population it was drawn from | S-13 S-14 S-15 S-16 S-17 S-18 S-44 S-46 | B |
| E4 — The rules the agent was given | The rules each session actually ran under are recovered, versioned, and every one of them gets a status | S-19 S-20 S-21 S-22 | C |
| E5 — Adherence you can check | An adherence figure exists, and it is falsifiable because it ships with the resolution that produced it | S-23 S-24 S-25 S-26 S-27 | C |
| E7 — The loop closes | The operator can see what recurs, whether last month's rule edit moved it, and what to do about it | S-35 S-36 S-37 S-38 S-39 S-51 S-54 | C |
| E8 — Missing capability | Where a tool or server is missing, the product says so — visibly marked as judgment | S-40 S-42 S-43 | D |
| E6 — Content checks that don't lie *(gated; sits last in this document)* | Rules about the code the agent writes become checkable without generating violations that do not exist | S-28 S-29 S-30 S-31 S-32 S-33 S-34 | **E — gated** |

Listed in document order. **E6 sits last, out of numeric sequence, because it is gated out of v1** —
its number was kept so nothing that references it goes stale. Story ids are not contiguous within an
epic either: S-41 was cut along with FR-47, and the contract and split stories were added to the
epics they serve rather than renumbered into them.

**Projects per PRD §3.1.** E1 → `AecoPostMortem.Data` (the DbContext, the entity model and the EF Core migrations) and `AecoPostMortem.Ingestion`, plus the solution, CLI, host and web shell (S-47, S-48). E4, E5, E6 (resolution and shapes) → `AecoPostMortem.Rules`. E3, E6 (findings), E7, E8 → `AecoPostMortem.Findings`. Surface stories → `AecoPostMortem.Api` and `web/`.

**The invariant that outranks the others.** Nothing in `AecoPostMortem.Rules` may name a tool, an MCP server or a repository (FR-34). Several stories below carry a guardrail criterion enforcing it, because it is the requirement the operator stated as non-negotiable in the discovery interview.

---

## Epic E1 — Durable ingestion

**Outcome:** The corpus spans a measured 111 days and rotates; every week without ingestion is process history permanently lost (PRD §3.5). This epic stops the loss and makes every session reconstructible.

### S-01 — Local store and its governance (FR-11, FR-2)

**As** the operator, **I want** every event preserved verbatim in one local file I control and can erase, **so that** my process history outlives Copilot's rotating window without my prompts and source code leaving the machine or accumulating somewhere I cannot find.

**Implements:** FR-2, FR-11
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-47. Root contract story — its first commit publishes the RAW row shape (source file, byte offset, content hash, provider version, verbatim JSON) so S-02, S-04 and S-06 can build against it.
**Deps:** S-47
**Blocks:** S-02, S-03, S-04, S-07, S-28, S-44, S-49

```gherkin
Scenario: An event is preserved verbatim
  Given a Copilot event carrying fields the parser does not recognise
  When it is persisted to RAW
  Then the row carries source file, byte offset, content hash and provider version
  And the original JSON round-trips byte-identically, unknown fields included

Scenario: The store is created under the operator's control
  Given no store exists
  When ingestion first writes
  Then the store is created at its documented per-user path
  And its schema is created by applying the migrations, with no command from the operator
  And the file carries owner-only permissions

Scenario: A store from an older build is brought forward, not rejected
  Given a store written by a build with an earlier RAW schema
  When the product next opens it
  Then the outstanding migrations are applied automatically
  And the RAW rows already in it are preserved unchanged

Scenario: Purge is total
  Given a populated store
  When the operator runs the purge command
  Then the store file is deleted entirely
  And running purge again reports nothing to purge, without error

Scenario: No network call exists
  Given the ingestion assembly
  When its dependencies are inspected
  Then no HTTP client, socket or outbound transport is referenced

Scenario: The indexes the read path depends on exist
  Given the created schema
  When its indexes are inspected
  Then every index the measured query shapes depend on is present
  And a missing one fails the schema test rather than degrading a surface quietly

Scenario: The source is never written to
  Given a Copilot session directory with recorded file hashes
  When ingestion has run
  Then every file under that directory is byte-identical to before
  And the ingestion assembly opens every source file read-only
```

**Edge cases:** byte-identical replay is Phase A's exit criterion (PRD §3.5), so the round-trip test must cover non-ASCII, embedded newlines and the largest measured system prompt; a store path colliding with an existing non-product file fails loudly rather than overwriting; store size must be queryable, since S-05's coverage report consumes it. **RAW is the only layer that carries a migration** (PRD §3.8) — a migration authored against NORMALIZED or FINDINGS is a defect, because those are required to be reproducible from RAW and a migration would hide a rebuild bug behind a preserved row. **The RAW insert path bypasses EF Core change tracking** (PRD §3.1): a measured 56,138 rows arrive in one full ingest, so this story's append is batched raw SQL against the same schema, not per-entity tracking. **Covering indexes are load-bearing, not tuning** — measured, their absence cost a 13.8× regression on one aggregate at the design target, against a 1.6× gap once present (`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md`), which is why the schema test asserts them rather than leaving them to a later profiling pass.

---

### S-02 — Session discovery and event-line parsing (FR-1, FR-3, FR-6)

**As** the operator, **I want** every session under `~/.copilot/session-state/` found and parsed line by line without silent drops, **so that** the product's coverage matches what actually happened rather than what happened to parse cleanly.

**Implements:** FR-1, FR-3, FR-6
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-01, S-44.
**Deps:** S-01, S-44
**Blocks:** S-04, S-06, S-19

```gherkin
Scenario: Sessions are discovered without configuration
  Given a machine with a populated Copilot session-state directory
  When discovery runs
  Then every session directory holding an events.jsonl is classified
  And sibling session.db, rewind-snapshots and workspace.yaml files are classified alongside it

Scenario: A missing directory is reported, not thrown
  Given no Copilot directory on the machine
  When discovery runs
  Then the run reports that no source was found and exits successfully

Scenario: Provider version comes from line 1
  Given a session whose first line is a session.start event
  When the file is ingested
  Then the provider version is read from that event's copilotVersion
  And parsers are registered against the event-schema version it declares
  And the file is not scanned for a first-declared version

Scenario: A partial trailing line is unfinished, not malformed
  Given a session file whose last line has no terminating newline
  When it is ingested
  Then ingestion stops at the last newline-terminated line
  And the high-water offset is recorded
  And that line is not counted as malformed

Scenario: A skipped line is retried, never written off
  Given a file with a previously skipped line that has since been completed
  When ingestion runs again
  Then that line is parsed and persisted
  And no line is ever recorded as permanently bad

Scenario: The malformed-line check registers itself
  Given a completed ingest
  When the check registry is read
  Then the malformed-line check appears with the number of lines parsed, whether or not any failed

Scenario: A malformed line is skipped, not fatal
  Given a session file containing one unparseable line
  When it is ingested
  Then every other line is persisted
  And the run reports a skipped-line count for that file
```

**Edge cases:** a measured 14 distinct CLI versions already appear in one corpus, so an unknown version must ingest rather than refuse (FR-3); an empty `events.jsonl` is a valid session with zero events, not an error; a session directory with no `events.jsonl` is classified and skipped, since a measured 13 of 48 directories are in that state per the data map.

---

### S-03 — Polymorphic tool arguments (FR-4)

**As** a downstream story builder — and ultimately the operator, whose code rules are unreadable without this — **I want** `tool.execution_start.data.arguments` parsed as either an object or a string, **so that** patch envelopes survive ingestion instead of being silently dropped.

**Implements:** FR-4
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-01.
**Deps:** S-01
**Blocks:** S-28

```gherkin
Scenario: A patch envelope survives as a string
  Given an apply_patch call whose arguments field is a JSON string
  When it is parsed
  Then the envelope text is preserved intact
  And no exception is raised

Scenario: An ordinary tool call parses as an object
  Given a view call whose arguments field is a JSON object
  When it is parsed
  Then its named fields are available individually

Scenario: The round-trip is proven over the corpus
  Given the reference corpus
  When every apply_patch call is parsed and re-serialised
  Then all of them round-trip, and a single failure fails the build
```

**Edge cases:** this is the failure mode PRD §3.9 lists first — a build assuming an object drops every patch and finding class 3 loses its entire input, silently and without error. The corpus round-trip is therefore a build gate, not a unit test. A future tool arriving with a third argument shape must be recorded as unparsed rather than coerced.

---

### S-04 — Idempotent, append-safe re-ingest (FR-5)

**As** the operator, **I want** to re-run ingestion whenever I like without duplicating anything, **so that** keeping the store current is a habit rather than a decision.

**Implements:** FR-5
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-01 · implementation S-02.
**Deps:** S-01, S-02
**Blocks:** S-05, S-45

```gherkin
Scenario: Re-running adds nothing
  Given a fully ingested corpus
  When ingestion runs again with no new sessions
  Then the store's event count is unchanged
  And the run reports zero new events

Scenario: A grown session ingests only its new tail
  Given a session already ingested
  And the same file has since grown by appended events
  When ingestion runs
  Then only the appended events are added
  And previously stored rows are untouched

Scenario: A rewritten file is detected, not silently merged
  Given a session file whose existing bytes no longer match their stored content hash
  When ingestion runs
  Then the mismatch is reported rather than appended over
```

**Edge cases:** append-only was verified — on all 8 events carrying `eventsFileSizeBytes`, the declared value equals the byte offset where that event begins, delta 0 (data map self-review) — so byte offsets are safe identity, with the content hash as the second line of defence; a resumed session continues the same byte stream and must not be treated as a new file.

---

### S-05 — Self-exclusion and the coverage report (FR-7, FR-14)

**As** the operator, **I want** this product's own analysis sessions excluded at ingest, and a report of exactly what was ingested, **so that** the tool does not measure itself and I can tell coverage from silence.

**Implements:** FR-7, FR-14
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-44 · implementation S-04.
**Deps:** S-04, S-44
**Blocks:** none

```gherkin
Scenario: An analysis session never enters the store
  Given an operator-configured exclusion list containing a repository root
  And a session whose start context cwd falls under it
  When ingestion runs
  Then no event from that session is persisted
  And the exclusion is reported with its reason

Scenario: The list is configurable, not compiled in
  Given a machine with a different layout
  When the operator adds a path to the exclusion list
  Then that path is honoured without rebuilding the product

Scenario: Exclusion happens at ingest, not at query time
  Given the store
  When it is queried without any filter
  Then no excluded session's events are present at all

Scenario: Every run reports its coverage
  Given any ingestion run
  When it completes
  Then it reports sessions found, ingested, excluded and why, lines parsed, lines skipped, and events by type
```

**Edge cases:** the discovery lists self-contamination as a key risk because sessions analysing agent failure pollute every string-derived signal — a rule's own text appearing in a transcript looks exactly like a rule being followed; whether any session in the reference corpus is already such a session was **not measured**, so the exclusion must work retroactively over an already-ingested store as well as prospectively.

---

### S-06 — Execution record reconstruction (FR-8, FR-9)

**As** the operator, **I want** each session rebuilt into sessions, turns, tool calls and agents with correct ownership, **so that** every later finding can point at the exact moment it happened.

**Implements:** FR-8, FR-9
**Priority:** Must Have · **Estimate:** L (estimate)
**Depends on:** contract S-44, S-49 · implementation S-02.
**Deps:** S-02, S-44, S-49
**Blocks:** S-09, S-45

```gherkin
Scenario: Causality is reconstructed from the envelope
  Given ingested events
  When the execution record is built
  Then each event's id and parentId form a chain across the whole session
  And turn boundaries come from the turn_start and turn_end pair

Scenario: A subagent's work is attributed to it, not to the main thread
  Given a session containing subagent spawns
  When the record is built
  Then every event carrying an agentId is attributed to that agent
  And an event with no agentId is attributed to the main thread

Scenario: Every spawn resolves to its spawning call
  Given the reference corpus
  When agents are reconstructed
  Then each subagent.started resolves to the task call that produced it
  And any that does not resolve is reported rather than dropped

Scenario: The spawn-resolution check registers itself
  Given a completed reconstruction
  When the check registry is read
  Then the spawn-resolution check appears with the number of spawns examined, whether or not any failed

Scenario: Nesting is derived, not assumed
  Given a subagent spawned by another subagent
  When the record is built
  Then its parent agent is derived from the agentId on the spawning task call
```

**Edge cases:** a measured 470 of 470 spawns resolve in the reference corpus, so a non-resolving spawn is a real signal and belongs in S-37's silent-checks surface rather than being swallowed; `subagent.completed` carries tokens and duration on only a measured 215 of 462 completions, so the record must represent "completed, cost unknown" distinctly from "not completed".

---

### S-07 — Volume control and the sources deliberately not read (FR-10, FR-12, FR-13)

**As** the operator, **I want** near-duplicate system prompts stored once and the live Copilot database left alone, **so that** the store stays a manageable local file and never reads something that is being written underneath it.

**Implements:** FR-10, FR-12, FR-13
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-01.
**Deps:** S-01
**Blocks:** S-19

```gherkin
Scenario: Identical system prompts are stored once
  Given many sessions carrying the same system prompt text
  When they are ingested
  Then the text is stored once and referenced by content hash
  And each session still resolves to its own full prompt text

Scenario: The live database is not opened
  Given a machine with a Copilot session-store database present
  When ingestion runs
  Then that file is never opened
  And the coverage report states it was skipped by design

Scenario: A rewritten snapshot index keeps both versions
  Given a rewind-snapshots index file that has been rewritten in place since last ingest
  When ingestion runs
  Then both versions are retained at the same source identity
```

**Edge cases:** a measured 337 system messages at a measured median 54,335 characters are mostly near-duplicates, so this is a design decision rather than a later optimisation (FR-12); the excluded database is live-written and WAL-dependent and covers a measured 7 of 40 sessions, offering only per-request latency and nano-AIU — nothing any finding class needs (FR-10), and this is recorded so it is not rediscovered as an oversight.

---

### S-45 — Phase A verification: census, replay and scale (PRD §3.5, §3.7)

**As** the operator, **I want** proof that ingestion reproduced my corpus exactly and finishes fast enough to run often, **so that** I trust the store as a record and re-running it never becomes a decision.

**Implements:** FR-55, PRD §3.5 Phase A exit criterion, §3.7 scale targets
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** implementation S-04, S-06. Verification story — it asserts properties of E1 rather than adding behaviour.
**Deps:** S-04, S-06
**Blocks:** none

```gherkin
Scenario: The census is frozen, not read live
  Given the reference sessions at the stated snapshot date
  When the fixture corpus is created
  Then their bytes or hashes plus a post-exclusion census are checked in
  And every exit criterion is evaluated against that fixture, never against the live directory

Scenario: The event census reproduces the reference counts
  Given the frozen fixture corpus
  When ingestion completes
  Then the count of each event type matches the data map's measured census
  And any type present on disk but absent from the store fails the check

Scenario: RAW replays byte-identically across the whole corpus
  Given a fully ingested corpus
  When every RAW row is re-serialised and compared to its source line
  Then all of them match byte for byte
  And a single mismatch fails the build

Scenario: A full ingest meets its time target
  Given a corpus of the reference size
  When a full ingest runs from an empty store
  Then it completes inside the target stated in PRD §3.7

Scenario: An incremental re-ingest meets its time target
  Given a fully ingested corpus with no new events
  When ingestion runs again
  Then it completes inside the target stated in PRD §3.7
```

**Edge cases:** the time figures in PRD §3.7 are **targets, not measurements**, so a miss is a conversation about the target rather than an automatic defect — but it must be visible rather than silently absorbed; the census check must compare against a recorded expectation file rather than against a number typed into a test, so it survives the corpus growing.

---

### S-47 — Solution scaffold and the command surface (FR-58, PRD §3.1)

**As** the operator, **I want** a solution I can build and a command I can run, **so that** "run the ingest command" in the product's own getting-started line is a thing that exists.

**Implements:** FR-58, PRD §3.1
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** none. Root story — nothing else has anywhere to live until this exists.
**Deps:** none
**Blocks:** S-01, S-48

```gherkin
Scenario: The solution builds and is contained
  Given a clean checkout
  When the AecoPostMortem solution is built
  Then it builds from the solution file at the repository root
  And no project in it references any AecoLedger assembly

Scenario: The command surface exists
  Given the built product
  When the operator runs the CLI with no arguments
  Then it lists ingest, purge and serve, each with its arguments and its output channel
  And serve reports which surfaces are not yet implemented rather than failing

Scenario: The test projects exist and run
  Given the built solution
  When the test suite is discovered
  Then a test project exists for each source project and all of them execute

Scenario: The frontend lives in web and builds from there
  Given a clean checkout
  When the React project is built
  Then it builds from web, and no frontend command is run from the repository root

Scenario: Containment is enforced, not conventional
  Given the repository
  When the containment test runs
  Then it fails if any project references an AecoLedger assembly
  And it fails if any project reference resolves to a path outside this repository
  And it fails if any project in the solution sits outside src, test or web
```

**Edge cases:** the containment test is the mechanical form of PRD §3.1's requirement and belongs here rather than in a review checklist — but **what it tests changed with the repository**. It once asserted that no project crossed the `SessionPostMortem/` directory boundary in either direction, because the subtree had to stay liftable by `git subtree split`. The lift has happened, so the test now asserts the thing that boundary was protecting: no AecoLedger assembly reference, and no project reference escaping the repository. A path-shaped assertion would now pass trivially and prove nothing. The `serve` command is specified here but has nothing to serve until S-48.

---

### S-48 — API host, web shell and the zero-data state (PRD §3.1)

**As** the operator, **I want** a running app that tells me what to do before I have ingested anything, **so that** my first encounter with the product is not a blank page.

**Implements:** PRD §3.1 (the host and the web shell)
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-50 · implementation S-47.
**Deps:** S-47, S-50
**Blocks:** S-08

```gherkin
Scenario: The three surfaces are routable
  Given the running app
  When the operator opens it
  Then the digest, a session view and the Rules Inventory are each reachable
  And a surface not yet built shows a placeholder naming the release it arrives in

Scenario: Before the first ingest, the app says so
  Given an empty store
  When the operator opens the app
  Then it states that nothing has been ingested and names the command that would fix it

Scenario: With no Copilot directory, the app says that instead
  Given a machine with no Copilot session-state directory
  When the operator opens the app
  Then it states that no source was found, distinctly from an empty store
```

**Edge cases:** the two empty states are different diagnoses with different fixes and must not collapse into one message; a repository carrying no rules at all is a third empty state, owned by S-22, and PRD Part 6 names that user explicitly.

---

### S-49 — Execution-record entity contract (PRD §3.2)

**As** a downstream story builder — and ultimately the operator, who would otherwise wait on one large story before anything else could start — **I want** the NORMALIZED entity shapes published before they are populated, **so that** fifteen stories can build against a shape rather than against an unfinished implementation.

**Implements:** PRD §3.2 NORMALIZED layer
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-01. Contract story — its first commit publishes the shapes.
**Deps:** S-01
**Blocks:** S-06, S-08, S-09, S-11, S-13, S-14, S-15, S-16, S-17, S-18, S-19, S-21, S-27, S-43, S-46

```gherkin
Scenario: The entity shapes are published
  Given the contract
  When it is read
  Then session, turn, tool call, agent, skill, hook, permission and write unit each have a defined shape

Scenario: Every entity is scoped by session
  Given any entity in the contract
  When its natural key is inspected
  Then the session is part of that key

Scenario: Agent attribution is representable as absent
  Given the tool-call shape
  When an event carries no agent id
  Then the shape represents main-thread ownership explicitly, not as a null to be guessed at
```

**Edge cases:** `subagent.completed` carries tokens and duration on only a measured 215 of 462 completions, so the agent shape must distinguish "completed, cost unknown" from "not completed" — S-06's edge case named this and had nowhere structural to put it.

---

### S-50 — API response envelope (FR-59)

**As** a downstream story builder — and ultimately the operator, for whom a figure without its resolution is unfalsifiable — **I want** provenance and resolution carried in the response contract itself, **so that** no client can render a finding without them and a second client cannot bypass the rule.

**Implements:** FR-59
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-44. Contract story.
**Deps:** S-44
**Blocks:** S-08, S-22, S-24, S-36, S-37, S-42, S-48

```gherkin
Scenario: Every served finding carries provenance
  Given any finding served by the API
  When the response is inspected
  Then it carries the finding's provenance level

Scenario: A finding with no suggestion is representable
  Given a finding whose class has no suggestion template
  When it is serialised
  Then the envelope represents the absent suggestion as a valid state, not a missing field

Scenario: An adherence figure cannot be served bare
  Given a request for an adherence figure
  When the response is assembled
  Then a figure without its resolution and rule version cannot be represented in the contract
```

**Edge cases:** this is where FR-33's refusal actually lives — S-24 tests the behaviour, this story makes the bare figure unrepresentable; the envelope must survive a finding class having no suggestion (FR-56), which is a valid state rather than a missing field.

---

## Epic E2 — One session, readable

**Outcome:** Today the evidence for a bad session is a file too large to read. This epic turns one session into something a human can move through, with every claim one click from the event that produced it.

### S-08 — Flight Recorder: masthead and tape (FR-21, part 1 of 3)

**As** the operator, **the morning after a session that felt wrong**, **I want** that session laid out as a time-ordered tape with its totals above it, **so that** I can find the moment it went off the rails instead of scrolling a transcript.

**Implements:** FR-21 (masthead, tape)
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-49, S-50 · implementation S-48.
**Deps:** S-48, S-49, S-50
**Blocks:** S-12, S-52, S-53

```gherkin
Scenario: The masthead states what this session was
  Given a reconstructed session
  When the recorder opens
  Then it shows session identity, repository, branch, CLI version, elapsed time, turns, tool calls, subagents, skills, models and context size at end

Scenario: The tape is ordered by real time
  Given a session containing hooks, prompts, skills, tool calls and MCP calls
  When the tape renders
  Then steps appear in wall-clock order with their offset from session start

Scenario: A session with no tool calls still renders
  Given one of the measured 2 of 35 sessions that made no tool call
  When the recorder opens
  Then the masthead renders and the tape states that no steps were recorded
```

**Edge cases:** this is the half everything else hangs off, which is why it is separated — S-52 and S-53 both need a tape to attach to; the finding chips are S-52's because they need findings joined per session, which is a different data path from the tape.

---

### S-52 — Flight Recorder: inspector, tabs and finding chips (FR-21, part 2 of 3)

**As** the operator, **I want** to select any step and see its detail, its reasoning and the raw event, **so that** every claim on the screen is one click from what produced it.

**Implements:** FR-21 (inspector, tabs, chips)
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** implementation S-08.
**Deps:** S-08
**Blocks:** S-09, S-10

```gherkin
Scenario: Selecting a step shows its evidence
  Given a rendered tape
  When the operator selects any step
  Then the inspector shows its detail, its readable reasoning where any exists, and the raw event that produced it

Scenario: The inspector has three named tabs
  Given a selected step
  When the inspector renders
  Then Detail, Thinking and Raw are each selectable
  And Raw shows the event that produced the step

Scenario: The finding chips summarise the session
  Given a session carrying findings
  When the recorder opens
  Then a chip row states each finding affecting this session with its count

Scenario: Nothing selected is a designed state
  Given a freshly opened recorder
  When no step is selected
  Then the inspector states that a step should be picked, rather than rendering blank panels
```

**Edge cases:** the Raw tab is the provenance guarantee made clickable, not a debugging affordance, so it must never be the tab that gets cut under pressure; a step whose raw event was skipped at ingest shows that fact rather than an empty panel.

---

### S-53 — Flight Recorder: scale, keyboard and the non-happy states (FR-21, part 3 of 3)

**As** the operator, **I want** the recorder to stay usable on my longest sessions and my broken ones, **so that** the sessions most worth examining are not the ones it cannot open.

**Implements:** FR-21 (virtualisation, keyboard, empty/loading/error states)
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** implementation S-08.
**Deps:** S-08
**Blocks:** none

```gherkin
Scenario: A long session renders without loading every step
  Given a session of the largest measured size — 84 turns and 764 tool calls
  When the tape renders
  Then it virtualises rather than mounting every step at once

Scenario: The tape is navigable without a mouse
  Given a rendered tape
  When the operator uses the keyboard alone
  Then every step can be reached and selected

Scenario: A session still ingesting says so
  Given a session whose ingest has not completed
  When the recorder opens
  Then it states that the session is incomplete rather than rendering a partial tape as final

Scenario: A session that failed to reconstruct says why
  Given a session whose reconstruction failed
  When the recorder opens
  Then it states that reconstruction failed and what was skipped
```

**Edge cases:** this story is where the size problem in the original single-story version actually lived, and it is deliberately last of the three so it cannot be silently dropped by being the tail of a larger story.

---

### S-09 — Subagent lanes and their real output (FR-22)

**As** the operator, **I want** each subagent's work on its own lane, with the report it actually produced, **so that** I can judge what a subagent did rather than reading the stub its parent recorded.

**Implements:** FR-22
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-49 · implementation S-06, S-52.
**Deps:** S-06, S-49, S-52
**Blocks:** none

```gherkin
Scenario: A subagent's output is read from its own stream
  Given a subagent that produced a long final report
  When its lane is inspected
  Then the report shown is the last assistant message bearing that agent's id

Scenario: The parent's truncated result is never shown as the output
  Given a read_agent completion ending in the marker "(Full response provided to agent)"
  When the subagent's output is displayed
  Then that stub is not what is shown

Scenario: A subagent with no output of its own says so
  Given a subagent that produced no messages under its own id
  When its lane is inspected
  Then it states that no output was recorded
  And it does not fall back to the parent's stub

Scenario: A failed subagent renders as failed
  Given a subagent that ended in failure
  When its lane renders
  Then the failure and its recorded error are shown

Scenario: Lanes are visually separable from the main thread
  Given a session with concurrent subagents
  When the tape renders
  Then each agent occupies its own lane and the main thread is distinguishable from all of them
```

**Edge cases:** measured on `read_agent` completions, results have a median of 48 characters against subagent reports whose median is far longer, which is why this story exists at all; a subagent with no messages of its own must show "no output recorded" rather than falling back to the parent's stub; a measured 6 `subagent.failed` events exist, so failure is a rendered state.

---

### S-10 — The Thinking lane, and why it is empty (FR-23)

**As** the operator, **I want** to be told when reasoning is unreadable rather than shown a blank panel, **so that** I do not mistake provider encryption for the agent not having thought.

**Implements:** FR-23
**Priority:** Should Have · **Estimate:** S (estimate)
**Depends on:** implementation S-52.
**Deps:** S-52
**Blocks:** none

```gherkin
Scenario: Readable reasoning is shown
  Given a message carrying plaintext reasoning
  When its step is inspected
  Then the Thinking tab shows that text

Scenario: Encrypted reasoning is explained, not blanked
  Given a session whose model writes provider-encrypted reasoning
  When the Thinking tab is opened
  Then it states that reasoning is encrypted for this model
  And it reports the measured readable share for the models this session actually used
```

**Edge cases:** the split is model-dependent — a measured 3.5% readable on one model against a measured 88.2% on another (recorder mockup) — so the figure must be computed per session rather than stated as a constant; a session using two models needs both figures, not an average.

---

### S-11 — Session token figures, without inventing a price (FR-24)

**As** the operator, **I want** token totals where the log records them and nothing where it does not, **so that** no number on the screen is something the product made up.

**Implements:** FR-24
**Priority:** Should Have · **Estimate:** S (estimate)
**Depends on:** contract S-49.
**Deps:** S-49
**Blocks:** none

```gherkin
Scenario: Session totals come from the shutdown event
  Given a session whose shutdown event carries per-model metrics
  When the masthead renders
  Then token totals are read from that event and marked Observed

Scenario: A session without totals says so
  Given a session with no shutdown metrics
  When the masthead renders
  Then it states that session totals were not recorded, rather than showing zero

Scenario: No currency appears anywhere
  Given any surface in the product
  When it renders a cost-like figure
  Then no monetary amount is shown
```

**Edge cases:** shutdown metrics are measured present on 31 of 35 sessions, so the missing case is common enough to design rather than treat as exceptional; Copilot prices in premium requests and nano-AIU and no local file states a conversion rate (PRD Part 6), so apportioning a session total across turns is Inferred and this product does not do it at all.

---

### S-12 — Skills as first-class steps (FR-25)

**As** the operator, **I want** skill invocations on the tape as their own steps, **so that** I can see which of my process scaffolding actually ran.

**Implements:** FR-25
**Priority:** Should Have · **Estimate:** S (estimate)
**Depends on:** implementation S-08.
**Deps:** S-08
**Blocks:** none

```gherkin
Scenario: A skill invocation is its own step
  Given a session containing skill invocations
  When the tape renders
  Then each appears as a step carrying its name, plugin and plugin version

Scenario: A subagent's skills belong to its lane
  Given a skill invoked inside a subagent
  When the tape renders
  Then that step appears on the subagent's lane, not the main thread
```

**Edge cases:** a measured 794 skill invocations exist across the corpus, a measured 445 of them scoped to a subagent, so lane attribution is the common case rather than the exception.

---

## Epic E3 — Waste, with denominators

**Outcome:** Cheapest findings, firing on the most sessions, proving the pipeline end to end. Every figure here carries the population it came from, because a figure whose population is unstated is what made two surfaces contradict each other during discovery.

### S-44 — The findings contract and the check registry (PRD §3.2, §3.8)

**As** a downstream story builder — and ultimately the operator, for whom an unlabelled finding is a guess — **I want** one published shape every finding is written in and one registry of every check that exists, **so that** provenance cannot be omitted by accident and a check that never ran cannot look like a check that found nothing.

**Implements:** FR-57, PRD §3.2 FINDINGS layer, §3.8 "provenance is structural"
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-01. Root contract story — its first commit publishes the finding record shape and the check-registry shape so every finding story in E3, E5, E6, E7 and E8 builds against them.
**Deps:** S-01
**Blocks:** S-02, S-05, S-06, S-13, S-14, S-15, S-16, S-17, S-18, S-32, S-33, S-36, S-37, S-39, S-40, S-42, S-46, S-50, S-51

```gherkin
Scenario: A finding cannot exist without provenance
  Given the finding record type
  When a finding is constructed without a provenance level
  Then construction fails

Scenario: The record carries everything the surfaces need
  Given any persisted finding
  When it is read
  Then it carries its class, provenance, evidence, recurrence, the resolution used where one applies, its suggestion, and the operator's response

Scenario: Each finding class declares its recurrence key
  Given the finding contract
  When a finding class is registered
  Then it declares what makes two occurrences in two sessions the same finding
  And a finding whose rule spans several rule-set versions states whether it is one finding or many

Scenario: Every check is registered whether or not it fired
  Given a completed analysis run
  When the check registry is read
  Then every check appears with its run status and the population it was run over

Scenario: A refused check is distinguishable from a clean one
  Given a check that was refused and a check that ran and found nothing
  When the registry is read
  Then the two are distinct states, not both zero
```

**Edge cases:** the registry is what makes S-37 possible and what stops silence reading as compliance (PRD §3.9); findings must be fully re-derivable from RAW (PRD §3.2), so nothing in this shape may be the only copy of anything; a finding whose recurrence is one session must still carry a recurrence value rather than omitting the field.

---

### S-46 — Rebuildability and determinism (PRD §3.2, §3.8)

**As** the operator, **I want** every derived layer reproducible from the raw events and identical every time, **so that** a finding I acted on last month can be reproduced today and a number that changes without my data changing is a bug, not a mystery.

**Implements:** PRD §3.2 "fully re-derivable from RAW", §3.8 rebuildable and determinism
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-44, S-49.
**Deps:** S-44, S-49
**Blocks:** none

```gherkin
Scenario: Derived layers rebuild from RAW alone
  Given a store with execution record and findings populated
  When the derived layers are deleted and re-derived from RAW
  Then the rebuilt layers are identical to what was deleted

Scenario: The same store produces the same findings
  Given an unchanged store
  When analysis runs twice
  Then both runs produce identical findings, in identical order

Scenario: No check depends on the clock or on chance
  Given the analysis code path
  When it is inspected
  Then no check reads the current time, samples randomly, or calls a model
```

**Edge cases:** identical *order* matters as much as identical content, because the digest ranks by sessions affected and a tie broken arbitrarily would reorder the operator's priorities between runs; a rebuild that is identical except for row ids is acceptable and the comparison must be defined accordingly; this story is what makes RAW load-bearing rather than merely stored.

---

### S-13 — Repeated file reads (FR-15)

**As** the operator, **I want** to see which files a session opened over and over, **so that** I can decide whether to pin them as standing context instead of writing another rule the agent forgets.

**Implements:** FR-15
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-44, S-49.
**Deps:** S-44, S-49
**Blocks:** none

```gherkin
Scenario: Repeated reads are grouped per session
  Given a session that opened one path four or more times
  When the finding is computed
  Then that path is reported with its read count for that session

Scenario: The finding carries its recurrence
  Given the whole corpus
  When the finding is ranked in the digest
  Then it states how many sessions it touched

Scenario: A session with no repeats produces no finding
  Given a session where no path was read more than three times
  When the finding is computed
  Then nothing is reported for that session
```

**Edge cases:** measured 16 of 35 sessions qualify, worst single path a measured 74 reads and worst session a measured 1,367 such reads across 90 paths (FR-15) — so the surface must rank and truncate rather than list everything; the read path is present on a measured 5,201 of 5,201 `view` calls, so a missing path is a parser defect, not a data gap.

---

### S-14 — Failed tool calls (FR-16)

**As** the operator, **I want** failure rates per tool with their raw counts, **so that** I can tell a broken tool from an unlucky one.

**Implements:** FR-16
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-44, S-49.
**Deps:** S-44, S-49
**Blocks:** S-40

```gherkin
Scenario: A rate never appears without its counts
  Given any tool with recorded failures
  When the finding renders
  Then it shows failures over calls alongside the percentage

Scenario: A rarely used tool is not ranked as if it were common
  Given a tool called only a handful of times
  When findings are ranked
  Then its session count is shown so a high rate on few calls is visible as such
```

**Edge cases:** the success field is measured present on 16,076 of 16,076 completions, so absence is a parser defect; a measured 61.2% failure rate on one tool used in only a measured 4 sessions is exactly the case where a bare percentage misleads.

---

### S-15 — Hook failures, with both denominators (FR-17)

**As** the operator, **I want** to be told that a hook has been failing silently, with the population the figure is drawn from, **so that** I can fix a bug the CLI never surfaced and check the claim myself.

**Implements:** FR-17
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-44, S-49.
**Deps:** S-44, S-49
**Blocks:** none

```gherkin
Scenario: Both denominators are always stated together
  Given hook failures across the corpus
  When the finding renders
  Then it states the count over all sessions and the count over sessions that made a tool call
  And neither figure appears alone

Scenario: The evidence is the field, quoted
  Given the finding
  When its evidence is expanded
  Then it shows the hook event with its success flag and error text
```

**Edge cases:** measured 34 of 35 sessions overall and 32 of the 33 that made a tool call — both are correct and the two extra sessions made no tool call yet still failed the hook, which is precisely why one figure alone reads as a contradiction; this finding should disappear from the digest on its own once the operator fixes the hook, and that is the intended behaviour, not a regression.

---

### S-16 — Aborted turns (FR-18)

**As** the operator, **I want** to see turns that were abandoned mid-flight, **so that** I can tell whether they cluster on a task shape I should stop giving the agent.

**Implements:** FR-18
**Priority:** Should Have · **Estimate:** S (estimate)
**Depends on:** contract S-44, S-49.
**Deps:** S-44, S-49
**Blocks:** none

```gherkin
Scenario: An abort is reported with its reason
  Given a session containing an abort event
  When the finding renders
  Then it shows the abort with its recorded reason and its position in the session

Scenario: The unknown is stated
  Given any abort finding
  When it renders
  Then it states that no rollback event is recorded, so what was already changed on disk is unknown
```

**Edge cases:** a measured 9 aborts across 8 sessions is low volume, so this finding ranks low by recurrence and must not be inflated to look more important than it measures.

---

### S-17 — Phase churn (FR-19)

**As** the operator, **I want** to see where the agent returned to a phase it had already declared finished, **so that** I have a handle on wandering that is not just a feeling.

**Implements:** FR-19
**Priority:** Should Have · **Estimate:** M (estimate)
**Depends on:** contract S-44, S-49.
**Deps:** S-44, S-49
**Blocks:** none

```gherkin
Scenario: The phase vocabulary and its ordering are derived, not hard-coded
  Given an ingested corpus
  When phase churn is computed
  Then the phase vocabulary is derived from the declared intents in the corpus
  And the ordering is derived from the sequence in which phases are first declared

Scenario: The derivation is recorded with the finding
  Given a churn finding
  When it renders
  Then the vocabulary and ordering used to produce it are shown alongside it

Scenario: A return to an earlier phase is detected
  Given a session whose declared intents are non-monotonic under the derived ordering
  When the finding is computed
  Then each return to an earlier phase is counted

Scenario: The finding shows its own denominator
  Given a churn finding
  When it renders
  Then it states returns against total declared intents for that session
```

**Edge cases:** a measured 2,167 intent calls exist corpus-wide and the worst session measured 104 returns across 352 intents, so per-session normalisation is required or long sessions always look worst; a session declaring no intents produces no finding rather than a zero.

---

### S-18 — Interruption load (FR-20)

**As** the operator, **I want** to see how often the agent stopped to ask me something, **so that** I can judge whether my permission setup is costing me attention.

**Implements:** FR-20
**Priority:** Could Have · **Estimate:** S (estimate)
**Depends on:** contract S-44, S-49.
**Deps:** S-44, S-49
**Blocks:** none

```gherkin
Scenario: Permission prompts and questions are counted separately
  Given a session containing both permission requests and direct questions
  When the finding renders
  Then the two are reported as distinct counts, not summed

Scenario: Denial is read from the field, not inferred
  Given a completed permission request
  When its outcome is shown
  Then the outcome comes from the recorded result kind and is marked Observed
```

**Edge cases:** a measured 1,033 permission prompts against a measured 1,031 completions means two prompts have no recorded outcome, which must render as "no outcome recorded" rather than as a denial.

---

## Epic E4 — The rules the agent was given

**Outcome:** The product's unfair advantage is that rules and behaviour sit in the same file (PRD Part 1). This epic recovers the rules as the agent actually received them, scopes them to the version in force, and gives every one of them a status.

### S-19 — Rule extraction from the system prompt (FR-26)

**As** the operator, **I want** my instruction files recovered exactly as they were injected into each session, **so that** adherence is measured against what the agent was really told, not against what my repository says today.

**Implements:** FR-26
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-49 · implementation S-02, S-07.
**Deps:** S-02, S-07, S-49
**Blocks:** S-20, S-22, S-23, S-25, S-27, S-30, S-38, S-43

```gherkin
Scenario: Rule statements are recovered verbatim and attributed
  Given a session whose system prompt carries custom instruction blocks
  When rules are extracted
  Then each statement is stored verbatim with the source file it was headed by

Scenario: The extraction unit is one list item, normalised
  Given an instruction block containing list items and prose
  When statements are extracted
  Then each list item becomes one statement, with its marker stripped and its text trimmed
  And identical statements across sessions are deduplicated to one

Scenario: The repository is never read
  Given the extraction code path
  When its file access is inspected
  Then it reads only the ingested store, never any markdown file on disk

Scenario: A session with no instruction blocks is recorded as such
  Given a session carrying no custom instruction block
  When extraction runs
  Then it is recorded as carrying no rules, distinctly from carrying rules that matched nothing
```

**Edge cases:** measured 32 of 35 sessions carry blocks, so the empty case is real; a measured 43 distinct statements were recovered from a measured 14 distinct blocks, so extraction must deduplicate across sessions while preserving which sessions carried what; a statement that is not a rule at all — a documentation index, a heading — must survive extraction and be classified by S-22 rather than filtered out silently.

---

### S-20 — Rule-set versioning (FR-27, FR-28)

**As** the operator, **who edits rules continuously**, **I want** every adherence figure pinned to the version of the rule set that was actually in force, **so that** no number I am shown describes a rule set that never existed.

**Implements:** FR-27, FR-28
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-19.
**Deps:** S-19
**Blocks:** S-22, S-24, S-35, S-38

```gherkin
Scenario: A version is identified by its block set
  Given sessions carrying different instruction blocks
  When versions are computed
  Then sessions sharing an identical block set share a version, per repository

Scenario: A version's window is contiguous
  Given a repository's sessions in time order
  When a version is rendered
  Then its window is stated as the first and last session carrying it

Scenario: A figure spanning an edit cannot be computed
  Given a request for adherence across two different rule-set versions
  When it is evaluated
  Then it is refused rather than averaged
```

**Edge cases:** a measured 6 versions in 32 days across a measured 25 sessions in one repository means versions are short-lived and small-sampled, so sample size renders alongside every figure; whether block ordering is stable across sessions was **not measured** (PRD Part 8 Q4), so the version hash must be order-insensitive until that is settled.

---

### S-21 — Tool vocabulary and derived roles (FR-29, FR-30)

**As** a downstream story builder — and ultimately the operator, whose rules name tools their agent does not have — **I want** the tool vocabulary and each tool's role derived from the logs, **so that** nothing in the checking layer has to name a tool.

**Implements:** FR-29, FR-30
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-49.
**Deps:** S-49
**Blocks:** S-23, S-26, S-31

```gherkin
Scenario: The vocabulary comes from the corpus
  Given an ingested corpus
  When the tool vocabulary is built
  Then it contains exactly the tools observed, and no tool named in source code

Scenario: Roles are derived from argument shapes
  Given the observed tools and their arguments
  When roles are derived
  Then a tool taking a path but no pattern is classified as file-reading
  And a tool taking a pattern is classified as searching
  And a tool taking replacement or file text is classified as writing

Scenario: All five derived roles are produced
  Given the observed tools and their arguments
  When roles are derived
  Then file-read, search, file-write, shell and spawn are each populated

Scenario: Each role names its dominant tool
  Given a derived role with several tools in it
  When the role is read
  Then it reports which tool carries the most calls, with that count

Scenario: An unclassifiable tool is recorded, not guessed
  Given a tool whose arguments match no known shape
  When roles are derived
  Then it is recorded as unclassified rather than assigned a role
```

**Edge cases:** a measured 61 distinct tools exist in the reference corpus, and role derivation must be re-run per corpus rather than cached as a constant, since the whole point is that the next machine has different tools; the dominant tool in a role matters more than the count of tools in it, because S-26 depends on knowing which tool actually does the job.

---

### S-22 — The Rules Inventory (FR-40)

**As** the operator, **I want** every extracted statement listed with a status and an in-force window, **so that** an empty violation count can never be mistaken for compliance.

**Implements:** FR-40
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-19, S-20, S-50.
**Deps:** S-19, S-20, S-50
**Blocks:** none

```gherkin
Scenario: Every statement carries exactly one status
  Given the extracted statements
  When the inventory renders
  Then each carries one of Watched, Checkable not yet built, Not checkable with a reason, or Not a rule

Scenario: Each row carries its origin and its reach
  Given the inventory
  When a statement is read
  Then it shows the source file it came from and the sessions carrying it

Scenario: The in-force window is stated
  Given a statement present across several sessions
  When it renders
  Then its first and last in-force dates are shown

Scenario: A repository with no rules is a designed state
  Given a repository whose sessions carry no instruction blocks
  When the inventory renders
  Then it states that no rules were found, rather than rendering an empty table

Scenario: A retired rule stays visible
  Given a rule absent from the most recent rule-set version
  When the inventory renders
  Then it is shown as retired with its adherence frozen at the date it was removed

Scenario: The inventory is scoped to a version
  Given a repository with several rule-set versions
  When the inventory renders
  Then it shows one version at a time and names which
```

**Edge cases:** measured on the reference corpus the four statuses split 4 / 9 / 9 / 21, so the largest bucket is "not a rule" and the surface must not make that look like failure; a measured 34 of 43 statements are absent from the most recent session, so the union-of-all-versions view the mockup shows is explicitly *not* what ships (PRD Part 4).

---

## Epic E5 — Adherence you can check

**Outcome:** An adherence figure that does not state its resolution is unfalsifiable, and the resolution choice moved one measured rule fivefold. This epic makes every figure checkable by the person reading it.

### S-23 — Four-layer operand resolution (FR-31, FR-32)

**As** a downstream story builder — and ultimately the operator, who would otherwise be shown a confident wrong number — **I want** a rule's operands resolved in confidence order with ties subtracted, **so that** the same words always resolve to the same tools and never to both sides at once.

**Implements:** FR-31, FR-32
**Priority:** Must Have · **Estimate:** L (estimate)
**Depends on:** contract S-19, S-21.
**Deps:** S-19, S-21
**Blocks:** S-24, S-25, S-26

```gherkin
Scenario: Layers are tried in confidence order
  Given a rule naming tools
  When its operands are resolved
  Then an exact tool name is preferred, then the logged MCP server field, then a derived role

Scenario: Server scope is a field match, not a string match
  Given a rule about one MCP server
  When its operands resolve
  Then tools from a different server whose names contain the same substring are excluded

Scenario: Operands are subtracted, with A winning ties
  Given a tool that both operands could claim
  When resolution completes
  Then it belongs to operand A only

Scenario: An unresolved operand becomes a finding
  Given a rule naming something that resolves to nothing
  When resolution completes
  Then it is reported as an unresolved-operand finding, never silently dropped
```

**Edge cases:** the subtraction defect is a known unfixed issue carried from discovery finding 8 and Phase C does not exit with it present (FR-32); a measured 28 tools resolve under the server-field layer, which is exactly the set a naive string match would get wrong.

---

### S-24 — Every figure ships with its resolution (FR-33)

**As** the operator, **I want** to see how a percentage was computed next to the percentage, **so that** I can reject a number whose mapping is wrong instead of acting on it.

**Implements:** FR-33
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-20, S-50 · implementation S-23.
**Deps:** S-20, S-23, S-50
**Blocks:** S-35

```gherkin
Scenario: The resolution renders beside the figure
  Given any adherence figure
  When it is displayed
  Then the layer used per operand and the resulting call counts are shown with it

Scenario: The API refuses to serve a bare figure
  Given a request for an adherence figure
  When the response is assembled
  Then a figure without its resolution and rule version cannot be returned
```

**Edge cases:** this is the guard against the measured fivefold spread on one rule from resolution choice alone; PRD §5.5 tolerates zero occurrences, so the refusal belongs in the response contract rather than in the UI, where a second client could bypass it.

---

### S-25 — The check-shape catalogue names nothing (FR-34)

**As** the operator, **whose rules must never be hard-coded**, **I want** checks parameterised from my rule text, **so that** a repository whose rules this product's author never saw still gets checked.

**Implements:** FR-34
**Priority:** Must Have · **Estimate:** L (estimate)
**Depends on:** contract S-19 · implementation S-23.
**Deps:** S-19, S-23
**Blocks:** none

```gherkin
Scenario: A shape matches a rule it was never written for
  Given a rule statement from a repository outside the reference corpus
  When it is matched against the catalogue
  Then it is matched on shape alone, with its operands taken from its own text

Scenario: The checking project names nothing
  Given the rules project's source
  When it is inspected
  Then no tool name, MCP server name or repository name appears in it

Scenario: A rule matching no shape is not silently dropped
  Given a statement that matches no shape
  When matching completes
  Then it is recorded for the inventory as checkable-not-built or not-checkable, with a reason
```

**Edge cases:** this is the invariant the operator called non-negotiable, and it is enforced structurally by living in its own project (PRD §3.1) so a reviewer can verify it by reading one project; a measured 8 rules became checkable with nothing hard-coded across five shapes, which is the floor this must at least reproduce.

---

### S-26 — "This rule names a tool your agent does not have" (FR-35)

**As** the operator, **I want** to be told when a rule names something my agent never calls, **so that** I fix the rule's vocabulary before I argue about its strength.

**Implements:** FR-35
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-21 · implementation S-23.
**Deps:** S-21, S-23
**Blocks:** none

```gherkin
Scenario: A rule naming a minor tool in a role is flagged
  Given a rule naming a tool that is not the dominant tool of the role it targets
  When the check runs
  Then it reports the named tool, the dominant tool, and both call counts

Scenario: A rule naming a non-existent tool is flagged
  Given a rule naming a tool that does not exist in this corpus
  When the check runs
  Then it reports that the rule names a tool the agent does not have

Scenario: The check runs regardless of adherence
  Given a rule with high measured adherence
  When checks run
  Then this check still runs and reports independently
```

**Edge cases:** measured on the navigation rule, the rule names a tool used a measured 129 times while the tool doing that job is used a measured 1,346 times and is never named — which is more actionable than any adherence percentage because the fix is one line; a rule naming several tools needs one finding per unresolved name, not one aggregate.

---

### S-27 — Conditional rules report base rates, labelled (FR-44)

**As** the operator, **I want** a conditional rule reported as a base rate and clearly not as a violation rate, **so that** I do not act on a number that never established the condition it depends on.

**Implements:** FR-44
**Priority:** Should Have · **Estimate:** S (estimate)
**Depends on:** contract S-19, S-49.
**Deps:** S-19, S-49
**Blocks:** none

```gherkin
Scenario: A conditional rule is labelled as a base rate
  Given a rule that applies only under a condition the logs cannot evaluate
  When its figure renders
  Then it is labelled a base rate
  And the unevaluated condition is stated

Scenario: A base rate is never ranked as a violation
  Given findings ranked by sessions affected
  When a base-rate item appears
  Then it is visually distinct from measured violations
```

**Edge cases:** the parallel-tool-calling rule measured a 43.6% single-call rate across a measured 7,449 tool-issuing messages, and whether a second independent call was available at each point was never measured — presenting that as disobedience is the exact failure PRD §3.9 lists.

---

## Epic E7 — The loop closes

**Outcome:** Every competitor-shaped thing in this space is a viewer. This epic is the Monitor step — the product's spine — plus the surface that ranks what recurs and the surface that makes silence legible.

### S-35 — The Monitor comparison (FR-39)

**As** the operator, **who has been editing rules blind for months**, **I want** adherence measured either side of a rule edit, **so that** I finally know whether a change I made worked.

**Implements:** FR-39
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-20 · implementation S-24.
**Deps:** S-20, S-24
**Blocks:** none

```gherkin
Scenario: Two adjacent versions are compared under one resolution
  Given two adjacent rule-set versions of the same repository
  When the comparison runs
  Then adherence is reported for each under a single stated resolution

Scenario: Sample sizes render as prominently as the percentages
  Given a comparison built on few sessions per side
  When it renders
  Then the session count on each side is as visible as the percentage

Scenario: Non-adjacent versions are refused
  Given two rule-set versions with another version between them
  When a comparison is requested
  Then it is refused, naming the intervening version
  And no averaged figure is offered

Scenario: The comparison reproduces from history
  Given the reference corpus
  When the comparison runs across its known rule edit
  Then it reproduces the measured before-and-after figures without needing new sessions
```

**Edge cases:** the reference edit measured 41.8% before against 71.7% after on a measured 3 and 4 sessions, which demonstrates the method rather than settling that edit — so the surface must not let a two-number story overwhelm a two-session sample; non-adjacent versions must be refused, since the intervening versions would be invisible.

---

### S-36 — The Process Digest: ranking and masthead (FR-41, part 1 of 2)

**As** the operator, **deciding what to change about my setup**, **I want** every recurring problem ranked by how many of my sessions it touched, **so that** I spend my editing effort where it pays rather than on whichever session annoyed me most recently.

**Implements:** FR-41 (ranking, corpus masthead)
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-44, S-50. It renders whatever findings exist, so it needs no individual finding story to be complete.
**Deps:** S-44, S-50
**Blocks:** S-39, S-42, S-54

```gherkin
Scenario: Ranking is by sessions affected
  Given findings across the corpus
  When the digest renders
  Then they are ordered by how many sessions each touched, not by severity or recency

Scenario: The corpus masthead states scope
  Given the digest
  When it renders
  Then it shows sessions, span, repositories, events, tool calls and rule coverage

Scenario: An empty store is a designed state
  Given a store with no findings
  When the digest renders
  Then it states that nothing has been analysed yet, distinctly from finding nothing

Scenario: A session still being ingested is a designed state
  Given a store mid-ingest
  When the digest renders
  Then it states that analysis is incomplete rather than showing partial counts as final

Scenario: Rule coverage is honest before rules are analysed
  Given a release in which no rules have been extracted
  When the masthead renders
  Then rule coverage reads "rules not yet analysed", never zero violations

Scenario: The masthead reads stored counters, not live counts
  Given a corpus at the design target size
  When the masthead renders
  Then its totals come from counters maintained at ingest
  And rendering it runs no aggregate scan over the event table
```

**Edge cases:** a finding touching one session is an anecdote and must be visually subordinate to one touching thirty, which is the ranking's entire purpose; the rule-coverage bar cannot be populated until Release 2 (FR-26, FR-40), which is why its Release 1 state is a stated requirement rather than an omission. **The masthead's totals are the one place this surface could scan the corpus**, and measurement says it must not: counting a million rows measured 126 ms on SQLite and 118 ms on Postgres, so this is not an engine problem and cannot be tuned away (`docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md`).

---

### S-54 — The Process Digest: row expansion, recurrence and repository scope (FR-41, part 2 of 2)

**As** the operator, **I want** to open a finding and see exactly which sessions it touched and what the evidence was, **so that** I can judge it rather than take the ranking on trust.

**Implements:** FR-41 (row expansion, recurrence strip, repository selector)
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** implementation S-36.
**Deps:** S-36
**Blocks:** none

```gherkin
Scenario: Every row carries its evidence and provenance
  Given any digest row
  When it is expanded
  Then it shows the evidence quoting the actual event fields, its provenance badge, and a suggestion

Scenario: The recurrence strip names which sessions
  Given a finding touching several sessions
  When its row renders
  Then a recurrence strip shows which sessions it touched, not only how many

Scenario: The digest defaults to one repository
  Given a store holding more than one repository
  When the digest renders
  Then it shows one repository at a time, selectable, per PRD Part 8 Q5

Scenario: A finding with no suggestion expands anyway
  Given a finding whose class has no suggestion template
  When its row is expanded
  Then it shows its evidence and states that no suggestion is offered
```

**Edge cases:** PRD Part 8 Q5 is decided — default to one repository, selectable — so this story implements that default and keeps the selector as the seam for a later cross-repository view; the no-suggestion case is FR-56's stated fallback and must not render as a blank suggestion area.

---

### S-37 — Checks that found nothing (FR-42)

**As** the operator, **I want** to see what was checked and came back clean, with the denominator, **so that** I can tell "clean" from "never looked".

**Implements:** FR-42
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-44, S-50.
**Deps:** S-44, S-50
**Blocks:** none

```gherkin
Scenario: A silent check states its denominator
  Given a check that produced no findings
  When the surface renders
  Then it states what was checked and over how many sessions or lines

Scenario: Every check registered in this release appears with its denominator
  Given a completed analysis run
  When the surface renders
  Then every check in the registry appears with the population it was run over
  And in Release 1 that includes the subagent-spawn resolution check and the malformed-line check

Scenario: A check that does not exist yet is not implied
  Given a release in which the contradiction check has not been built
  When the surface renders
  Then it is absent rather than shown as clean
  And it joins the surface in Release 2 with S-38

Scenario: A check that never ran is not shown as clean
  Given a rule whose check was refused
  When the surfaces render
  Then it appears as not checkable in the inventory, not as a clean silent check
```

**Edge cases:** the distinction between "ran and found nothing" and "never ran" is the whole point of this surface, and conflating them is the failure PRD §3.9 names as silence reading as compliance.

---

### S-38 — The contradiction check, shipped as Inferred (FR-43)

**As** the operator, **I want** to be told when two of my own rules conflict inside one rule set, **so that** I can resolve it before the agent has to.

**Implements:** FR-43
**Priority:** Could Have · **Estimate:** M (estimate)
**Depends on:** contract S-19, S-20.
**Deps:** S-19, S-20
**Blocks:** none

```gherkin
Scenario: Comparison is pairwise and excludes self-matches
  Given a rule set
  When contradictions are sought
  Then each pair is compared once and no statement is compared against itself

Scenario: The check is scoped to one version
  Given several rule-set versions
  When the check runs
  Then it compares only statements in force together

Scenario: The result ships as Inferred in the silent-checks surface
  Given any contradiction result
  When it renders
  Then it is labelled Inferred and appears in the silent-checks surface
```

**Edge cases:** a keyword-polarity first pass returned a measured 4 candidates and all 4 were spurious — three matched a statement against itself, because a prohibition contains the phrase it prohibits — so self-match exclusion is the load-bearing requirement, not an optimisation; this can never be an Observed check.

---

### S-51 — Suggestions, as deterministic templates (FR-56)

**As** the operator, **I want** each finding to come with a concrete proposed fix, **so that** I can act on it or refuse it rather than translating a measurement into a rule edit myself.

**Implements:** FR-56
**Priority:** Must Have · **Estimate:** M (estimate)
**Depends on:** contract S-44.
**Deps:** S-44
**Blocks:** none

```gherkin
Scenario: A suggestion is a template bound to a check shape
  Given a finding produced by a check shape with a template
  When its suggestion renders
  Then the template is populated from the same operands and resolution the finding used

Scenario: The same finding always yields the same suggestion
  Given an unchanged store
  When suggestions are produced twice
  Then both runs produce identical suggestion text

Scenario: No template means no suggestion
  Given a finding whose class has no template
  When it renders
  Then it shows its evidence and no suggestion, rather than a generic one

Scenario: No model is called
  Given the suggestion code path
  When it is inspected
  Then it makes no model call and reads no clock
```

**Edge cases:** PRD §3.8 forbids a model call, which is why these are templates rather than generated text; FR-35 is the worked example to generalise from — *"name `rg`, `glob` and `view`"* — and a template that cannot name a concrete operand should produce no suggestion rather than a vague one, because §5.4 measures the rejection rate and a vague suggestion poisons that signal.

---

### S-39 — Recording what the operator does with a suggestion (FR-45)

**As** the operator, **I want** my acceptances and rejections recorded, **so that** the product can tell me when it is being obeyed where it does not matter and ignored where it does.

**Implements:** FR-45
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-44 · implementation S-36.
**Deps:** S-36, S-44
**Blocks:** none

```gherkin
Scenario: Every response is recorded
  Given a suggestion
  When the operator accepts, rejects or ignores it
  Then that outcome is stored against the finding and its provenance level

Scenario: The guardrail is computable
  Given recorded responses
  When the guardrail is computed
  Then it reports the rejection share and the share of acted-on findings that were Inferred
```

**Edge cases:** this is the input to the product's own failure detector (PRD §5.4) — a rising adherence curve alongside a rising rejection rate means the tool is being ignored where it matters, and without this story that signal cannot exist; changing a verdict later must be possible and must not lose the earlier one.

---

## Epic E8 — Missing capability

**Outcome:** The highest-value findings with the weakest provenance. Built last, rendered so they can never be mistaken for measurements.

### S-40 — Tool-failure clusters (FR-46)

**As** the operator, **I want** to see which tools fail often enough to be the real problem, **so that** I fix the server instead of tightening a rule that was never the cause.

**Implements:** FR-46
**Priority:** Should Have · **Estimate:** S (estimate)
**Depends on:** contract S-44 · implementation S-14.
**Deps:** S-14, S-44
**Blocks:** none

```gherkin
Scenario: Clusters are reported with counts and rates
  Given tools with recorded failures
  When clusters are computed
  Then each is reported with failures over calls and its rate

Scenario: A cluster near a mandated tool is cross-referenced
  Given a tool a rule mandates that also fails often
  When the finding renders
  Then it links to the adherence finding for that rule, labelled as a hypothesis
```

**Edge cases:** independence was checked and holds, but **only once the population was rescoped** — measured inside the sessions carrying one ban, 15 calls produced 1 failure; outside them, 38 calls produced 14 (FR-48). The earlier figure of 49/15 came from a substring match that pulled in a different MCP server's tool, which is the exact failure FR-31 layer 2 prevents — so this check must be re-runnable under a stated matching convention rather than asserted.

---

### S-42 — Inferred findings are visibly separate (FR-48)

**As** the operator, **I want** judgments rendered differently from measurements and never ranked beside them, **so that** I can tell what the product knows from what it thinks.

**Implements:** FR-48
**Priority:** Must Have · **Estimate:** S (estimate)
**Depends on:** contract S-44, S-50 · implementation S-36.
**Deps:** S-36, S-44, S-50
**Blocks:** none

```gherkin
Scenario: Inferred findings are not in the ranked list
  Given findings of mixed provenance
  When the digest renders
  Then Inferred findings appear in their own section, not interleaved by rank

Scenario: The three levels are visually distinguishable
  Given findings at each provenance level
  When they render
  Then Observed, Derived and Inferred are distinguishable without reading the label
```

**Edge cases:** distinguishable without colour alone, for accessibility; a hypothesis must read as a hypothesis in its text as well as its styling, since the styling does not survive being quoted elsewhere.

---

### S-43 — Subagent rule attribution, or nothing (FR-49)

**As** the operator, **I want** the product to admit it cannot know which rules a subagent ran under, **so that** the digest never tells me a subagent broke a rule it may never have been given.

**Implements:** FR-49
**Priority:** Should Have · **Estimate:** S (estimate)
**Depends on:** contract S-19, S-49.
**Deps:** S-19, S-49
**Blocks:** none

```gherkin
Scenario: Inheritance is labelled, never asserted
  Given a subagent
  When its rules are displayed
  Then any inherited rule set is labelled Inferred

Scenario: What is Observed is shown instead
  Given a subagent
  When its context is displayed
  Then its spawn description, its task prompt and its own skill invocations are shown as Observed
```

**Edge cases:** the system prompt carries no agent id, so a subagent's own rules are unrecoverable — this is a measured limit, not a gap to be closed later; showing nothing is an acceptable outcome and is preferable to a labelled guess in the digest's ranked list.

---

## Epic E6 — Content checks that don't lie *(GATED — not in v1)*

> **Entry condition:** at least one content-shaped rule appears in a Copilot repository's instruction
> files. Measured today: **0 of 43** (PRD §3.4.3). The measured 14 rules these stories were designed
> against came from the Claude Code corpus, which PRD Part 7 excludes and FR-26 forbids reaching.
> Nothing here is built until that changes; nothing here is discarded either — the measurement and the
> design behind it stand ready.

**Outcome:** Rules about the code the agent writes become checkable without generating violations that
do not exist. Unscoped, a measured 2 of 3 real rules were wholly false-positive.

### S-28 — Write units, with added content separated (FR-36)

**As** a downstream story builder — and ultimately the operator, who would otherwise be shown violations in code the agent never wrote — **I want** every write split per file with agent-added content separated from content carried along, **so that** a content check searches only what the agent actually typed.

**Implements:** FR-36
**Priority:** Gated (Phase E) · **Estimate:** M (estimate)
**Depends on:** contract S-01 · implementation S-03.
**Deps:** S-01, S-03
**Blocks:** S-29

```gherkin
Scenario: A patch is split per file with added lines identified
  Given a patch envelope touching several files
  When write units are extracted
  Then each file becomes its own unit
  And lines the patch adds are distinguished from context and removed lines

Scenario: An edit separates new text from replaced text
  Given an edit call
  When its write unit is extracted
  Then the replacement text is added content and the replaced text is pre-existing

Scenario: Every write unit carries a usable path
  Given the frozen fixture corpus
  When write units are extracted
  Then all of them carry a file path
```

**Edge cases:** measured 673 write operations become a measured 842 write units across a measured 380 files, and a measured 842 of 842 carry a usable path, so a pathless unit is a parser defect; reading added content only removes a measured 21.0% of hits on its own (FR-36), which is real but secondary to S-29's filters.

---

### S-29 — Scope-independent filters, ordered first (FR-50)

**As** the operator, **I want** the obvious junk removed before anything clever happens, **so that** I am not shown a rule matching its own text in a design document.

**Implements:** FR-50
**Priority:** Gated (Phase E) · **Estimate:** S (estimate)
**Depends on:** implementation S-28.
**Deps:** S-28
**Blocks:** S-31, S-32

```gherkin
Scenario: Documentation files are excluded before scope resolution
  Given content hits in markdown and other documentation files
  When a content check runs
  Then those hits are removed before any scope is resolved

Scenario: External-URL lines are excluded
  Given a hit on a line carrying an absolute external URL
  When the check runs
  Then that hit is removed

Scenario: Ordering is enforced, not conventional
  Given the check pipeline
  When its stages are inspected
  Then filters provably run before scope resolution
```

**Edge cases:** ordering is load-bearing — measured on one rule, four scope mechanisms disagree 29 against 0 on raw content and agree at 0 once the documentation filter runs, because a measured 29 of 29 outlier hits were markdown design documents carrying example code (FR-50); the documentation filter alone removes a measured 51.3% of surviving hits, making it the single largest source of false positives.

---

### S-30 — Parsing a rule into subject, alternative and banned (FR-51)

**As** a downstream story builder — and ultimately the operator, whose preferred wrapper would otherwise be mistaken for a scope — **I want** each rule statement parsed into its three roles by position, **so that** the check knows what it is searching for and where.

**Implements:** FR-51
**Priority:** Gated (Phase E) · **Estimate:** M (estimate)
**Depends on:** contract S-19.
**Deps:** S-19
**Blocks:** S-31

```gherkin
Scenario: The three roles are identified by position
  Given a rule statement naming several symbols
  When it is parsed
  Then the leading symbol is the subject, the symbol following a preference word is the alternative, and the symbol following a negation is banned

Scenario: A statement with no subject yields no scope
  Given a rule whose only symbols are an alternative and a banned symbol
  When it is parsed
  Then it is recorded as having no scope
  And the alternative is never treated as one

Scenario: A prose statement is recorded as unparsed, not guessed at
  Given a rule written as prose rather than as a list item
  When it is parsed
  Then it is recorded as unparsed rather than assigned roles by position
```

**Edge cases:** the measured failure case is a rule preferring a wrapper over a raw call, where treating the wrapper as a scope is wrong and reporting the rule as not checkable is right (FR-51); whether this parse survives rules written as prose was **not tested** — every statement examined was a list item — which is why the third scenario exists.

---

### S-31 — Four-layer scope resolution with precedence (FR-52, FR-53)

**As** the operator, **I want** a rule's territory resolved the same way its operands are, **so that** a check either knows where it applies or admits it does not.

**Implements:** FR-52, FR-53
**Priority:** Gated (Phase E) · **Estimate:** L (estimate)
**Depends on:** contract S-21 · implementation S-29, S-30.
**Deps:** S-21, S-29, S-30
**Blocks:** S-32, S-33, S-34

```gherkin
Scenario: A subject symbol wins over every other mechanism
  Given a rule whose subject is a code symbol
  When its scope resolves
  Then scope is co-occurrence with that symbol
  And path-based mechanisms are not consulted

Scenario: The co-occurrence window is an explicit parameter
  Given layer 1 resolution
  When a scope is produced
  Then the window used — same line, same declaration or same file — is recorded with it

Scenario: A category word resolves against corpus conventions
  Given a rule naming a category of file
  When its scope resolves
  Then the naming convention is derived from the corpus, never from a hard-coded map

Scenario: An unresolvable scope produces a not-checkable rule
  Given a rule whose scope has no observable correlate
  When resolution completes
  Then the rule is listed as not checkable with its reason
  And no check is run for it
```

**Edge cases:** precedence is load-bearing — a measured 9 false hits came from consulting path-scoping for a rule whose subject symbol had already answered the question, and entity-scoping returns a measured 0 on that rule, which adjudication confirms is correct. **This story owns the largest unresolved risk in the product** (PRD Part 8 Q1 residual): whether layer 2 generalises past one category word is unknown, and only S-34's fixture will tell.

---

### S-32 — Two refusals: unscoped and ambiguous (FR-37)

**As** the operator, **I want** a check to refuse rather than guess, **so that** silence costs me nothing and a wrong answer never reaches a process change.

**Implements:** FR-37
**Priority:** Gated (Phase E) · **Estimate:** M (estimate)
**Depends on:** contract S-44 · implementation S-29, S-31.
**Deps:** S-29, S-31, S-44
**Blocks:** S-34

```gherkin
Scenario: An unscoped content check is refused
  Given a content rule with no resolvable scope
  When checks run
  Then no check runs for it, and the refusal is reported

Scenario: An ambiguous scope is refused, not resolved by preference
  Given a rule where two plausible scope mechanisms disagree after filtering
  When checks run
  Then the finding is suppressed
  And a scope-ambiguous item is reported naming the mechanisms and their counts

Scenario: The agreement test reports its own firing rate
  Given a completed run
  When the check registry is read
  Then the agreement test reports how often it fired, as a health signal

Scenario: Refusal is a first-class report, not a log line
  Given any refusal
  When the surfaces render
  Then it appears in the Rules Inventory or the silent-checks surface, visible to the operator
```

**Edge cases:** a backstop firing constantly means the resolver above it is broken (FR-54), which is why the firing rate is a rendered figure rather than a log; a check refused for ambiguity must be distinguishable in the inventory from one refused for having no scope at all, because the fixes differ.

---

### S-33 — Named false-positive causes on the finding (FR-38)

**As** the operator, **I want** a content-check hit to tell me the ways it might be wrong, **so that** I adjudicate quickly instead of rediscovering the same three traps.

**Implements:** FR-38
**Priority:** Gated (Phase E) · **Estimate:** S (estimate)
**Depends on:** contract S-44 · implementation S-31.
**Deps:** S-31, S-44
**Blocks:** none

```gherkin
Scenario: Every content hit is labelled Derived and unconfirmed
  Given any content-check hit
  When it renders
  Then it is labelled Derived and unconfirmed

Scenario: The known causes are named on the finding
  Given a content-check finding
  When it renders
  Then it names the wrapper's-own-implementation, sibling-entity and untouched-code causes

Scenario: Untouched code is stated as invisible
  Given any content-check finding
  When it renders
  Then it states that a rule broken outside what the agent edited cannot be seen by this method
```

**Edge cases:** measured, a majority of the surviving hits on the one rule that passed scoping are the wrapper's own implementation, and all of the path-scoped sibling-entity hits on another were false — so these are not hypothetical traps but the two that actually fired.

---

### S-34 — The labelled fixture (FR-54)

**As** the operator, **I want** the scope resolver measured against hits I have judged myself, **so that** its correctness is a test result rather than an argument.

**Implements:** FR-54
**Priority:** Gated (Phase E) · **Estimate:** M (estimate)
**Depends on:** implementation S-31, S-32. Requires one adjudication sitting with the operator, which PRD Part 6 lists as a dependency.
**Deps:** S-31, S-32
**Blocks:** none

```gherkin
Scenario: Surviving hits are presented for adjudication
  Given the testable rules run through filters and scope resolution
  When adjudication starts
  Then every surviving hit is presented with its file, its line and the rule it came from

Scenario: Verdicts are frozen as a fixture
  Given adjudicated hits
  When the fixture is written
  Then each carries real, false positive, or cannot tell
  And each carries a confidence and a one-line reason

Scenario: The seed alone is a valid gate
  Given only the seed verdicts and no further adjudication
  When the fixture runs
  Then it gates resolver changes, so the resolver has a regression net before the full sitting happens

Scenario: The fixture gates resolver changes
  Given a change to any scope layer
  When the fixture runs
  Then a regression against a previously-real or previously-false verdict fails the build
```

**Edge cases:** a seed of a measured 11 already-adjudicated hits exists in the FP measurement and is loaded first rather than re-judged; "cannot tell" is a first-class verdict and must not be coerced to either side. **Stated limitation:** the ground truth is labelled once, by the operator, who also wrote the rules being checked and chose the resolver design — no second adjudicator, no blind labelling. That is why verdicts carry a confidence and a reason (FR-54).

---

## FR coverage

Every requirement in PRD §3.6 is owned. Two are **split across parts**, and the `Implements:` lines
say which part: **FR-21** across S-08 / S-52 / S-53 (masthead+tape, inspector+chips, scale+states) and
**FR-41** across S-36 / S-54 (ranking+masthead, expansion+recurrence). Everything else is owned by
exactly one story. Gated rows are Phase E, not in v1.

| Story | Implements |
|---|---|
| S-01 | FR-2, FR-11 |
| S-02 | FR-1, FR-3, FR-6 |
| S-03 | FR-4 |
| S-04 | FR-5 |
| S-05 | FR-7, FR-14 |
| S-06 | FR-8, FR-9 |
| S-07 | FR-10, FR-12, FR-13 |
| S-08 | FR-21 (masthead, tape) |
| S-09 | FR-22 |
| S-10 | FR-23 |
| S-11 | FR-24 |
| S-12 | FR-25 |
| S-13 | FR-15 |
| S-14 | FR-16 |
| S-15 | FR-17 |
| S-16 | FR-18 |
| S-17 | FR-19 |
| S-18 | FR-20 |
| S-19 | FR-26 |
| S-20 | FR-27, FR-28 |
| S-21 | FR-29, FR-30 |
| S-22 | FR-40 |
| S-23 | FR-31, FR-32 |
| S-24 | FR-33 |
| S-25 | FR-34 |
| S-26 | FR-35 |
| S-27 | FR-44 |
| S-28 *(gated)* | FR-36 |
| S-29 *(gated)* | FR-50 |
| S-30 *(gated)* | FR-51 |
| S-31 *(gated)* | FR-52, FR-53 |
| S-32 *(gated)* | FR-37 |
| S-33 *(gated)* | FR-38 |
| S-34 *(gated)* | FR-54 |
| S-35 | FR-39 |
| S-36 | FR-41 (ranking, corpus masthead) |
| S-37 | FR-42 |
| S-38 | FR-43 |
| S-39 | FR-45 |
| S-40 | FR-46 |
| S-42 | FR-48 |
| S-43 | FR-49 |
| S-44 | FR-57, PRD §3.2 FINDINGS layer, §3.8 "provenance is structural" |
| S-45 | FR-55, PRD §3.5 Phase A exit criterion, §3.7 scale targets |
| S-46 | PRD §3.2 "fully re-derivable from RAW", §3.8 rebuildable and determinism |
| S-47 | FR-58, PRD §3.1 |
| S-48 | PRD §3.1 (the host and the web shell) |
| S-49 | PRD §3.2 NORMALIZED layer |
| S-50 | FR-59 |
| S-51 | FR-56 |
| S-52 | FR-21 (inspector, tabs, chips) |
| S-53 | FR-21 (virtualisation, keyboard, empty/loading/error states) |
| S-54 | FR-41 (row expansion, recurrence strip, repository selector) |

## Parallelism plan

Computed from the `Deps:` lines, excluding gated E6. <!--src: derived from the Deps lines, not asserted--> **46 v1 stories, critical path 8 deep, widest wave 12.** Recompute whenever a `Deps:` line changes — this is derived, not asserted.

| Wave | Stories |
|---|---|
| 0 | S-47 |
| 1 | S-01 |
| 2 | S-03 S-07 S-44 S-49 |
| 3 | S-02 S-11 S-13 S-14 S-15 S-16 S-17 S-18 S-21 S-46 S-50 S-51 |
| 4 | S-04 S-06 S-19 S-36 S-37 S-40 S-48 |
| 5 | S-05 S-08 S-20 S-23 S-27 S-39 S-42 S-43 S-45 S-54 |
| 6 | S-12 S-22 S-24 S-25 S-26 S-38 S-52 S-53 |
| 7 | S-09 S-10 S-35 |

**Critical path:** `S-47 → S-01 → S-44 → S-50 → S-48 → S-08 → S-52 → S-09`

**Most-constraining stories** — what everything else waits on:

| Story | Blocks | What it publishes |
|---|---|---|
| S-44 | 17 | the finding record and the check registry | <!--src: measured from the Deps graph-->
| S-49 | 15 | the NORMALIZED entity shapes | <!--src: measured from the Deps graph-->
| S-50 | 7 | the API response envelope | <!--src: measured from the Deps graph-->
| S-19 | 7 | extracted rule statements | <!--src: measured from the Deps graph-->
| S-01 | 6 | the RAW row shape | <!--src: measured from the Deps graph-->

The two widest fans are **contracts, not implementations**, which is the point: a contract can be published on day one and built against while its own tests are still red.

## Releases

This section is authoritative for scope, and every release is **dependency-closed** — no story depends on anything in a later release. Verified, not asserted.

**Release 1 — prove the loop** — 26 stories <!--src: count of the list below-->

> S-01 S-02 S-03 S-04 S-05 S-06 S-07 S-08 S-13 S-14 S-15 S-36 S-37 S-39 S-42 S-44 S-45 S-46 S-47 S-48 S-49 S-50 S-51 S-52 S-53 S-54

Delivers PRD §5.1 **for the waste class**: a session count and evidence on every finding. It does **not** deliver the per-rule-version trend or the rule-coverage bar, which need FR-26, FR-27 and FR-40 in Release 2 — the digest states that coverage honestly rather than showing zero. Its real job is to test this plan's largest unvalidated assumption, that the operator acts on a digest (PRD Part 6), before the expensive epics are built.

**Release 2 — close the loop** — 18 stories <!--src: count of the list below-->

> S-09 S-10 S-11 S-12 S-16 S-17 S-18 S-19 S-20 S-21 S-22 S-23 S-24 S-25 S-26 S-27 S-35 S-38

Delivers PRD §5.2, and completes §5.1 — the per-rule-version trend and the coverage bar arrive here.

**Release 3 — judgment** — 2 stories <!--src: count of the list below-->

> S-40 S-43

Inferred findings, rendered so they can never be mistaken for measurements.

**Two deliberate deviations from the PRD's phase table**, stated rather than left to be spotted: **S-42** (FR-48, provenance separation) is PRD Phase D but ships in Release 1, because the rule must hold the moment any Inferred finding exists; and **S-51** (FR-56, suggestions) is PRD Phase C but ships in Release 1, because FR-41 requires a refusable suggestion on every digest row.

**Not scheduled — Epic E6 (Phase E).** Entry condition: at least one content-shaped rule appears in a Copilot repository's instruction files, a measured 0 of 43 today.

## INVEST validation

Stories that would fail a strict reading, and why they are kept anyway:

| Story | Fails | Kept because |
|---|---|---|
| S-44 | V — no operator-visible value alone | it publishes the finding shape and the check registry, and its provenance-cannot-be-omitted criterion changes every downstream story. The alternative is each finding story inventing its own record |
| S-49 | V — same | the same argument for the NORMALIZED shapes. It is the one change that takes the widest fan in the plan off an implementation story |
| S-50 | V — same | it is where FR-33's refusal becomes unrepresentable rather than merely tested |
| S-45 | I — measures finished pipelines by construction | it produces a reviewable artifact — a frozen fixture corpus and a census — that Phase A's exit criterion is stated against |
| S-46 | I — same | the rebuild-and-compare harness is real work with a real artifact. Its two cross-cutting invariants are deliberately **not** in the story; they are in the Definition of Done, because they must hold for checks that do not exist yet |
| S-12 | S — task-sized | a thin vertical slice is acceptable. It was a candidate to fold into the recorder, but S-08 is now split three ways and folding it in would undo that |

## Definition of Ready

A story enters a sprint only when all of these are true:

- [ ] The story follows the persona / goal / benefit format and names a real beneficiary
- [ ] INVEST is satisfied, or the story appears in the INVEST validation table with its reason
- [ ] Acceptance criteria are in Gherkin and each scenario is independently testable
- [ ] **For UI stories: empty, loading, error and refusal states are covered in the acceptance criteria** — not in edge-case prose
- [ ] Its FR numbers are listed and trace to the PRD
- [ ] Every figure it quotes carries a citation or an explicit estimate label
- [ ] Its `Deps:` contracts are committed; implementation dependencies are scheduled ahead of it
- [ ] Any PRD gap the story names has an owner for the decision
- [ ] It fits in one sprint; if not, it is split before it starts

## Definition of Done

These hold at merge for **every** story, including ones written later. They are not inside any single
story because they must constrain code that does not exist yet:

- [ ] No outbound transport anywhere in the product — no HTTP client, socket or remote call
- [ ] No check reads the wall clock, samples randomly, or calls a model; all temporal ordering derives from event timestamps
- [ ] No project references an AecoLedger assembly, and no project reference resolves outside this repository
- [ ] Every finding the change can produce carries a provenance level; every adherence figure carries its resolution and rule version
- [ ] The product never writes to `~/.copilot/`
- [ ] Findings remain re-derivable from RAW alone (checkable by running S-46's harness, which is why S-46 is a story and these are not)

## Open items carried from the PRD

Open questions, not blockers, each attached to the story that will feel it first:

| Open item | Felt first by |
|---|---|
| Whether scope layer 2 generalises past one category word (PRD Part 8 Q1 residual) | S-31 *(gated)*, measured by S-34 *(gated)* |
| Whether instruction-block ordering is stable across sessions (PRD Part 8 Q4) | S-20 |
| Whether the positional rule parse survives prose statements | S-30 *(gated)* |
| Whether any corpus session is itself an analysis session | S-05 |
| Whether PRD §3.7's design targets hold — S-45 tests only the reference corpus | S-45 |
