using System.Globalization;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Cli;

/// <summary>
/// Dispatch and exit codes. The writers and the store are injected so the whole surface is testable
/// in-process; nothing here starts a child process or touches the console directly.
/// </summary>
public static class CommandRunner
{
    public const int Success = 0;
    public const int UnrecognisedCommand = 2;

    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter stdout,
        TextWriter stderr,
        LocalStore? store = null)
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

        if (string.Equals(command.Name, "purge", StringComparison.Ordinal))
        {
            return Purge(store ?? LocalStore.AtDefaultLocation(), stdout);
        }

        if (string.Equals(command.Name, "rebuild", StringComparison.Ordinal))
        {
            return Rebuild(store ?? LocalStore.AtDefaultLocation(), stdout);
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
    /// S-46's rebuild: drop and re-derive the NORMALIZED and FINDINGS layers from RAW
    /// (<see cref="DerivedSchema.Rebuild"/>). <paramref name="store"/> is opened and RAW is only
    /// read to report the count the derivation ran against — nothing here accepts or reads a source
    /// directory, so that requirement holds by construction rather than by a check.
    /// </summary>
    static int Rebuild(LocalStore store, TextWriter stdout)
    {
        using var context = store.Open();

        var rawEventCount = context.RawEvents.Count();

        DerivedSchema.Rebuild(context);

        stdout.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Rebuilt the derived layer from {rawEventCount:N0} RAW event(s); RAW is unchanged."));

        return Success;
    }
}
