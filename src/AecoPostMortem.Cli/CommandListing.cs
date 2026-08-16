namespace AecoPostMortem.Cli;

/// <summary>Renders <see cref="CommandSurface.Commands"/>. FR-58 requires each command to appear
/// with its arguments and its output channel, so both come from the table rather than from prose.</summary>
public static class CommandListing
{
    public static void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("AecoPostMortem — reads GitHub Copilot CLI session logs and reports where a");
        writer.WriteLine("session diverged from the process it was given.");
        writer.WriteLine();
        writer.WriteLine("Usage: aecopostmortem <command> [arguments]");
        writer.WriteLine();

        var invocations = CommandSurface.Commands
            .Select(command => command.Arguments.Length == 0
                ? command.Name
                : $"{command.Name} {command.Arguments}")
            .ToArray();

        var width = invocations.Max(invocation => invocation.Length);

        foreach (var (command, invocation) in CommandSurface.Commands.Zip(invocations))
        {
            writer.WriteLine($"  {invocation.PadRight(width)}   {command.Summary}");
            writer.WriteLine($"  {new string(' ', width)}   output: {command.OutputChannel}");
            writer.WriteLine();
        }
    }
}
