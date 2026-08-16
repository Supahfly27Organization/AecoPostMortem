# Content-check false positives — a measurement

**Date:** 2026-08-16 · **Status:** measurement record, nothing built
**Commissioned by:** the operator, to answer PRD Part 8 Q1 — *how does a scoped check derive its
scope*, and can a false-positive rate be estimated at all.

> **What this is.** A measurement run against `~/.copilot/` and `~/.claude/` on 2026-08-16, asking
> how much of a content check's false-positive mass can be removed *mechanically*, before the
> unsolved scoping problem is touched.
>
> **What this is not.** Not a plan, not a requirement, not approved scope. It records what was
> measured and what it means for
> `docs/product-superpowers/prds/2026-08-16-copilot-session-postmortem.md`; it does not amend it.

---

## Part 1: The question, and why the existing figure could not answer it

Discovery finding 11 measured three rules: unscoped hits 194 / 61 / 33 against scoped hits 0 / 0 / 20.
That gives a measured false-positive share of 100% / 100% / 39%, on a sample of three rules that
were hand-picked off the miss list rather than sampled, with the measured 20 survivors never
adjudicated.

Three rules cannot produce a rate. The question this run asks instead is narrower and answerable:
**how much junk can be removed without solving scoping at all?**

---

## Part 2: Method, and the extractor validated against discovery

A write unit is one file touched by one write operation. The extractor splits `apply_patch`
envelopes per file, and separates **agent-added content** from **pre-existing content carried along
by the write**:

| Tool | Measured calls | Added content | Pre-existing content |
|---|---|---|---|
| `apply_patch` | 381 | lines beginning `+` | context lines and `-` lines |
| `edit` | 239 | `new_str` | `old_str` |
| `create` | 53 | `file_text` | none |

Those sum to a measured 673 write operations, matching discovery finding 11 exactly.

**Extractor validation** — this run against discovery finding 11:

| Property | Measured, this run | Measured, discovery finding 11 | Agrees |
|---|---|---|---|
| Write units | 842 | 842 | yes |
| Distinct file paths | 380 | 380 | yes |
| Units carrying a usable path | 842 of 842 | 842 of 842 | yes |
| Sessions with writes | 20 | 20 | yes |
| Added lines in patch envelopes | 22,069 | 22,089 | within a measured 20 lines |
| Characters of written content | 1,565,687 | 1,538,717 | no — a measured 1.8% apart |

The four structural counts reproduce exactly, so the two extractors are reading the same thing. The
character total differs by a measured 1.8%, and the added-line count by a measured 20 lines; both
are consistent with a slightly different treatment of removed lines. Neither affects a ratio.

### The rule set: a measured 14 testable rules, not 3

Content rules and written content live in different corpora, so they had to be joined.

**Measured: 0 content-shaped rules exist in the Copilot corpus.** All 14 distinct
`<custom_instruction>` blocks were extracted, yielding a measured 43 distinct rule statements —
which matches discovery finding 1's measured 43 exactly — of which a measured 8 are
normative-negative and **all 8 constrain tool choice or agent behaviour, none constrains written
code**. This corroborates discovery finding 10 from the other direction.

The rules therefore come from the Claude Code corpus, whose `nested_memory` attachments carry
`CLAUDE.md` verbatim:

| Stage | Measured |
|---|---|
| `CLAUDE.md` files recovered | 60 |
| Repository roots | 4 |
| Distinct rule bullets | 550 |
| Bullets carrying a negative marker | 153 |
| …naming a banned symbol **after** the negative marker | 123 |
| …whose symbol also appears in the Copilot write corpus | 62 |
| …surviving generic-token exclusion, i.e. genuinely testable | **14** |

The measured 60 files across 4 repositories match discovery finding 10 exactly. The bullet counts
differ from discovery's measured 427 bullets and 105 normative because the filters differ; the
overlap that matters is that both runs see the same measured 60 files.

**The prediction that the sample could not grow was wrong.** It grew from 3 rules to a measured 14 —
a measured 4.7× — and that is what makes the rest of this document a distribution rather than an
anecdote.

---

## Part 3: The headline — three mechanical filters remove a measured 61.8% of hits, without scoping

Applied in order across all of the measured 14 testable rules:

| Stage | Measured hits | Removed at this stage |
|---|---|---|
| Unscoped, all written content | 767 | — |
| **Agent-added content only** | 606 | a measured 21.0% |
| …**excluding documentation files** | 295 | a measured 51.3% |
| …**excluding external-URL lines** | 293 | a measured 0.7% |

**Cumulative: a measured 61.8% of unscoped hits removed before scoping is attempted.**

Per-rule reduction, measured across the 14: min 40%, p25 54%, **median 63%**, p75 80%, max 100%.
A measured 2 of the 14 rules drop to zero hits on these filters alone.

### Robustness

The result does not depend on how loosely the banned symbol is matched:

| Match convention | Measured hits before | Measured hits after | Cumulative removal | Median per-rule |
|---|---|---|---|---|
| Strict, word-boundary | 767 | 293 | 61.8% | 63% |
| Loose, plain substring | 911 | 368 | 59.6% | 57% |

### Which filter earns its place

- **Documentation files are the dominant free win**, at a measured 51.3% of what survives the first
  filter. The operator writes a great deal of markdown, and a rule quoted in a spec document or a
  `CLAUDE.md` matches itself. The `fetch()` rule's own text was measured as one of its own hits.
- **Agent-added-only is real but secondary**, at a measured 21.0%. This corrects the optimistic
  reading: "the agent didn't write it" is *not* the main cause of false positives.
- **External-URL lines are negligible here**, at a measured 0.7%. Kept because it is free and its
  two measured hits were both genuine false positives, but it earns no priority.

---

## Part 4: Filters are not a substitute for scoping

Head-to-head on the three rules discovery already adjudicated by hand:

| Rule | Measured raw | Measured filters only | Measured scope only | Measured both |
|---|---|---|---|---|
| Controllers never `UpFrontDbContext` | 198 | 92 | **0** | **0** |
| `OrderSagaEvent` never `UpdateAsync` | 15 | 9 | 9 | 6 |
| Frontend never raw `fetch()` | 15 | 3 | 8 | 3 |

On the first rule the filters remove a measured 53% and scoping removes **all** of it. The two are
complementary, not alternatives: filters cut the volume cheaply and generically, scoping is what
actually decides correctness.

---

## Part 5: The finding that sharpens Q1 — the scope *mechanism* decides the answer

Q1 is usually stated as "the check needs a scope". The measurement says something more specific and
more awkward: **two equally plausible scope mechanisms give different answers on the same rule.**

All figures below use the same strict word-boundary convention as Part 4, so they are comparable
with it — stating the convention is the point of the exercise.

| Rule | Scope mechanism | Measured hits |
|---|---|---|
| Controllers never `UpFrontDbContext` | unscoped | 198 |
| | path contains `Controller` | 0 |
| | path has a `/Controllers/` directory | 0 |
| | filename ends `Controller.cs` | 0 |
| | **content declares a `*Controller` class** | **29** |
| `OrderSagaEvent` never `UpdateAsync` | unscoped | 15 |
| | **path mentions `ordersaga`** | **9** |
| | content mentions `OrderSagaEvent` | 0 |
| | `UpdateAsync` on an `OrderSagaEvent` line | 0 |
| Frontend never raw `fetch()` | unscoped | 15 |
| | path under `/FrontEnd/` | 5 |
| | `.ts` / `.tsx` / `.js` / `.jsx` extension | 7 |
| | either | 8 |

Two mechanisms that both read as reasonable land a measured 29 apart on the first rule and a
measured 9 apart on the second, and in both cases one of them is 0.

This is discovery finding 3's fivefold operand-resolution spread appearing again in the content
domain, from the same underlying cause: the rule states a scope in English and the check has to
choose a mechanical reading of it.

### Adjudicating the survivors

The path-scoped `UpdateAsync` check leaves a measured 9 hits, falling to a measured 6 once the
Part 3 filters are also applied. Every one is a false positive, and provably so:

```
IOrderSagaInstanceData.cs        Task UpdateAsync(OrderSagaInstance instance, …)
IOrderSagaStepData.cs            Task UpdateAsync(OrderSagaStep step, …)
IOrderSagaOperatorActionData.cs  Task UpdateAsync(OrderSagaOperatorAction action, …)
OrderSagaInstanceData.cs         public async Task UpdateAsync(OrderSagaInstance instance, …)
OrderSagaStepData.cs             public async Task UpdateAsync(OrderSagaStep step, …)
OrderSagaOperatorActionData.cs   public async Task UpdateAsync(OrderSagaOperatorAction action, …)
```

All six are `UpdateAsync` on **sibling entities** — `OrderSagaInstance`, `OrderSagaStep`,
`OrderSagaOperatorAction` — which the rule does not govern. The rule names one entity, and
`OrderSagaEvent` was measured to appear exactly **once** in the entire write corpus. Entity-scoping
returns a measured 0 and is right; path-scoping returns a measured 9 — the 6 above plus 3 the Part 3
filters remove — and every one of them is wrong, on a rule where path-scoping looks entirely
reasonable.

The `fetch()` check leaves a measured 3 hits:

| Hit | Verdict |
|---|---|
| `authSession.ts` × 2 — `fetch(\`${AUTH_API}${path}\`)` | **False positive.** The auth plumbing the preferred wrapper is built on. A wrapper's own implementation must use the thing it wraps |
| `contact-client.js` — `fetch('/api/contact')` | **Unresolved.** Internal path, but the rule governs *authenticated business API* calls and whether this endpoint is one cannot be determined from the logs |

Before scoping, the same rule's hits also included two calls to `challenges.cloudflare.com` and
`api.resend.com` — external third-party APIs the rule does not govern, both removed by the
external-URL filter.

**So on the one rule that survived discovery's scoping, adjudication finds at best 1 of 3 real, and
possibly 0 of 3.** Discovery open question 3 asked whether the surviving `fetch` hits were real. The
answer is: mostly not, and the dominant cause is the definition-site problem — the wrapper's own
implementation.

---

## Part 6: Four false-positive causes, which need four different fixes

Lumping these is part of why a single rate felt unobtainable. They are separable, and three of the
four are mechanically detectable today.

| # | Cause | Example measured | Detector | Status |
|---|---|---|---|---|
| 1 | The agent did not write it — pre-existing context carried by the write | a measured 21.0% of all hits | added content only | free, works now |
| 2 | The hit is prose, not code — often the rule quoting itself | a measured 51.3% of the remainder | documentation file extension | free, works now |
| 3 | The symbol is used somewhere the rule does not govern | 194 → 0 on the controllers rule | **scope** | **unsolved — Q1** |
| 4 | The symbol is used in the one place the rule must permit — the wrapper's own implementation | a measured 2 of 3 surviving `fetch` hits | definition-site test | designable, not built |

Cause 3 is the only one that needs the unsolved answer, and Part 5 shows it needs more than "apply a
scope" — it needs the right *kind* of scope, chosen per rule.

---

## Part 7: Can a false-positive rate be estimated?

**Not as a corpus-wide number.** But three per-rule estimators are now measured to work, and none
needs a human:

1. **Filter shrinkage** — the measured 61.8% aggregate, median 63% per rule. A rule losing most of
   its hits to the documentation filter was mostly matching prose.
2. **Scope shrinkage** — run scoped and unscoped and report the ratio. The measured 198 → 0 says the
   scope was doing all the work; a measured 15 → 8 says it was doing half.
3. **Scope-mechanism disagreement** — run more than one plausible scope and report the spread. A rule
   where path-scoping and entity-scoping disagree (measured 42 against 0) is a rule whose hits should
   not be shown as findings at all.

Estimator 3 is the new one, and it is the honest response to Q1: where the mechanism is ambiguous,
**report the ambiguity instead of picking a mechanism and reporting a number**.

True ground truth still requires operator adjudication, one click per hit. The three estimators
decide *which* hits are worth a click.

---

## Part 8: What this means for the PRD

Not applied — the PRD is at its review gate. Recorded for a single amendment pass.

| PRD element | Change indicated |
|---|---|
| FR-36 (extract write units) | Add: separate agent-added content from pre-existing content, per the Part 2 table. A measured 21.0% of hits are removed by this alone |
| FR-37 (scoped content checks) | Add the two generic filters, which are independent of scoping and remove a measured 61.8% together. Add: when two plausible scope mechanisms disagree, refuse — extending "refuse, don't warn" from *absent* scope to *ambiguous* scope |
| FR-38 (hits are unconfirmed) | Strengthen. The measured adjudication is 2 of 3 surviving `fetch` hits are the wrapper's own implementation. Add the definition-site test as cause 4 |
| Part 8 Q1 | **Rewrite, severity unchanged but reframed.** Not "derive a scope" but "derive the right *kind* of scope, and detect when you cannot" — measured 42 against 0 on one rule from that choice alone |
| Part 8 Q3 (are the `fetch` hits real?) | **Answerable now: mostly no.** A measured 2 of 3 are the wrapper's own implementation, 1 unresolved |
| §5.5 counter metrics | The per-rule shrinkage figures are computable, so "false-positive content-check hits" gains a real proxy instead of waiting for adjudication |

**What did not change.** Scoping stays the central risk. The filters cut volume; they do not decide
correctness. On the controllers rule the filters removed a measured 53%, and scoping removed a measured 100% of
what was left.

---

## Self-review — what was checked and how

- **The extractor was validated before it was trusted.** Four structural counts reproduce discovery
  finding 11 exactly, as measured: 842 write units, 380 paths, 842 of 842 with a path, 20 sessions. The two
  figures that do not agree are recorded in Part 2 rather than reconciled away.
- **Discovery's 194 / 61 / 33 could not be reproduced exactly.** Closest measured under a plain
  substring convention: 204 and 65, against 194 and 61. The `fetch` figure is furthest — a measured
  15 under a `fetch(` pattern against discovery's 33 — and no convention tried reproduced it. The
  cause is that **neither run recorded its regex**, which is the same defect the product is being
  built to prevent. Every ratio here is internally consistent within one convention, and Part 3's
  robustness table shows the conclusion holds under both.
- **The headline was checked against a second match convention** rather than reported from one:
  measured 61.8% strict against 59.6% loose.
- **The survivors were adjudicated by reading them,** not by assuming. The six `UpdateAsync` hits are
  reproduced verbatim in Part 5 so the sibling-entity reading can be checked rather than trusted.
- **One prediction made before the run was wrong and is recorded as such:** the testable rule set was
  predicted to stay in single digits and measured at 14.
- **Not measured:** whether `contact-client.js`'s `/api/contact` call is an authenticated business
  API call. It needs the repository, which this product does not read.
- **Not measured:** whether the 14 testable rules are representative of the measured 123 rules naming
  a banned symbol. They are the subset whose symbol appears in this write corpus, which is a
  selection on the outcome and could bias the shrinkage distribution in either direction.
- **One corpus, one machine.** Everything here is an observation about this corpus, never a rate to
  quote about content checks in general.

---

## Appendix: reproduction

Scripts live in the session scratchpad, not the repository. The load-bearing one is the write-unit
extractor:

```python
FILE_MARK = re.compile(r"^\*\*\* (Add|Update|Delete) File: (.+?)\s*$")

def parse_patch(envelope):
    """-> [(path, added_text, pre_text)]; added = '+' lines, pre = context and '-' lines."""
    units, path, added, pre = [], None, [], []
    for raw in envelope.splitlines():
        m = FILE_MARK.match(raw)
        if m:
            if path is not None:
                units.append((path, "\n".join(added), "\n".join(pre)))
            path, added, pre = m.group(2), [], []
        elif path is None or raw.startswith(("*** Begin Patch", "*** End Patch", "@@")):
            continue
        elif raw.startswith("+"):
            added.append(raw[1:])
        else:
            pre.append(raw[1:] if raw.startswith((" ", "-")) else raw)
    if path is not None:
        units.append((path, "\n".join(added), "\n".join(pre)))
    return units
```

`edit` contributes `new_str` as added and `old_str` as pre-existing; `create` contributes
`file_text` as added with no pre-existing content. Rules come from `~/.claude/projects/*/*.jsonl`,
from attachments where `type == "nested_memory"`, reading `content.content` — the outer `content` is
an object, not a string, and treating it as a string recovers a measured 0 files.
