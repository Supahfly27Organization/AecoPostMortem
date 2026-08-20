# Reference corpus fixture

`corpus-manifest.json` freezes the reference Copilot corpus as **hashes plus a
post-exclusion census**, so that every Phase A exit criterion can be stated
against a fixed target instead of a rotating one (FR-55, story S-45).

Frozen **2026-08-16** from `~/.copilot/session-state` on the reference machine.

## Why the bytes are not here

The session files hold the operator's source code, prompts and possibly secrets
(PRD §3.8), and 176.7 MiB of them would make the repository unwieldy. The
manifest records, per session, the file's SHA-256, its size, its line and
malformed-line counts, its CLI and event-schema versions, its first and last
event timestamps, its cwd and repository, and its full event census. That is
enough to detect any change without the bytes ever being committed.

## Using it

```sh
python scripts/freeze-corpus-manifest.py            # re-freeze (deliberate act)
python scripts/freeze-corpus-manifest.py --check    # verify disk; exit 1 on drift
python scripts/check-apply-patch-roundtrip.py       # FR-4: apply_patch round-trips (S-03)
python scripts/check-corpus-verification.py         # FR-55: Phase A exit criterion (S-45, issue #9)
```

`--check` compares hashes and per-session censuses only; the freeze date is
metadata, not evidence. A session that has rotated away reports as a difference
rather than being absorbed — that is the point of the file, not a failure of it.

`check-corpus-verification.py` runs a real full ingest and a real incremental re-ingest of this
manifest's own `source` directory, and checks: the RAW event census matches `totals.event_census`
exactly, every RAW row re-serialises byte-identically to its source line, and both runs finish
inside PRD §3.7's time targets (3 minutes full, 15 seconds incremental — targets, not measurements,
so a miss is a conversation about the target rather than a silently absorbed failure). Like the
other corpus-shaped checks here, it reads the live directory this manifest's `source` field names
and skips rather than fails when that directory is not present on the machine running it.

## What the freeze measured

| Property | Frozen value |
|---|---|
| Session directories | 47 |
| …without an `events.jsonl` | 12 |
| Sessions found | 35 |
| Sessions excluded by cwd (FR-7) | **0** |
| Sessions included | 35 |
| Event lines | **56,138** |
| Malformed lines | 0 |
| Bytes | 185,235,761 (176.7 MiB) |
| Distinct event types | 31 |
| Event span | 2026-04-20T14:57:50.942Z → 2026-08-09T20:14:36.758Z |
| Distinct `copilotVersion` values | 14 |

## Two corrections to the data map, recorded rather than silently applied

Following the evidence discipline the discovery documents already use
(`docs/product-superpowers/discovery/2026-08-16-copilot-ingestion-data-map.md`
Part 8 records its own corrections the same way), both are recorded here and
**not** overwritten in the documents that carry the older figures.

### 1. Event lines are 56,138, not 56,176

The data map's Part 1 stated "Total event lines parsed | 56 176". Its own Part 3
census table sums to **56,138**, and the live corpus matches that table
**exactly, on all 31 event types, with zero per-type deltas** — verified by
extracting the table from the document mechanically rather than by
transcription. The 2026-08-13 discovery independently reported 56,138 for the
same corpus.

So Part 1's figure is 38 high relative to the table it heads; the table and the
corpus agree. Neither run recorded how the total was derived, which is the same
defect FR-33 exists to prevent, one document upstream.

**Corrected to 56,138 on 2026-08-16**, with the change logged in the PRD's own
Appendix rather than made silently:

- the PRD, six occurrences — Part 1 evidence base, Part 2's silent-checks
  paragraph, §3.2, FR-6, FR-42 and §3.7's scale table
- the digest mockup — masthead, clean-checks card, and footer note
- the data map, two occurrences — Part 1's corpus table (the origin of the
  figure) and Part 5's RAW-layer row, with the correction logged in that
  document's own self-review

FR-42's surface exists specifically to state correct denominators, which is why
it was worth correcting before it ships.

- the approved discovery, two mentions — its corpus note and its opportunity
  assessment, with the correction logged in its self-review

No document in the set now carries 56,176 except where it is quoted as the
superseded figure.

The data map's Part 1 **directory count stays at 48** and is not an error: the
corpus now holds 47, the lost directory carried no `events.jsonl`, and the
35-session census is unchanged. Overwriting a figure that was true when measured
would erase the evidence of rotation rather than record it — which is the whole
argument for freezing a fixture.

**The stories document needed no change** — it never states the count, referring
instead to "the number of lines parsed" (S-02) and naming the malformed-line
check without a denominator (S-37). Writing the criterion abstractly is what
kept it correct.

### 2. Session directories are 47, not 48

The data map measured 48; disk holds 47. The lost directory carried no
`events.jsonl`, so the 35-session census is unaffected. This is the rotating
window doing exactly what §3.5 says it does — within the same day the
measurement was taken.

## FR-7's first run, and what it does and does not show

FR-7 excludes this product's own analysis sessions at ingest, keyed on
`session.start.data.context.cwd` against an operator-configured list defaulting
to this repository's root. On this corpus that list excludes **0 sessions**,
because this repository was created after every session in it.

That is not evidence the corpus is uncontaminated. The recorded cwds are:

| Sessions | cwd |
|---|---|
| 20 | `F:\git\UpFront` |
| 7 | `F:\git\upfront` |
| 2 | `C:\Users\david` |
| 1 each | two `.worktrees` paths, `F:\git\UpFront\.claude`, `C:\Users\david\AppData\Local\Temp`, `C:\Users\david\Downloads\upfront-website-v2`, `F:\git\aeco-test2` |

The sessions that produced this specification set ran under `F:\git\UpFront`,
alongside ordinary feature work in the same directory. So cwd alone cannot
separate them here — which is precisely the failure FR-7's own text anticipates:
"a path match alone would exclude ordinary feature work done *inside* this
repository, which the operator wants measured, while missing an analysis session
run from anywhere else." From this repository onward the default works; for the
historical corpus it does not, and the exclusion list needs session-level
entries if that history is to be cleaned.

Whether any of those 35 sessions is an analysis session **remains unmeasured** —
recorded as still open, not closed by a run that excluded nothing.

## Not yet here

FR-55 also calls for "a small hand-picked set of redacted fixtures for parser
tests". Those are not in this directory yet; redaction is a judgment call over
real prompt and patch content and has not been made.
