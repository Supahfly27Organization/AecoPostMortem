# SQLite vs PostgreSQL on the read path — a measurement

**Date:** 2026-08-16 · **Status:** measurement record; the decision it informs is PRD §3.1
**Commissioned by:** the operator, asking which engine queries faster given that ingest may take
time but querying must be prompt.

> **What this is.** A query-latency benchmark over the real corpus, run through the drivers the
> product will actually use — `Microsoft.Data.Sqlite` and `Npgsql` on .NET 10 — against the query
> shapes the three surfaces issue.
>
> **What this is not.** Not an ingest benchmark, and not a cold-cache benchmark. Both engines were
> measured warm. The confounds are in the self-review, and one of them is material.

Harness: `bench/Program.cs`, runnable against any Copilot corpus. <!--src: harness configuration,
chosen not measured--> Medians of 15 runs after 3 warm-ups. Scale 1× is the frozen corpus
(`fixtures/corpus-manifest.json`); the larger scale duplicates it 18× <!--src: scale factor chosen
to reach the design target--> under distinct session ids, to reach the target in PRD §3.7.

---

## Part 1: What was measured

| Scale | Raw rows, measured | `tool_call` rows | SQLite file | Postgres relations |
|---|---|---|---|---|
| 1× (the frozen corpus) | 56,138 | 32,161 | 213 MiB | 132 MiB |
| 18× (§3.7 design target) | 1,010,484 | 578,898 | 3,853 MiB | 2,371 MiB |

Postgres stores the same data in **a measured 1.6× less space**, from TOAST compressing the verbatim
JSON payload. An earlier estimate of 4–6× was wrong and is corrected here.

Nine query shapes, chosen to match what the surfaces do rather than what is easy to benchmark:
digest ranking and its recurrence strip (precomputed FINDINGS reads), the corpus masthead, the
Flight Recorder tape both narrow and dragging the payload, a single raw-event fetch, and three
analytical aggregations — per-tool failure rates, repeated file reads, and adherence scoped to a
rule-set version's sessions.

## Part 2: Results at the design target, 1,010,484 rows

All medians, warm, with the covering indexes from Part 3 in place.

| Query | Rows | SQLite, measured | Postgres, measured | Faster |
|---|---|---|---|---|
| Q1 digest ranking | 20 | **0.05 ms** | 0.59 ms | SQLite 12.5× |
| Q2 recurrence strip | 620 | **0.51 ms** | 0.71 ms | SQLite 1.4× |
| Q3 masthead counts | 1 | 126.10 ms | **118.27 ms** | Postgres 1.1× |
| Q4 tape, narrow projection | 10,906 | 26.55 ms | **17.37 ms** | Postgres 1.5× |
| Q5 tape, dragging the payload | 10,906 | **59.80 ms** | 122.95 ms | SQLite 2.1× |
| Q6 raw tab, one row | 1 | **0.01 ms** | 0.50 ms | SQLite 43.4× |
| Q7 per-tool failure rates | 62 | 64.34 ms | **39.36 ms** | Postgres 1.6× |
| Q8 repeated file reads | 50 | 65.11 ms | **21.07 ms** | Postgres 3.1× |
| Q9 adherence, scoped to a version | 21 | 34.22 ms | **15.54 ms** | Postgres 2.2× |

At 1× the same shapes rank the same way, with every figure smaller; the only rank change is Q7,
where Postgres led by a measured 4.2× before the covering index existed.

### The split is structural, and it is legible

**SQLite wins whenever the round trip dominates.** Q6 is a measured 43.4× — 0.01 ms against 0.50 ms
— because an in-process query is a function call and Postgres pays a socket round trip it cannot
amortise on a single row. Q1 is the same effect at a measured 12.5×.

**SQLite wins large payload reads**, Q5 by a measured 2.1×, because 32 MiB of JSON never crosses a
socket or gets deserialised out of a wire format.

**Postgres wins every aggregation**, by a measured 1.6× to 3.1×, using parallel scans and hash
aggregation that SQLite has no equivalent for. It also wins the narrow tape scan by a measured 1.5×.

## Part 3: The prediction that was wrong, and the index that fixed it

Before the covering indexes existed, Q7 measured **776.06 ms on SQLite against 56.15 ms on
Postgres — a measured 13.8× gap**, and 776 ms is not a prompt response.

Adding `tool_call(tool_name, success)` and `tool_call(session_id, tool_name)` moved SQLite's Q7 to a
measured **64.34 ms, a 12.1× improvement**, and cut Postgres's lead to a measured 1.6×.

**So the aggregation deficit was indexable, not structural** — but only once someone thinks to index
it. Postgres reached a measured 56 ms with no covering index at all, because a parallel hash aggregate does not
need one. That is the honest shape of the trade: Postgres is faster by default on analytical
queries; SQLite gets within range when indexed deliberately, and is far slower when it is not.

## Part 4: What this means for the decision

**The recommendation in PRD §3.1 stands, and one claim behind it does not.**

The claim that survived: the read path is dominated by precomputed-FINDINGS reads and one selective
session scan, and SQLite is measured fastest at exactly those — Q1, Q2, Q5, Q6.

The claim that did not: that Postgres's planner and parallelism "never engage" here. They engage,
and at the design target they are worth a measured 1.6× to 3.1× on the three analytical shapes.

What rescues the decision is architectural rather than a property of SQLite: **Q7, Q8 and Q9 are
findings, not queries.** FR-15, FR-16 and FR-31/FR-33 are computed at analysis time and persisted
(§3.2), and FR-57 stores the per-version breakdown as an attribute of the finding. In the shipped
product those three run once per ingest, and the surfaces read Q1 and Q2.

Two requirements follow, and both are now measured rather than asserted:

1. **Keeping FINDINGS materialised is load-bearing, not a style preference.** If an aggregation
   leaks into the request path, SQLite is measured 1.6× to 3.1× slower than Postgres at the design
   target — and a measured 13.8× slower if the covering index is also missed.
2. **The masthead must be precomputed too.** Q3 measured 126 ms on SQLite and 118 ms on Postgres, so
   this one is not an engine problem: `COUNT(*)` and `COUNT(DISTINCT session_id)` over a million rows
   is slow on both. FR-41's corpus masthead should read stored counters.

The payload lever is confirmed: Q4 against Q5 is the same measured 10,906 rows from one table, and
dragging the verbatim JSON along costs a measured 2.3× on SQLite (26.55 → 59.80 ms) and a measured
7.1× on Postgres, measured at 17.37 → 122.95 ms. Keeping the payload out of list queries is worth
more than the engine choice on that path.

None of this outweighs §3.8's "no socket", FR-11's "no server", the one-file purge or owner-only
permissions. The measurement was run to check whether performance argued against those constraints.
It does not.

---

## Self-review — what was checked and how

- **Both engines ran through the drivers the product will use**, on .NET 10, rather than through a
  scripting language whose overhead would differ from the product's.
- **The harness is in the repository** (`bench/Program.cs`) and takes the corpus directory, a
  connection string and a scale factor, so every figure here is reproducible.
- **One prediction was wrong and is recorded rather than quietly dropped:** Postgres's storage
  advantage was estimated at 4–6× and measured at 1.6×.
- **A second prediction was wrong:** that Postgres's query-planner advantages would not engage. They
  did, on three of nine shapes.
- **Material confound, favouring SQLite: Postgres ran in Docker Desktop**, so its socket traffic
  crosses a WSL2 VM boundary and a port proxy. A native install would cut the per-query round trip,
  which is most of Q1 and Q6 — the two shapes where SQLite's advantage is largest. **Those two
  ratios should be read as upper bounds on SQLite's advantage, not as the gap on equal footing.** The
  aggregation results are compute-bound and are not explained by this.
- **Warm cache only.** Cold-cache behaviour was not measured; evicting the OS page cache reliably on
  Windows was judged not worth the complexity for a 2 GB database that will normally be warm. This is
  the one place Postgres's measured 1.6× storage advantage might matter and it went untested.
- **The 18× corpus duplicates the measured 35 sessions** under distinct session ids, giving 630
  <!--src: 35 x 18, arithmetic on the scale factor--> of them. Column cardinality and per-session
  index locality are therefore more regular than a genuine corpus of that size would be. The
  absolute figures are a reasonable guide; the shape of the split is the result.
- **Not measured:** EF Core's own overhead. The harness issues SQL directly, so these are engine and
  driver numbers. EF Core adds materialisation cost on top, equally to both.
- **One machine, one run per scale**, with no repetition across reboots or machines.
  <!--src: harness configuration, chosen not measured-->
