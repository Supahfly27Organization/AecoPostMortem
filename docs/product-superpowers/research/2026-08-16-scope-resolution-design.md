# Scope resolution for content checks — the answer to PRD Part 8 Q1

**Date:** 2026-08-16 · **Status:** design note, nothing built
**Answers:** `docs/product-superpowers/prds/2026-08-16-copilot-session-postmortem.md` Part 8 Q1.
**Rests on:** `docs/product-superpowers/discovery/2026-08-16-content-check-false-positives.md`
(cited as *FP measurement Part N*) and
`docs/product-superpowers/discovery/2026-08-16-copilot-session-postmortem.md` finding 8, whose
operand-resolution ladder this deliberately mirrors.

> **What this is.** A mechanism for deciding *where* a content rule applies, and for detecting when
> that cannot be decided. It converts Q1 from an open question into buildable requirements plus one
> residual unknown.
>
> **What this is not.** Not built, not measured end to end. The measured figures here come from the
> FP measurement; what this note adds is a design over them, and a validation plan that has not been
> executed.

---

## Part 1: The problem, restated precisely

A content rule states two things — a banned symbol and the territory it is banned in. The check
reads the first reliably and the second not at all.

The FP measurement showed the second half is not merely missing but **ambiguous**: on one rule,
path-scoping returned a measured 9 hits and entity-scoping a measured 0, and all 9 were false
positives. Picking a mechanism silently yields a confident wrong number.

This is discovery finding 3's fivefold operand spread in a second domain, and it gets the same shape
of answer for the same reason: **derive the resolution from the corpus, state which layer produced
it, and report the unresolvable rather than guessing.**

---

## Part 2: Filters run first, and most ambiguity dissolves

Measured on the controllers rule — four plausible scope mechanisms, before and after the two
generic filters from FP measurement Part 3:

| Scope mechanism | Measured raw | Measured, added-only | Measured, + documentation filter |
|---|---|---|---|
| path contains `Controller` | 0 | 0 | 0 |
| path has a `/Controllers/` directory | 0 | 0 | 0 |
| filename ends `Controller.cs` | 0 | 0 | 0 |
| content declares a `*Controller` class | **29** | 29 | **0** |

All of the measured 29 outlier hits were in markdown design documents carrying example controller
code:

```
  14  [DOC]  2026-05-05-order-saga-control-center-phase-1.md
  10  [DOC]  2026-04-28-auth-service-extraction.md
   5  [DOC]  2026-07-22-ledger-entry-correction.md
```

**The disagreement was filter residue, not a real disagreement.** Ordering matters: filter, then
resolve scope, then test for agreement. Any design that tests for agreement first will chase
artifacts.

This does not dissolve every case. On the `UpdateAsync` rule the measured 9 path-scoped hits fall to
a measured 6 under the filters and all 6 remain false positives, so that rule needs Part 4's layer
precedence rather than Part 2's filters.

---

## Part 3: A rule holds three symbols with three different jobs

Confusing them is what breaks scoping, and telling them apart is positional, therefore mechanical.

| Role | Where it sits | Example |
|---|---|---|
| **Subject** — the scope | leads the statement, before the first verb | **`OrderSagaEvent`** is append-only… |
| **Alternative** — the preferred thing | follows *use* / *only* / *prefer* / *instead of* | …only **`AddAsync`**… |
| **Banned** — what the check searches for | follows *never* / *not* / *avoid* / *must not* | …never **`UpdateAsync`** |

Worked against the two rules that behave differently:

> **`OrderSagaEvent`** is append-only — only **`AddAsync`**, never **`UpdateAsync`**
> subject → scope · alternative · banned

> Use **`apiFetch()`** for authenticated business API calls — never raw **`fetch()`**
> alternative · *(no subject)* · banned

The second rule has **no subject symbol at all**. Its scope is the English phrase *"authenticated
business API calls"*, which has no observable correlate in a tool-call log. Treating `apiFetch()` as
a scope because it is backticked and not banned is exactly the error this parse prevents — that rule
must fall to Layer 4 and be reported as not checkable, not checked badly.

---

## Part 4: Four scope layers, most confident first

Deliberately the same ladder as finding 8's operand resolution, because it is the same problem.

| Layer | Fires when | Scope resolves to | Evidence it works |
|---|---|---|---|
| **1 — Symbol** | the statement has a subject symbol (Part 3) | write content co-occurring with that symbol, within a bounded window | `OrderSagaEvent` scoping returns a measured 0 hits, which adjudication confirms is correct |
| **2 — Convention** | the statement names a category word | the naming convention that word correlates with **in this corpus** | "controllers" correlates with `*Controller.cs`; measured 0 hits under all three path readings |
| **3 — Path literal** | the statement contains a path fragment | that path prefix | `UpFront.Data/Migrations/`; the `never-read-path` shape already does this |
| **4 — Unresolvable** | none of the above fire | **nothing — the rule is reported *not checkable*, with the reason** | *"authenticated business API calls"* has no observable correlate |

**Layer 2 is the only new machinery**, and it is the same derivation finding 8 already performed:
tool roles were derived from the argument shapes present in the logs rather than from a table
anyone wrote. Here, a category word is resolved against the naming conventions present in the write
corpus rather than against a hard-coded map — no rule may state that controllers live in
`Controllers/`, because the next repository will not.

**Precedence is what fixes the `UpdateAsync` case.** That rule has a subject symbol, so Layer 1
fires and Layer 2 is never consulted. Path-scoping — the measured 9 hits, all false — never gets a
vote. The bug was never that path-scoping is wrong in general; it was that it was consulted for a
rule that had already answered the question more precisely.

---

## Part 5: The agreement test, as a backstop

For whatever survives Parts 2–4: run **every** layer that produces a candidate, not only the winner.

- Candidates agree → emit the finding, stating the layer that produced the scope.
- Candidates disagree → **suppress the finding and emit "scope ambiguous"** as its own item, naming
  the mechanisms and their counts.

This is the PRD's FR-37 second refusal. After filtering and precedence it should fire rarely, and
that is the design intent: a backstop that fires constantly means the resolver above it is broken.
Its firing rate is therefore itself a health signal, not just a safety net.

---

## Part 6: How this is validated — a labelled fixture, not an argument

A scope resolver cannot be proved correct analytically. The deliverable that answers Q1 is a
regression fixture, and building one is cheap:

| Step | What | Size |
|---|---|---|
| 1 | Run the measured 14 testable rules through filters, then the four layers | — |
| 2 | Present every surviving hit to the operator | a measured 293 hits survive filtering; scope resolution should cut this well below 100 |
| 3 | Label each: real / false positive / cannot tell | one sitting |
| 4 | Freeze as a fixture; every resolver change runs against it | permanent |

**A seed already exists.** The FP measurement adjudicated roughly 15 hits with known verdicts: the
6 sibling-entity `UpdateAsync` cases (all false), the 2 `authSession.ts` wrapper-implementation hits
(both false), the 2 external-API `fetch` calls (both false), and 1 unresolved `/api/contact` call.

Making the fixture the Phase C exit criterion is what stops the resolver being a plausible story —
which is the anxiety discovery §Forces of Progress names, applied to the product's own internals.

---

## Part 7: What remains unknown after this

One question, and it is now narrow enough to measure rather than argue about:

**How often does Layer 2 derive the wrong convention for a category word?** "Controllers" resolves
cleanly in this corpus. "Services", "handlers", "models" or "helpers" may correlate with several
conventions at once, or with none. The fixture in Part 6 measures this directly; nothing else will.

Two smaller ones:

- **Layer 1's window.** Co-occurrence "within a bounded window" is unspecified — same line, same
  declaration, or same file. Measured on the `UpdateAsync` rule, same-line and same-file both return
  0, so this corpus does not discriminate between them. A rule where they differ has not been found.
- **The definition-site test** (FP measurement Part 6, cause 4) is not scope and is not solved here.
  A rule preferring a wrapper cannot forbid the wrapper's own implementation, and that needs its own
  detector.

---

## Self-review — what was checked and how

- **The central claim of Part 2 was measured, not reasoned.** The hypothesis that the measured 29
  outlier hits were documentation artifacts was tested by listing them; a measured 29 of 29 are in
  three named markdown files, which is why the claim is stated as a result, not an explanation.
- **The design deliberately reuses finding 8's ladder** rather than inventing a second resolution
  idiom. If layered resolution is the right answer for tool operands, an unrelated mechanism for
  content scopes would need a reason, and there is none.
- **Part 3's parse was checked against the rule that breaks it**, not only the rule that suits it.
  The `apiFetch()` rule has no subject symbol, and the parse is written so that this produces "not
  checkable" rather than a scope derived from the preferred alternative.
- **Not built, not measured end to end.** Every figure here comes from the FP measurement. No layer
  in Part 4 has been implemented, and the agreement test in Part 5 has never run.
- **Not validated:** that Layer 2 generalises past "controllers". This is Part 7's first question and
  the single largest remaining risk in the content-check family.
- **Not tested:** whether the positional parse in Part 3 survives rule statements written as prose
  rather than as bullets. Every statement examined was a bullet.
