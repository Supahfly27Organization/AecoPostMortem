# Patterns

## Optional dependency parameters as the testability seam

`CommandRunner.Run`'s `store` parameter (`AecoPostMortem.Cli/CommandRunner.cs`) defaults to
`LocalStore.AtDefaultLocation()`, the operator's real store; a test passes a throwaway one instead.
S-48 extended the same convention with two more optional parameters on the same method:
`copilotSessionStateRoot` (defaults to `CopilotSourceLocation.DefaultSessionStateRoot`, the real
`~/.copilot/session-state`) and `runHost` (defaults to blocking on `app.Run()`, the real behaviour
of `serve`). In each case, the default is what the operator actually gets; the parameter exists so
a test can substitute something throwaway or non-blocking without a second code path. Prefer this
over an interface/mock layer for a dependency that has exactly one real implementation.

## A host is built unstarted; the caller decides when to run it

`AecoPostMortem.Api.ApiHost.Build` returns a configured but unstarted `WebApplication`. This is
what makes it testable without a Kestrel listener staying up for the life of a test run: a test
calls `app.StartAsync()`, makes a request, then `app.StopAsync()`, all inside one `[Fact]`, rather
than the host owning its own lifetime. `AecoPostMortem.Cli.CommandRunner.Serve` is the one real
caller that runs it to completion (via `runHost`, above).

## Known Issues / Tech Debt

- The web client's `AppStateReport`/`AppStateKind` shapes (`web/src/api/appState.ts`) are hand-kept
  in sync with the server's `AecoPostMortem.Api.AppStateReport` — no generated client or shared
  schema exists yet. This has already drifted silently once (a missing JSON naming policy shipped
  `"EmptyStore"` instead of the `"emptyStore"` the frontend expected); see
  `AecoPostMortem.Api/CLAUDE.md`'s note on `ApiHostTests.The_kind_field_is_serialised_as_camelCase_
  on_the_wire`. Revisit if a second endpoint makes the hand-kept-contract cost outweigh generating
  one.
