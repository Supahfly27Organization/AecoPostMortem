# AecoPostMortem.Cli

The command surface FR-58 enumerates: `ingest`, `rebuild`, `purge`, `serve`.

## References

`Api`, `Findings`, `Ingestion`, `Data` — the CLI is the entry point that dispatches into whichever
layer a command needs; it holds no logic of its own beyond parsing and dispatch. `Data` is on that
list because `purge` is a store operation and there is no layer between the two worth inventing for
it.

## Non-obvious decisions

### `CommandSurface.Commands` is the single source of truth for what commands exist

The listing (`CommandListing`) is rendered from it and dispatch (`CommandRunner`) reads from it, so
nothing else enumerates commands. `CommandSpec.ArrivesWith` names what implements each command's
behaviour, and a command whose behaviour has not landed reports that and exits zero rather than
failing — FR-58 requires the surface to enumerate itself before what sits behind it exists.

### `CommandRunner.Run` takes the store as an optional argument

It defaults to `LocalStore.AtDefaultLocation()`, which is the operator's real store. Tests pass a
throwaway one; without that argument the only way to test `purge` would be to delete it.

### `rebuild` drops and recreates the derived layer, and takes no arguments

`rebuild` calls `DerivedSchema.Rebuild` (unconditional drop-and-recreate, distinct from the
version-gated rebuild `LocalStore.Open` already runs via `EnsureCurrent`) and reports the RAW event
count it ran against. It opens the store and nothing else — there is no path argument on its
`CommandSpec` (`Arguments` is `""`, unlike `ingest [path]`), so "the source directory is not read"
holds structurally rather than by a runtime check (S-46, issue #24). The actual re-derivation of
NORMALIZED/FINDINGS rows from RAW is not implemented yet — that logic lands with the E1 ingestion
stories; today `rebuild` empties the derived tables rather than repopulating them, which is the
honest behaviour for a RAW that has no reader yet.

## Status

The command surface exists (`CommandSpec`, `CommandSurface`, `CommandParser`, `CommandListing`,
`CommandRunner`, `Program`), and `purge` and `rebuild` are wired to the store. Behaviour behind
`ingest` and `serve` arrives in the order each `CommandSpec.ArrivesWith` names.

## Playbook — adding a command

1. Add a `CommandSpec` to `CommandSurface.Commands`. Nothing else enumerates commands.
2. Add its name to `CommandSurfaceTests.The_surface_is_exactly_the_four_commands_FR_58_enumerates`
   and to the `[InlineData]` sets in `CommandListingTests` and `CommandRunnerTests`.
3. Implement dispatch in `CommandRunner.Run`.

The listing is rendered from the table, so a command cannot exist without being documented — do not
add a second place that lists commands.
