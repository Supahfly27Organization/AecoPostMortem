# AecoPostMortem.Cli

The command surface FR-58 enumerates: `ingest`, `rebuild`, `purge`, `serve`.

## References

`Api`, `Findings`, `Ingestion` — the CLI is the entry point that dispatches into whichever layer a
command needs; it holds no logic of its own beyond parsing and dispatch.

## Non-obvious decisions

`CommandSurface.Commands` is the single source of truth for what commands exist — the listing
(`CommandListing`) is rendered from it and dispatch (`CommandRunner`) reads from it, so nothing else
enumerates commands. `CommandSpec.ArrivesWith` names what implements each command's behaviour; the
surface exists, the behaviour does not yet, and `serve` reports that rather than failing when
invoked.

## Status

The command surface exists (`CommandSpec`, `CommandSurface`, `CommandParser`, `CommandListing`,
`CommandRunner`, `Program`). Behaviour behind each command arrives next, in the order its
`CommandSpec.ArrivesWith` names.

## Playbook — adding a command

1. Add a `CommandSpec` to `CommandSurface.Commands`. Nothing else enumerates commands.
2. Add its name to `CommandSurfaceTests.The_surface_is_exactly_the_four_commands_FR_58_enumerates`
   and to the `[InlineData]` sets in `CommandListingTests` and `CommandRunnerTests`.
3. Implement dispatch in `CommandRunner.Run`.

The listing is rendered from the table, so a command cannot exist without being documented — do not
add a second place that lists commands.
