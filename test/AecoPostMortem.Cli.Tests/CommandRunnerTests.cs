using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AecoPostMortem.Api;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AecoPostMortem.Cli.Tests;

public sealed class CommandRunnerTests
{
    static (int ExitCode, string Stdout, string Stderr) Run(params string[] arguments) =>
        Run(store: null, arguments);

    static (int ExitCode, string Stdout, string Stderr) Run(LocalStore? store, params string[] arguments)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CommandRunner.Run(arguments, stdout, stderr, store);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Drives <c>serve</c> with a fake <c>runHost</c> so the test controls the host's
    /// lifetime instead of blocking forever on the real <c>app.Run()</c>, and a throwaway Copilot
    /// root so the result does not depend on whatever is really on the machine running the test.</summary>
    static (int ExitCode, string Stdout, string Stderr) RunServe(
        LocalStore store,
        Func<WebApplication, TextWriter, int> runHost,
        string? copilotSessionStateRoot = null,
        params string[] portArguments)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var arguments = new[] { "serve" }.Concat(portArguments).ToArray();
        var exitCode = CommandRunner.Run(
            arguments,
            stdout,
            stderr,
            store,
            runHost,
            copilotSessionStateRoot ?? Path.Combine(Path.GetTempPath(), "no-such-copilot-root"));
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void With_no_arguments_it_lists_the_commands_on_stdout_and_succeeds()
    {
        var (exitCode, stdout, stderr) = Run();

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("ingest", stdout);
        Assert.Contains("rebuild", stdout);
        Assert.Contains("purge", stdout);
        Assert.Contains("serve", stdout);
        Assert.Equal(string.Empty, stderr);
    }

    /// <summary>Scenario "the three surfaces are routable" (issue #11) starts with the host coming
    /// up at all: the URL goes to stdout, per its <see cref="CommandSpec.OutputChannel"/>, before
    /// the host runs.</summary>
    [Fact]
    public void Serve_prints_the_listening_URL_and_runs_the_host()
    {
        using var temporary = new TemporaryStore();
        WebApplication? captured = null;

        var (exitCode, stdout, stderr) = RunServe(
            temporary.Store,
            (app, _) =>
            {
                captured = app;
                return CommandRunner.Success;
            },
            portArguments: ["--port", "0"]);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.StartsWith("http://127.0.0.1:", stdout, StringComparison.Ordinal);
        Assert.NotNull(captured);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void Serve_defaults_to_the_documented_default_port_when_none_is_given()
    {
        using var temporary = new TemporaryStore();

        var (exitCode, stdout, _) = RunServe(temporary.Store, (_, _) => CommandRunner.Success);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains($":{CommandRunner.DefaultPort}", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Serve_accepts_an_explicit_port()
    {
        using var temporary = new TemporaryStore();

        var (exitCode, stdout, _) = RunServe(
            temporary.Store, (_, _) => CommandRunner.Success, portArguments: ["--port", "54321"]);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains(":54321", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Serve_rejects_a_non_numeric_port_and_never_builds_a_host()
    {
        using var temporary = new TemporaryStore();
        var hostBuilt = false;

        var (exitCode, stdout, stderr) = RunServe(
            temporary.Store,
            (_, _) =>
            {
                hostBuilt = true;
                return CommandRunner.Success;
            },
            portArguments: ["--port", "not-a-number"]);

        Assert.Equal(CommandRunner.InvalidArguments, exitCode);
        Assert.Contains("--port", stderr, StringComparison.Ordinal);
        Assert.False(hostBuilt);
        Assert.Equal(string.Empty, stdout);
    }

    /// <summary>Scenario "with no Copilot directory, the app says that instead" (issue #11): the
    /// endpoint the web shell reads is reachable through the exact host <c>serve</c> builds, not a
    /// separate one constructed by the test.</summary>
    [Fact]
    public void The_app_state_endpoint_is_reachable_through_the_host_serve_builds()
    {
        using var temporary = new TemporaryStore();
        AppStateReport? report = null;

        var (exitCode, _, _) = RunServe(
            temporary.Store,
            (app, _) =>
            {
                app.Start();
                try
                {
                    var address = app.Services.GetRequiredService<IServer>()
                        .Features.Get<IServerAddressesFeature>()!.Addresses.First();

                    using var client = new HttpClient { BaseAddress = new Uri(address, UriKind.Absolute) };
                    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                    };

                    report = client.GetFromJsonAsync<AppStateReport>(ApiHost.AppStateRoute, options)
                        .GetAwaiter().GetResult();
                }
                finally
                {
                    app.StopAsync().GetAwaiter().GetResult();
                }

                return CommandRunner.Success;
            },
            portArguments: ["--port", "0"]);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.NotNull(report);
        Assert.Equal(AppStateKind.NoSourceFound, report!.Kind);
    }

    /// <summary>FR-1: a missing Copilot directory is reported, not thrown — the coverage report
    /// simply states zero sessions found rather than the command failing.</summary>
    [Fact]
    public void Ingest_against_a_missing_Copilot_root_reports_zero_sessions_and_succeeds()
    {
        using var temporary = new TemporaryStore();
        var missingRoot = Path.Combine(Path.GetTempPath(), "AecoPostMortem.Tests", "no-such-copilot-root");

        var (exitCode, stdout, stderr) = RunIngest(temporary.Store, missingRoot);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("Sessions found: 0", stdout, StringComparison.Ordinal);
        Assert.Contains("Sessions ingested: 0", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);
    }

    /// <summary>FR-58's stated output channel for `ingest` — "stdout, the coverage report" — proven
    /// against a real session directory read through the whole path: discovery, RAW persistence
    /// (<see cref="IngestionRun.Run"/>) and the report <c>CommandRunner.Ingest</c> writes.</summary>
    [Fact]
    public void Ingest_persists_a_real_session_and_reports_its_coverage()
    {
        using var temporary = new TemporaryStore();
        using var sessionState = new TemporarySessionState();
        sessionState.WriteEventsFile(
            "session-1",
            """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1}}""",
            """{"type":"assistant.turn_start","timestamp":"2026-05-07T14:16:49.000Z"}""");

        var (exitCode, stdout, stderr) = RunIngest(temporary.Store, sessionState.Root);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("Sessions found: 1", stdout, StringComparison.Ordinal);
        Assert.Contains("Sessions ingested: 1", stdout, StringComparison.Ordinal);
        Assert.Contains("Lines parsed: 2", stdout, StringComparison.Ordinal);
        Assert.Contains("session.start: 1", stdout, StringComparison.Ordinal);
        Assert.Contains("assistant.turn_start: 1", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);

        using var reopened = temporary.Store.Open();
        Assert.Equal(2, reopened.RawEvents.Count());
    }

    /// <summary>A positional path argument overrides the default Copilot root, the same way
    /// <c>--port</c> overrides <c>serve</c>'s default — <c>CommandSurfaceTests.
    /// Ingest_takes_an_optional_path_and_serve_an_optional_port</c> is the structural half of this
    /// claim; this is the behavioural half.</summary>
    [Fact]
    public void Ingest_uses_a_given_path_argument_instead_of_the_default_root()
    {
        using var temporary = new TemporaryStore();
        using var sessionState = new TemporarySessionState();
        sessionState.WriteEventsFile(
            "session-1",
            """{"type":"session.start","timestamp":"2026-05-07T14:16:48.682Z","data":{"copilotVersion":"1.0.40","version":1}}""");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CommandRunner.Run(
            ["ingest", sessionState.Root],
            stdout,
            stderr,
            temporary.Store,
            runHost: null,
            copilotSessionStateRoot: Path.Combine(Path.GetTempPath(), "AecoPostMortem.Tests", "not-this-one"));

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("Sessions found: 1", stdout.ToString(), StringComparison.Ordinal);
    }

    static (int ExitCode, string Stdout, string Stderr) RunIngest(LocalStore store, string sessionStateRoot)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CommandRunner.Run(
            ["ingest"], stdout, stderr, store, runHost: null, copilotSessionStateRoot: sessionStateRoot);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>A throwaway Copilot session-state root: <c>ingest</c>'s default is the real
    /// machine's own directory, and a test reading it would make the test's outcome depend on
    /// whatever the machine running it happens to have ingested Copilot sessions for.</summary>
    sealed class TemporarySessionState : IDisposable
    {
        public TemporarySessionState()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "AecoPostMortem.Tests",
                Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WriteEventsFile(string sessionId, params string[] lines)
        {
            var directory = Path.Combine(Root, sessionId);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "events.jsonl"), string.Join('\n', lines) + '\n');
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Never created, or already gone.
            }
        }
    }

    [Fact]
    public void Purge_deletes_the_store_and_says_what_it_deleted()
    {
        using var temporary = new TemporaryStore();
        using (temporary.Store.Open())
        {
        }

        var (exitCode, stdout, stderr) = Run(temporary.Store, "purge");

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains(temporary.Store.FilePath, stdout, StringComparison.Ordinal);
        Assert.Contains("Purged", stdout, StringComparison.Ordinal);
        Assert.False(temporary.Store.Exists);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void Purging_when_there_is_nothing_to_purge_reports_that_and_exits_zero()
    {
        using var temporary = new TemporaryStore();

        var (exitCode, stdout, stderr) = Run(temporary.Store, "purge");

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("Nothing to purge", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);
    }

    /// <summary>Scenario "The operator can invoke the rebuild" (issue #24): the derived layer is
    /// dropped and re-derived, and RAW — what it is re-derived from — is unchanged.</summary>
    [Fact]
    public void Rebuild_drops_the_derived_layer_and_leaves_RAW_unchanged_and_reports_it()
    {
        using var temporary = new TemporaryStore();

        using (var context = temporary.Store.Open())
        {
            context.RawEvents.Add(new RawEvent(
                "session-1", 0, "session.start", "2026-08-09T20:14:36.758Z", "0.0.339",
                @"~/.copilot/session-state/session-1/events.jsonl", 0,
                RawPayload.ContentHashOfText("{}"), "{}"));

            context.Sessions.Add(new Session
            {
                SessionId = "session-1",
                StartedAt = "2026-08-09T20:14:36.758Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/session-1/events.jsonl",
                Cwd = @"C:\repo",
            });

            context.SaveChanges();
        }

        var (exitCode, stdout, stderr) = Run(temporary.Store, "rebuild");

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("Rebuilt", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);

        using var reopened = temporary.Store.Open();
        Assert.Single(reopened.RawEvents);

        // The hand-seeded Session (Cwd = C:\repo) is gone — rebuild re-derives from RAW alone, and
        // RAW's own payload here ("{}") carries no context at all, so the freshly derived row picks
        // up SessionBuilder's own defaults rather than the stale hand-seeded value.
        var session = Assert.Single(reopened.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.Equal(string.Empty, session.Cwd);
    }

    /// <summary>Rebuild's <see cref="CommandSpec"/> takes no arguments (see
    /// <c>CommandSurfaceTests.Ingest_takes_an_optional_path_and_serve_an_optional_port</c> for the
    /// commands that do) — there is structurally nowhere for a source directory to be passed, so
    /// "the source directory is not read" holds without a runtime check.</summary>
    [Fact]
    public void Rebuild_takes_no_source_directory_argument_so_there_is_none_to_read()
    {
        // Used to assert `Arguments` was empty outright, which stopped being the expression of this
        // invariant once `--store <path>` became global. The invariant itself is unchanged and is
        // what is asserted now: `rebuild` names no source directory (S-46, issue #24 — repopulation
        // reads only RAW, already in the store). A store to open is not a source to read.
        var arguments = CommandSurface.Find("rebuild")!.Arguments;

        Assert.Equal("[--store <path>]", arguments);
        Assert.DoesNotContain("[path]", arguments, StringComparison.Ordinal);
    }

    /// <summary>A store in a throwaway directory: the CLI's default is the operator's real store,
    /// and a test that purged it would be the one thing FR-11 exists to prevent.</summary>
    sealed class TemporaryStore : IDisposable
    {
        readonly string folder;

        public TemporaryStore()
        {
            folder = Path.Combine(
                Path.GetTempPath(),
                "AecoPostMortem.Tests",
                Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

            Store = new LocalStore(Path.Combine(folder, StoreLocation.FileName));
        }

        public LocalStore Store { get; }

        public void Dispose()
        {
            Store.Purge();

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Never created, or already gone.
            }
        }
    }

    /// <summary>
    /// `--store <path>` opens the store at that path instead of FR-11's documented per-user default.
    /// Proven end to end (the wiring, not the parsing — `CommandParserTests` owns that) by the store
    /// file appearing at the requested path.
    ///
    /// **This test is deliberately written so that a broken flag is harmless, not merely detected.**
    /// It passes no injected `store` — that parameter is the outermost override and would mask the
    /// flag entirely — which means a regression sends the command to the operator's *real* store. So
    /// the command is `ingest` over an **empty** session-state root: with the flag broken it opens
    /// the real store, finds nothing to ingest and writes nothing, while the assertion below still
    /// fails loudly. A destructive command (`purge`) or a non-empty root would each have made a
    /// regression in this flag destroy or pollute real data — which is exactly what happened once
    /// while this test was first being written, and is why it is shaped this way.
    /// </summary>
    [Fact]
    public void A_store_option_opens_the_store_at_that_path_instead_of_the_default_location()
    {
        using var sessionState = new TemporarySessionState();
        var folder = Path.Combine(
            Path.GetTempPath(), "AecoPostMortem.Tests", Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
        var elsewhere = Path.Combine(folder, StoreLocation.FileName);

        try
        {
            var (exitCode, _, stderr) = Run("ingest", "--store", elsewhere, sessionState.Root);

            Assert.Equal(CommandRunner.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(File.Exists(elsewhere), $"no store was created at {elsewhere}");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void The_store_option_with_no_value_is_rejected_before_anything_runs()
    {
        var (exitCode, stdout, stderr) = Run("purge", "--store");

        Assert.Equal(CommandRunner.InvalidArguments, exitCode);
        Assert.Contains("--store", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public void An_unknown_command_goes_to_stderr_with_a_non_zero_exit_code()
    {
        var (exitCode, stdout, stderr) = Run("digest");

        Assert.Equal(CommandRunner.UnrecognisedCommand, exitCode);
        Assert.Contains("digest", stderr);
        Assert.Contains("ingest", stderr);
        Assert.Equal(string.Empty, stdout);
    }
}
