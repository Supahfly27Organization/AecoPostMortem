using System.Globalization;
using AecoPostMortem.Api;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Ingestion;
using Microsoft.AspNetCore.Builder;

namespace AecoPostMortem.Cli;

/// <summary>
/// Dispatch and exit codes. The writers and the store are injected so the whole surface is testable
/// in-process; nothing here starts a child process or touches the console directly.
/// </summary>
public static class CommandRunner
{
    public const int Success = 0;
    public const int UnrecognisedCommand = 2;
    public const int InvalidArguments = 3;

    /// <summary>FR-58: "a stated default port" — stated here, and in <see cref="CommandListing"/>
    /// through <see cref="CommandSurface"/>'s own summary text.</summary>
    public const int DefaultPort = 48173;

    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter stdout,
        TextWriter stderr,
        LocalStore? store = null,
        Func<WebApplication, TextWriter, int>? runHost = null,
        string? copilotSessionStateRoot = null)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var invocation = CommandParser.Parse(arguments);

        if (invocation.UnrecognisedName is { } unrecognised)
        {
            stderr.WriteLine($"Unrecognised command '{unrecognised}'.");
            stderr.WriteLine();
            CommandListing.Write(stderr);
            return UnrecognisedCommand;
        }

        if (invocation.ShowsListing)
        {
            CommandListing.Write(stdout);
            return Success;
        }

        var command = invocation.Command!;

        if (string.Equals(command.Name, "ingest", StringComparison.Ordinal))
        {
            return Ingest(
                store ?? LocalStore.AtDefaultLocation(),
                copilotSessionStateRoot ?? CopilotSourceLocation.DefaultSessionStateRoot,
                invocation,
                stdout);
        }

        if (string.Equals(command.Name, "purge", StringComparison.Ordinal))
        {
            return Purge(store ?? LocalStore.AtDefaultLocation(), stdout);
        }

        if (string.Equals(command.Name, "rebuild", StringComparison.Ordinal))
        {
            return Rebuild(store ?? LocalStore.AtDefaultLocation(), stdout);
        }

        if (string.Equals(command.Name, "serve", StringComparison.Ordinal))
        {
            return Serve(
                store ?? LocalStore.AtDefaultLocation(),
                copilotSessionStateRoot ?? CopilotSourceLocation.DefaultSessionStateRoot,
                invocation,
                stdout,
                stderr,
                runHost);
        }

        // The surface enumerates itself before everything behind it exists (FR-58). Reporting and
        // exiting zero is the specified behaviour for a command whose story has not landed, not a
        // placeholder.
        stdout.WriteLine($"'{command.Name}' is not implemented yet; it arrives with {command.ArrivesWith}.");
        stdout.WriteLine($"When it does, its output goes to {command.OutputChannel}.");
        return Success;
    }

    /// <summary>
    /// FR-11's purge. Nothing to purge is reported and exits zero: the operator asked for the store
    /// to be gone, and it is gone either way.
    /// </summary>
    static int Purge(LocalStore store, TextWriter stdout)
    {
        var outcome = store.Purge();

        if (!outcome.DeletedAnything)
        {
            stdout.WriteLine($"Nothing to purge; there is no store at {store.FilePath}.");
            return Success;
        }

        foreach (var file in outcome.Deleted)
        {
            stdout.WriteLine($"Deleted {file}");
        }

        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Purged {outcome.Deleted.Count} file(s), {outcome.BytesReclaimed:N0} bytes."));

        return Success;
    }

    /// <summary>
    /// FR-58/FR-14: reads the Copilot session-state root — an optional positional path
    /// (<see cref="CommandSpec.Arguments"/>'s <c>"[path]"</c>) overrides the stated default the same
    /// way <c>serve</c>'s own <paramref name="copilotSessionStateRoot"/> does — through
    /// <see cref="IngestionRun.Run"/> and writes the resulting <see cref="CoverageReport"/> to
    /// stdout, <c>ingest</c>'s own <see cref="CommandSpec.OutputChannel"/>. The exclusion list is
    /// loaded from beside the store actually being opened (<see cref="LocalStore.Folder"/>), not
    /// from the hard-coded default folder <see cref="ExclusionListSource.DefaultPath"/> resolves to
    /// — so a test store's run never depends on whatever exclusions the real machine has configured,
    /// the same isolation <paramref name="store"/> and <paramref name="copilotSessionStateRoot"/>
    /// already give the rest of this command's dependencies.
    /// </summary>
    static int Ingest(
        LocalStore store,
        string copilotSessionStateRoot,
        ParsedInvocation invocation,
        TextWriter stdout)
    {
        var sessionStateRoot = invocation.Arguments.Count > 0
            ? invocation.Arguments[0]
            : copilotSessionStateRoot;

        var excludedRoots = ExclusionListSource.Load(
            Path.Combine(store.Folder, ExclusionListSource.FileName));

        using var context = store.Open();

        var report = IngestionRun.Run(context, sessionStateRoot, excludedRoots);

        WriteCoverageReport(report, stdout);

        return Success;
    }

    static void WriteCoverageReport(CoverageReport report, TextWriter stdout)
    {
        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"Sessions found: {report.SessionsFound}"));
        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"Sessions ingested: {report.SessionsIngested}"));
        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"Sessions excluded: {report.SessionsExcluded.Count}"));

        foreach (var excluded in report.SessionsExcluded)
        {
            stdout.WriteLine($"  {excluded.SessionId}: {excluded.Reason}");
        }

        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"Lines parsed: {report.LinesParsed}"));
        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"Lines skipped: {report.LinesSkipped}"));

        stdout.WriteLine("Events by type:");

        if (report.EventsByType.Count == 0)
        {
            stdout.WriteLine("  none");
        }

        foreach (var eventType in report.EventsByType.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            stdout.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"  {eventType.Key}: {eventType.Value}"));
        }
    }

    /// <summary>
    /// S-46's rebuild: drop and re-derive the NORMALIZED and FINDINGS layers from RAW
    /// (<see cref="DerivedSchema.Rebuild"/>), then re-populate the six tables
    /// <see cref="NormalizedLayerWriter"/> owns for every session RAW still holds — the source
    /// directory is never read, so "re-derived from RAW alone" holds by construction rather than by
    /// a check.
    /// </summary>
    static int Rebuild(LocalStore store, TextWriter stdout)
    {
        using var context = store.Open();

        var rawEventCount = context.RawEvents.Count();

        DerivedSchema.Rebuild(context);

        var sessionIds = context.RawEvents.Select(raw => raw.SessionId).Distinct().ToList();
        foreach (var sessionId in sessionIds)
        {
            NormalizedLayerWriter.Derive(context, sessionId);
        }

        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Rebuilt the derived layer from {rawEventCount:N0} RAW event(s); RAW is unchanged."));

        return Success;
    }

    /// <summary>
    /// S-48: builds the API host and the web shell and starts it on the requested (or the stated
    /// default) port. The URL is written to stdout before the host runs, per its
    /// <see cref="CommandSpec.OutputChannel"/> — the operator sees where to point a browser even
    /// though the process then blocks until it is stopped. <paramref name="runHost"/> defaults to
    /// running the host until shutdown (real usage); a test supplies its own so a request can be
    /// made against the running app and the host stopped again, without the test itself blocking.
    /// <paramref name="copilotSessionStateRoot"/> defaults to the real machine's Copilot directory
    /// the same way <paramref name="store"/> defaults to the real store — a test overrides it for
    /// the same reason `CommandRunner.Run`'s own optional <c>store</c> parameter exists.
    /// </summary>
    static int Serve(
        LocalStore store,
        string copilotSessionStateRoot,
        ParsedInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        Func<WebApplication, TextWriter, int>? runHost)
    {
        if (!TryParsePort(invocation.Arguments, out var port, out var parseError))
        {
            stderr.WriteLine(parseError);
            return InvalidArguments;
        }

        // Synchronous `using`, not `await using`: CommandRunner.Run's whole call chain is
        // synchronous by design (Program.Main has no async signature to await through), and
        // WebApplication.Dispose() on an already-stopped host is safe.
        using var app = ApiHost.Build(store, copilotSessionStateRoot, port, ServeWebRoot.Resolve());

        stdout.WriteLine($"http://127.0.0.1:{port}");

        return (runHost ?? RunUntilShutdown)(app, stdout);
    }

    static int RunUntilShutdown(WebApplication app, TextWriter stdout)
    {
        app.Run();
        return Success;
    }

    /// <summary>Parses <c>--port &lt;n&gt;</c> off <c>serve</c>'s own arguments
    /// (<see cref="CommandSurface"/>'s <c>"[--port &lt;n&gt;]"</c>). No flag present is not an
    /// error — it is <see cref="DefaultPort"/>, the "stated default port" FR-58 asks for.</summary>
    static bool TryParsePort(IReadOnlyList<string> arguments, out int port, out string? error)
    {
        port = DefaultPort;
        error = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--port", StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= arguments.Count)
            {
                error = "'--port' requires a value.";
                return false;
            }

            // 0 is accepted deliberately, the same as `dotnet run --urls http://localhost:0`: it
            // asks the OS for an ephemeral port rather than naming one, which is what lets a test
            // run this command without claiming a fixed port another test might also want.
            var value = arguments[index + 1];
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed is < 0 or > 65535)
            {
                error = $"'--port' must be a number between 0 and 65535; got '{value}'.";
                return false;
            }

            port = parsed;
            return true;
        }

        return true;
    }
}
