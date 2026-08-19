# AecoPostMortem.Api

Endpoints for the three surfaces.

## Structure

| File | What it holds |
|---|---|
| `FindingEnvelope.cs` | FR-59's response contract for one served finding — `FindingEnvelope.General` and `FindingEnvelope.Adherence`, and the `From`/`FromAdherence` factories that assemble them from a `Finding` |
| `SuggestionEnvelope.cs` | FR-56 in the response contract — `SuggestionEnvelope.Present` and `.AbsentSuggestion`, so "no suggestion template" is an explicit serialised state, never a missing field |

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

### No HTTP endpoints yet

This story (S-50 / FR-59) is a contract story: it publishes the response shape so the stories that
build real endpoints against it (S-08, S-22, S-24, S-36, S-37, S-42, S-48) have something structural
to target. Nothing here reads through `Data` or calls into `Rules` yet — the factory methods take a
`Finding` (and, for `FromAdherence`, a `Resolution` and rule version) as plain inputs.

## Status

The response envelope contract (`FindingEnvelope`, `SuggestionEnvelope`). No HTTP endpoints exist
yet — those arrive with the stories this contract unblocks.
