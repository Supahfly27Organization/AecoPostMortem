# The findings contract and the check registry — design

**Story:** S-44 ([issue #23](https://github.com/Supahfly27Organization/AecoPostMortem/issues/23)) ·
**Implements:** FR-57, PRD §3.2 FINDINGS layer, §3.8 "provenance is structural"
**Blocks:** S-02, S-05, S-06, S-13, S-14, S-15, S-16, S-17, S-18, S-32, S-33, S-36, S-37, S-39, S-40,
S-42, S-46, S-50, S-51
**Date:** 2026-08-19

## 1. What this story is for

Nineteen stories need a finding to have one shape before any of them can write one. This story
publishes that shape, and the shape of the registry that says which checks ran — and it writes no
finding and runs no check. Like S-49 before it, that is deliberate: a contract story that also did
some of the work it is unblocking would be designing the later stories blind.

**This story ships pure C# types in `AecoPostMortem.Findings`, with no persistence.** §3.2 lists
FINDINGS as a store layer, but nothing in the acceptance criteria mentions a table, an EF mapping, or
a migration — every scenario is about the shape's own structure: what it carries, what fails to
construct, what a class or a check declares about itself. Wiring a `Finding` into the store is a
later story's problem, the same way S-49 published NORMALIZED's shapes years (in review-time) before
S-06 populates them. `AecoPostMortem.Data` is "the only project that owns the schema"
(`src/AecoPostMortem.Data/CLAUDE.md`) and cannot reference `AecoPostMortem.Findings` without a cycle,
so a `Finding` cannot be EF-mapped from inside `Data` without first deciding how the two projects
talk — a decision this contract does not need to make in order to satisfy its own acceptance
criteria.

## 2. The finding record

```
src/AecoPostMortem.Findings/
  Finding.cs              ← FindingClass, Provenance, OperatorResponse, Finding
  FindingClassRegistry.cs ← FindingClassRegistration, FindingClassRegistry
  Evidence.cs              ← EvidenceItem
  Recurrence.cs            ← Recurrence, RecurrenceOccurrence
  Resolution.cs            ← Resolution
  Suggestion.cs            ← Suggestion
  CheckRegistry.cs         ← CheckRunStatus, CheckRegistryEntry, CheckRegistry
```

### 2.1 Provenance fails construction structurally, not by convention

```csharp
public required Provenance Provenance { get; init; }
```

`required` makes an object initializer that omits `Provenance` a compile error (`CS9035`). That is
what Scenario 1 means by "construction fails" — the same reasoning `AecoPostMortem.Rules/CLAUDE.md`
uses for its own invariant: structural is stronger than a code-review convention, because there is no
commit at which it can be skipped by accident. A reflection check
(`typeof(Finding).GetProperty(nameof(Finding.Provenance))!.GetCustomAttribute<RequiredMemberAttribute>()`)
proves the property carries the attribute the compiler enforces, so a future edit that quietly drops
`required` fails the test suite instead of only failing a hand review.

### 2.2 What the record carries — literally the acceptance criterion's list, nothing more

Scenario 2 names seven things and no others: class, provenance, evidence, recurrence, the resolution
used where one applies, its suggestion, and the operator's response. No `Id`. No `SessionId` — a
finding's identity is `(class, class-specific key)` per FR-57, not a session, and a session is where
`Recurrence` says the finding recurred, not where the finding itself lives.

```csharp
public sealed record Finding
{
    public required FindingClass Class { get; init; }
    public required Provenance Provenance { get; init; }
    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
    public required Recurrence Recurrence { get; init; }
    public Resolution? Resolution { get; init; }
    public Suggestion? Suggestion { get; init; }
    public OperatorResponse OperatorResponse { get; init; } = OperatorResponse.Ignored;
}
```

`Resolution` and `Suggestion` are the two nullable fields, and each is nullable for a reason stated
elsewhere in the PRD rather than invented here: FR-33's resolution only applies to adherence figures,
and FR-56 says a finding class with no template "ships with its evidence and no suggestion, never a
generic one." `OperatorResponse` is not `required`: a freshly detected finding has not yet been shown
to an operator, and FR-45's three states — accepted, rejected, ignored — already cover "never
responded to" as `Ignored`, so a fourth "pending" state would duplicate it.

### 2.3 The four classes, `Provenance`, and the operator's response

```csharp
public enum FindingClass
{
    RuleAdherenceToolChoice = 1,
    Waste = 2,
    RuleAdherenceWrittenContent = 3, // gated out of v1, PRD §3.4.3 — still a real class, no checks yet
    MissingCapability = 4,
}

public enum Provenance
{
    Observed,
    Derived,
    Inferred,
}

public enum OperatorResponse
{
    Ignored,
    Accepted,
    Rejected,
}
```

Numbered 1–4 to match the PRD §3.3 table rather than build order — §3.4.3 already recorded that the
*build* order is 2, 1, 4, with 3 waiting on input; renumbering the enum to match build order would
make the two documents disagree about which class is "3."

### 2.4 Evidence, Resolution, Suggestion

```csharp
public sealed record EvidenceItem
{
    public required string Field { get; init; }   // e.g. "data.toolName"
    public required string Value { get; init; }    // the quoted value, verbatim
}

public sealed record Resolution
{
    public required string OperandLayer { get; init; }  // FR-33: "the layer used per operand"
    public required int CallCount { get; init; }         // FR-33: "the resulting call counts"
}

public sealed record Suggestion
{
    public required string Text { get; init; }  // FR-56's rendered deterministic template
}
```

`EvidenceItem` is a field/value pair rather than a raw string so a UI can render "the actual event
fields" (Part 4) as fields, not as an opaque blob — Part 4 says the Raw tab is "the provenance
guarantee made clickable," which needs to know what it is pointing at.

### 2.5 Recurrence carries FR-57's decided answer as structure

FR-57 already decided the question Scenario 3 asks: identity is `(class, class-specific key)`,
version-independent, and a rule spanning several rule-set versions is **one** finding whose
per-version breakdown is an attribute of that finding, never several findings. `Recurrence` makes the
second half impossible to get wrong rather than asking a caller to remember it — there is no
constructor that produces two `Finding`s for one `(class, key)` pair, because the type has nowhere to
put a second one.

```csharp
public sealed record Recurrence
{
    public required string Key { get; init; }
    public required IReadOnlyList<RecurrenceOccurrence> Occurrences { get; init; }
}

public sealed record RecurrenceOccurrence
{
    public required string SessionId { get; init; }
    public string? RuleSetVersion { get; init; }  // null for classes with no rule-set version (Waste, MissingCapability)
}
```

The edge case — "a finding whose recurrence is one session must still carry a recurrence value" — is
satisfied by `Recurrence` being `required` on `Finding`: there is no path that constructs a `Finding`
with the field omitted, one-session or not.

### 2.6 `FindingClassRegistry` — Scenario 3's registration

```csharp
public sealed record FindingClassRegistration
{
    public required FindingClass Class { get; init; }
    public required string RecurrenceKeyDescription { get; init; }
}

public static class FindingClassRegistry
{
    public static readonly IReadOnlyList<FindingClassRegistration> All = [ /* one entry per FindingClass */ ];
}
```

Four entries, one per `FindingClass`, each stating in plain text what FR-57 already named per class:
the rule statement for a rule finding (classes 1 and 3), the path or hook identity for waste, the
tool name for a missing-capability cluster. A test asserts `All` has exactly one entry per
`FindingClass` value and no entry has an empty description — that is what "a finding class is
registered" and "declares what makes two occurrences the same finding" mean for a story that ships no
finding-detection logic yet.

## 3. The check registry

Scenarios 4 and 5 are about a different registrable thing than Scenario 3: not a finding class, a
check — and per the design decision, one registry, entries keyed by an abstract `CheckId` rather than
two parallel registries for classes and checks. A finding class is a fixed set of four, known today;
a check is open-ended — the eventual `AecoPostMortem.Rules` check-shape catalogue plus the
special-purpose checks named in §3.9 (contradiction, unresolvable-spawn, malformed-line) — so it gets
an abstract string id rather than an enum.

```csharp
public enum CheckRunStatus
{
    Ran,
    Refused,
}

public sealed record CheckRegistryEntry
{
    public required string CheckId { get; init; }
    public required CheckRunStatus Status { get; init; }
    public required int Population { get; init; }   // Scenario 4: "the population it was run over"
    public int? FindingCount { get; init; }          // set when Ran, including 0; null when Refused
    public string? RefusalReason { get; init; }       // set when Refused; null when Ran
}

public sealed record CheckRegistry
{
    public required IReadOnlyList<CheckRegistryEntry> Entries { get; init; }
}
```

**Why two states, not three.** Scenario 5 names exactly two: a refused check and a check that ran and
found nothing. Nothing in the acceptance criteria or FR-37/FR-42 names a third "never attempted"
state distinct from those two — FR-37's refusal already covers "did not run because scope could not
be resolved," and every other check that exists either ran (clean or not) or refused. Inventing a
third state the Gherkin does not ask for is exactly what §"YAGNI ruthlessly" warns against.

**Why the null/zero pair is the whole mechanism.** `FindingCount` is `null` when `Status == Refused`
and a real integer — possibly `0` — when `Status == Ran`. That is Scenario 5, structurally: a refused
check and a clean check are never "both zero," because refused is `null`, not `0`. This mirrors
`Agent.cs`'s tri-state (`AgentOutcome.CompletedCostUnknown` exists precisely so a missing metric is
never read as a zero one) rather than inventing a new idiom.

**Population is required on every entry, refused or not.** Scenario 4 states this as a blanket rule
("every check appears... with its run status and the population it was run over") with no carve-out
for refused checks. `Population` is the candidate set a check considered before deciding whether it
could run cleanly — e.g. "35 sessions in the corpus" — which is defined whether or not scope
resolution then succeeded.

`CheckRegistry` wraps the list because Scenario 4 says "the check registry is read," naming a thing to
read rather than a bare collection.

## 4. What this story ships, and what it does not

**Ships:** `Finding` and its seven fields; `FindingClass`, `Provenance`, `OperatorResponse`;
`EvidenceItem`, `Recurrence`, `RecurrenceOccurrence`, `Resolution`, `Suggestion`;
`FindingClassRegistry`; `CheckRunStatus`, `CheckRegistryEntry`, `CheckRegistry`; the tests below;
`src/AecoPostMortem.Findings/CLAUDE.md` updated to describe the shapes instead of "Empty."

**Does not ship:** any concrete finding-detection logic (S-02, S-05, S-06, ...); the
`AecoPostMortem.Rules` check-shape catalogue (a separate, still-empty project); persistence of a
`Finding` or a `CheckRegistryEntry` anywhere; the API response envelope (FR-59, a later story); the
Rules Inventory or Process Digest surfaces.

## 5. Tests

| Test | What it holds |
|---|---|
| A finding cannot be constructed without a provenance level | Scenario 1 — `Provenance` carries `RequiredMemberAttribute` |
| The record carries everything the surfaces need | Scenario 2 — all seven fields round-trip; the two optional ones can be `null` |
| `FindingClassRegistry` has exactly one entry per `FindingClass`, each with a non-empty recurrence-key description | Scenario 3 |
| A `CheckRegistry`'s entries include every check regardless of status | Scenario 4 |
| A refused entry and a clean entry are distinguishable, and neither reads as the other's zero | Scenario 5 |
| A finding's `Recurrence` is required, even for a single-session finding | the edge case naming a one-session finding explicitly |

## 6. Out of scope

Any finding class's detection logic. The check-shape catalogue in `AecoPostMortem.Rules`. Persistence
of either shape. The API envelope. Suggestion templates for any specific class (FR-56 itself, not
this contract). The Rules Inventory, Process Digest, and Flight Recorder surfaces.
