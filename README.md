# AecoPostMortem

Session Post-Mortem reads GitHub Copilot CLI's own event logs and reports where a session
diverged from the process it was given: which written rules were followed, which were ignored,
where effort was wasted, and which capability was missing.

It runs entirely on your machine. Nothing it reads — your prompts, your source code, your rules —
leaves it: there is no network client anywhere in the ingestion path.

## Install

**Prerequisites**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (18+), only if you want to build the web shell — the CLI and API
  work without it, just without a UI to open in a browser
- GitHub Copilot CLI, previously run at least once, so there is something under
  `~/.copilot/session-state/` to read

**Get the code and build it**

```bash
git clone <this-repo-url>
cd AecoPostMortem
dotnet build
```

`dotnet build` builds every project in `AecoPostMortem.sln` — the CLI, the API host, and their
supporting libraries. There is no separate install step and no database to set up: the local
store is a single SQLite file created automatically the first time it's needed.

**Build the web shell** (optional, needed for `serve` to show a UI instead of just an API)

```bash
cd web
npm install
npm run build
cd ..
```

This produces `web/dist/`, which the CLI looks for at startup. If it isn't built, `serve` still
runs — you just get the API with no browser UI behind it.

## Run

Everything goes through one CLI. From the repo root:

```bash
dotnet run --project src/AecoPostMortem.Cli -- <command>
```

(Or run the built binary directly after `dotnet build`, from
`src/AecoPostMortem.Cli/bin/Debug/net10.0/AecoPostMortem.Cli`.)

Running it with no arguments lists the command surface:

| Command | Arguments | Does |
|---|---|---|
| `ingest` | `[path]` | Read Copilot's session state and store it locally. |
| `rebuild` | — | Re-derive the normalized and findings layers from what's already stored, without re-reading Copilot's logs. |
| `purge` | — | Delete the local store entirely. |
| `serve` | `[--port <n>]` | Start the local API and web shell (default port `48173`). Prints the URL, then blocks until you stop it. |

A command whose behavior hasn't shipped yet reports that plainly and exits successfully, rather
than failing — the surface is designed to enumerate itself before everything behind it exists.

**A typical first run:**

```bash
dotnet run --project src/AecoPostMortem.Cli -- serve
```

Open the printed URL (`http://127.0.0.1:48173` by default) in a browser. With nothing ingested
yet, the app tells you so and names the command that fixes it, rather than showing a blank page.

To point at a different port (e.g. if 48173 is taken):

```bash
dotnet run --project src/AecoPostMortem.Cli -- serve --port 5080
```

To start over:

```bash
dotnet run --project src/AecoPostMortem.Cli -- purge
```

Running it twice is safe — the second run reports nothing to purge rather than erroring.

## Use

Once you've ingested at least one Copilot session and started `serve`, the web app has three
surfaces:

- **Flight Recorder** — one session laid out as a time-ordered tape: prompts, tool calls, hooks,
  skills and subagent lanes, each step selectable down to the raw event that produced it. This is
  where you go the morning after a session that felt wrong, to find the exact moment it went off
  the rails.
- **Process Digest** — waste and rule-adherence findings ranked across your whole corpus, each
  one showing how many sessions it touched and what population the figure was drawn from. This is
  the "what's actually going wrong, repeatedly" view.
- **Rules Inventory** — every rule statement recovered from your `AGENTS.md` / `CLAUDE.md` /
  custom instructions, each with a status (watched, checkable-not-yet-built, not-checkable-with-a-
  reason, or not-a-rule) and the sessions/versions it was in force for. This is what stops an
  empty violation count from being mistaken for compliance.

Every figure the app shows carries its own provenance — Observed (read straight from the log),
Derived (computed from Observed data), or Inferred (a judgment call, always visibly marked as
one) — and no adherence number is ever shown without the rule-set version and resolution method
that produced it.

### Local data

| | |
|---|---|
| Store | SQLite file, owner-only permissions, no server or account |
| Location (Windows) | `%LOCALAPPDATA%\AecoPostMortem\store.db` |
| Location (macOS/Linux) | `~/.local/share/AecoPostMortem/store.db` |
| Migrations | Applied automatically on first use — there's no database command to run |
| Erasing everything | `dotnet run --project src/AecoPostMortem.Cli -- purge` |

## Running the tests

```bash
dotnet test
```

```bash
cd web && npm test
```
