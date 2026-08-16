# Solution scaffold and the command surface — design

**Story:** S-47 ([issue #10](https://github.com/Supahfly27Organization/AecoPostMortem/issues/10)) ·
**Epic:** E1 — Durable ingestion ([issue #1](https://github.com/Supahfly27Organization/AecoPostMortem/issues/1))
**Implements:** FR-58, PRD §3.1
**Blocks:** S-01, S-48
**Date:** 2026-08-16

## 1. What this story is for

Nothing else in the product has anywhere to live until the solution exists, and Part 1 of the PRD
tells the operator to "run the ingest command" — so something has to be the thing that runs. S-47
delivers exactly two things: a solution that builds and is provably contained, and a command surface
that enumerates itself. It delivers no behaviour behind any of the four commands.

The containment rule is the reason this story is not merely `dotnet new sln`. PRD §3.1 states the
product depends on AecoLedger in neither direction; a rule that lives only in a review checklist
erodes. This story makes it a test.

## 2. Layout

```
AecoPostMortem.sln                       ← the only solution, at the repository root
Directory.Build.props                    ← net10.0, nullable, warnings-as-errors, LangVersion
Directory.Packages.props                 ← central package version management
src/
  AecoPostMortem.Data/                   ← class library; references nothing yet (S-01 fills it)
  AecoPostMortem.Ingestion/              ← class library → Data
  AecoPostMortem.Rules/                  ← class library → nothing
  AecoPostMortem.Findings/               ← class library → Rules, Data
  AecoPostMortem.Api/                    ← class library → Findings
  AecoPostMortem.Cli/                    ← executable → Ingestion, Findings, Api
test/
  AecoPostMortem.Data.Tests/
  AecoPostMortem.Ingestion.Tests/
  AecoPostMortem.Rules.Tests/
  AecoPostMortem.Findings.Tests/
  AecoPostMortem.Api.Tests/
  AecoPostMortem.Cli.Tests/
  AecoPostMortem.Containment.Tests/      ← the structural invariants; owns no src project
web/                                     ← React + TypeScript + Vite
```

**Why a `Cli` project, when PRD §3.1's tree lists five.** The stories document's project map names
"the solution, CLI, host and web shell" as S-47 and S-48's output, so the CLI was always intended;
§3.1's tree enumerates the modules of the pipeline, not every compilation unit. Keeping it separate
means `ingest`, `rebuild` and `purge` do not drag in the web stack, and `Api` stays what §3.1 calls
it — the endpoints — rather than becoming both host and endpoint library. `serve` is the only
command that will reach the host, and it reaches it through `Api`.

**Why `Containment.Tests` owns no src project.** Its subject is the solution, not a module. The
acceptance criterion is that a test project exists *for each* source project — a requirement over
source projects, which an additional test project does not violate.

**`bench/bench.csproj` stays out of the solution, deliberately.** It is the harness from the
SQLite-versus-Postgres latency research, it sits at the repository root, and adding it to the
solution would break the rule that no project in the solution sits outside `src`, `test` or `web`.
The assertion carries a comment saying so, because the obvious "fix" for a future reader is the
wrong one.

## 3. The command surface

`AecoPostMortem.Cli` is driven by a table rather than a parser library. The surface is four commands
and one optional positional argument; the acceptance criterion asks for each command to be listed
"with its arguments and its output channel", which is bespoke text that no general parser emits for
free. A table makes the criterion an assertion over data instead of over formatted output.

```csharp
sealed record CommandSpec(
    string Name,
    string Arguments,
    string OutputChannel,
    string Summary);
```

| Name | Arguments | Output channel | Summary |
|---|---|---|---|
| `ingest` | `[path]` | stdout — the coverage report (FR-14) | Read the Copilot session state and re-derive |
| `rebuild` | — | stdout — the re-derivation summary | Re-derive NORMALIZED and FINDINGS from RAW |
| `purge` | — | stdout — what was deleted | Delete the store (FR-11) |
| `serve` | `[--port <n>]` | stdout — the listening URL | Start the local API and web shell |

The same array renders the listing and dispatches the invocation, so the two cannot drift.

**Behaviour in S-47.** Parsing, the listing, unknown-command handling and exit codes are the real
deliverable; the four commands themselves are not implemented here.

| Invocation | Output | Exit code |
|---|---|---|
| no arguments | the listing, on stdout | 0 |
| a known command | a notice naming the story that implements it, on stdout | 0 |
| an unknown command | the listing, on stderr, preceded by what was not recognised | 2 |

`serve` reporting that its surfaces are not yet built, and exiting zero, is the acceptance
criterion's "reports which surfaces are not yet implemented rather than failing" — not a placeholder
that happens to satisfy it. It has nothing to serve until S-48.

**Shape for testability.** Parsing is a pure function from `string[]` to a parsed invocation, and
rendering writes to an injected `TextWriter`. Every CLI test therefore runs in-process; none starts
a child process.

## 4. The containment test

The test reads `AecoPostMortem.sln` and every `.csproj` **as files on disk**. It does not use
reflection over loaded assemblies: reflection sees only what the test project already references,
and a project that has drifted outside the rules is exactly the project the test project will not
reference. Solution membership and reference paths are build-graph facts, not runtime facts.

The repository root is located by walking up from `AppContext.BaseDirectory` until the directory
holding `AecoPostMortem.sln` is found.

Five assertions, one per clause of the acceptance criteria:

1. **No AecoLedger.** No `ProjectReference` or `PackageReference` in any project names an
   `AecoLedger*` assembly.
2. **No escape.** Every `ProjectReference` path, resolved against its own project directory and
   normalised, remains under the repository root.
3. **No stray project.** Every project listed in the solution sits under `src/`, `test/` or `web/`.
4. **Rules touches no persistence.** `AecoPostMortem.Rules.csproj` references no
   `Microsoft.EntityFrameworkCore*` package, no `System.Data.*` package and no `*.Data` project.
   This is what turns PRD §3.1's non-negotiable invariant from an assertion into a test: a project
   with no persistence dependency has a very small surface in which a tool name could hide.
5. **A test project per source project.** For every `src/<name>.csproj` there is a
   `test/<name>.Tests.csproj`.

Assertion 3 is the one that changed with the repository. It once asserted that no project crossed
the `SessionPostMortem/` directory boundary, because the subtree had to stay liftable by
`git subtree split`. The lift has happened, so a path-shaped assertion of that kind would now pass
trivially and prove nothing; what the boundary protected is assertions 1 and 2.

## 5. web/

A Vite scaffold — React and TypeScript — created in `web/`, with its lockfile committed. Its build
is verified by `scripts/build-web.ps1`, which runs `npm ci` and `npm run build` with `web/` as the
working directory, satisfying the rule that no frontend command runs from the repository root.

The containment test asserts only the structural half of that criterion: a `package.json` exists
under `web/`, and none exists at the repository root. `dotnet test` does not shell out to npm — that
would make the .NET suite depend on Node being installed and on `node_modules` being populated, and
would add the frontend build's duration to every backend test run.

The web shell's actual content — routing, the three surfaces, the two empty states — is S-48.

## 6. The five smoke tests

`Data.Tests`, `Ingestion.Tests`, `Rules.Tests`, `Findings.Tests` and `Api.Tests` each get one test
asserting their subject assembly's name and target framework. Stated plainly: these are thin, and
they exist because the acceptance criterion requires every test project to execute while the source
projects are still empty. They do catch a target-framework drift. Each is replaced by real coverage
by the story that fills its project — S-01 for `Data`, and so on.

`Cli.Tests` and `Containment.Tests` carry real tests from the start.

## 7. Implementation order

Test-first, in two passes:

1. **Containment.** Write the five structural assertions. They fail because no solution exists — the
   honest red for a scaffold story. Create the solution, the props files, the thirteen projects —
   six under `src/`, seven under `test/` — and the project references until they pass.
2. **Command surface.** Write the parser and listing tests against the table. Implement
   `AecoPostMortem.Cli` until they pass.

Then the Vite scaffold and `scripts/build-web.ps1`, verified by running it, and the smoke tests.
`.gitignore` gains `bin/`, `obj/`, `node_modules/` and `web/dist/`.

## 8. Out of scope

- Any EF Core entity, `DbContext` or migration — S-01. Repo Rule 4 governs where migrations may
  ever appear.
- Any endpoint, route or real web shell — S-48.
- Any behaviour behind `ingest`, `rebuild`, `purge` or `serve`.
- The FR-34 source scan proving `Rules` names no tool, MCP server or repository. S-47 delivers the
  reference-level half of that invariant (assertion 4); the source-level half belongs to the story
  that builds the check-shape catalogue.
