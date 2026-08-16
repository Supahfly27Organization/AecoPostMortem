namespace AecoPostMortem.Cli;

/// <summary>
/// Dispatch and exit codes. The writers are injected so the whole surface is testable in-process;
/// nothing here starts a child process or touches the console directly.
/// </summary>
public static class CommandRunner
{
    public const int Success = 0;
    public const int UnrecognisedCommand = 2;

    public static int Run(IReadOnlyList<string> arguments, TextWriter stdout, TextWriter stderr)
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

        // S-47 delivers the surface, not the behaviour. Reporting and exiting zero is the
        // specified behaviour, not a placeholder: FR-58 requires the surface to enumerate itself
        // before anything behind it exists.
        var command = invocation.Command!;
        stdout.WriteLine($"'{command.Name}' is not implemented yet; it arrives with {command.ArrivesWith}.");
        stdout.WriteLine($"When it does, its output goes to {command.OutputChannel}.");
        return Success;
    }
}
