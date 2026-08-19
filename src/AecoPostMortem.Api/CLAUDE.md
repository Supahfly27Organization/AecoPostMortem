# AecoPostMortem.Api

Endpoints for the three surfaces.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General` and `FindingEnvelope.Adherence`, and the `From`/`FromAdherence` factories that assemble them from a `Finding` |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |
| `SilentCheckEnvelope.cs` | FR-42's "checks that found nothing" surface — `SilentCheckEnvelope.From(CheckRegistry)` projects only the entries that ran clean |

## References

`Findings` — the API is a thin host over the finding classes and their orchestration; it has no
reason to reach into `Data` or `Rules` directly, only through what `Findings` already exposes.

## Non-obvious decisions

### `FindingEnvelope` is two closed shapes, not one type with a nullable resolution

`Finding.Resolution` is nullable because only adherence classes carry one (FR-33). The response
envelope makes that distinction structural rather than repeating the nullable field: `General` has no
`Resolution` or `RuleVersion` members at all, and `Adherence` is the only shape that has them — both
`required`. Assembling an `Adherence` envelope without a resolution and rule version is a compile
error (CS9035), the same guarantee `Finding.Provenance` already gives (issue #23). `FR-33`'s refusal
therefore lives here, structurally, at build time; `S-24` is the story that exercises the resulting
behaviour at the API boundary — this contract only has to make the bare figure unrepresentable, not
implement the refusal itself.

Both shapes derive from `FindingEnvelope` through a private constructor, so nothing outside this file
can add a third shape — the same closed-hierarchy trick `SuggestionEnvelope` uses. `[JsonPolymorphic]`
/ `[JsonDerivedType]` carry a `"kind"` discriminator (`"general"` / `"adherence"`) so a client can tell
the two apart without inspecting which optional fields happen to be present.

### `SuggestionEnvelope` makes "no suggestion" a value, not an absence

`Finding.Suggestion` is nullable because a finding class with no template (FR-56) ships with none.
Wrapping it in a nullable field on the envelope would let "no suggestion" collide with "the field was
omitted by mistake." `SuggestionEnvelope` is instead a required, closed two-state union —
`Present { Text }` and the `Absent` singleton (backed by the nested `AbsentSuggestion` record) — so
every served finding's `Suggestion` field is present in the JSON, and its value states explicitly
which case applies. `SuggestionEnvelope.Of(Suggestion?)` does the mapping from the domain type.

### `SilentCheckEnvelope.From` filters, it never synthesises

FR-42's surface has exactly one producer, `From(CheckRegistry)`, and it is a pure filter over
`CheckRegistry.Entries` — `Status == Ran && FindingCount == 0` — never a step that fabricates an
entry for a check the registry doesn't carry. That is what makes all three of this story's negative
scenarios (issue #46) hold structurally rather than by caller discipline:

- A `Refused` entry is dropped here; it belongs to the Rules Inventory (FR-53) as "not checkable",
  a different surface this project does not yet implement — showing it here as clean is exactly the
  "silence reading as compliance" failure PRD §3.9 names.
- A check the registry has no entry for at all (not built yet this release, e.g. the contradiction
  check before S-38) has nothing for `From` to project — absence in, absence out. There is no
  hard-coded list of expected `CheckId`s this type could complete against; it only ever reflects
  what `CheckRegistry.Entries` actually contains.
- A `Ran` entry with `FindingCount > 0` is also dropped: this surface is specifically checks that
  found *nothing*, not every check that ran. `FindingCount` is a real int on every served
  `SilentCheckEnvelope` (never null, since `Refused` entries — the only ones with a null
  `FindingCount` — are filtered out before the projection), and it is always `0` by construction of
  the filter, carried explicitly rather than left for the reader to infer from mere presence.

Unlike `FindingEnvelope` and `SuggestionEnvelope`, `SilentCheckEnvelope` is a single plain
`sealed record` rather than a closed hierarchy behind a private constructor. Those two types close
off a *discriminated union* — "which of these shapes is this?" is part of what a client needs to
know. This surface serves only one shape (a clean check's id, population and zero count); there is
no second variant to keep a client from constructing by mistake, so there is nothing for the
closed-hierarchy trick to protect here.

### No HTTP endpoints yet

S-50 (FR-59) is a contract story: it publishes the response shape so the stories that build real
endpoints against it (S-08, S-22, S-24, S-36, S-37, S-42, S-48) have something structural to target.
S-37 (FR-42, this file's `SilentCheckEnvelope`) follows the same pattern — `From` takes a
`CheckRegistry` as a plain input, the same way `FindingEnvelope`'s factories take a `Finding`.
Nothing in this project reads through `Data` or calls into `Rules` yet, and no HTTP host exists —
those arrive with the stories this contract unblocks.

## Status

The response envelope contracts: `FindingEnvelope` / `SuggestionEnvelope` (FR-59, issue #13) and
`SilentCheckEnvelope` (FR-42, issue #46). No HTTP endpoints exist yet — those arrive with the
stories these contracts unblock.
