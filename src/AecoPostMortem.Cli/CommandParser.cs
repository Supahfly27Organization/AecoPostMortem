namespace AecoPostMortem.Cli;

/// <summary>
/// What the operator asked for. Exactly one of the three states holds: a command, an unrecognised
/// word, or a request for the listing.
/// </summary>
public sealed record ParsedInvocation(
    CommandSpec? Command,
    string? UnrecognisedName,
    IReadOnlyList<string> Arguments,
    string? StorePath = null,
    string? OptionError = null)
{
    public bool ShowsListing => Command is null && UnrecognisedName is null;
}

/// <summary>Pure: no console, no environment, no file system. That is what makes it testable.</summary>
public static class CommandParser
{
    static readonly string[] HelpWords = ["help", "--help", "-h", "-?", "/?"];

    /// <summary>The one global option: which store to open, overriding FR-11's documented per-user
    /// default (<see cref="AecoPostMortem.Data.StoreLocation.Default"/>). Global rather than
    /// per-command because every command that exists opens the store, and a flag that meant
    /// different things — or nothing — depending on the verb would be its own trap.</summary>
    public const string StoreOption = "--store";

    public static ParsedInvocation Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var words = arguments.Where(word => !string.IsNullOrWhiteSpace(word)).ToArray();

        if (words.Length == 0 || HelpWords.Contains(words[0], StringComparer.OrdinalIgnoreCase))
        {
            return new ParsedInvocation(null, null, []);
        }

        var command = CommandSurface.Find(words[0]);

        if (command is null)
        {
            return new ParsedInvocation(null, words[0], []);
        }

        var (storePath, remaining, error) = TakeStoreOption(words[1..]);

        return new ParsedInvocation(command, null, remaining, storePath, error);
    }

    /// <summary>
    /// Lifts <see cref="StoreOption"/> and its value out of a command's own arguments, preserving
    /// the order of everything else. Taking it out is load-bearing, not tidiness: <c>ingest</c>
    /// reads <c>Arguments[0]</c> as its session-state root, so a flag left in place would be read as
    /// that path. A repeated flag takes the last one, matching how a shell user expects a later
    /// argument to win.
    /// </summary>
    static (string? StorePath, IReadOnlyList<string> Remaining, string? Error) TakeStoreOption(
        IReadOnlyList<string> arguments)
    {
        string? storePath = null;
        var remaining = new List<string>(arguments.Count);

        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], StoreOption, StringComparison.Ordinal))
            {
                remaining.Add(arguments[index]);
                continue;
            }

            if (index + 1 >= arguments.Count)
            {
                return (null, remaining, $"'{StoreOption}' requires a path.");
            }

            storePath = arguments[index + 1];
            index++;
        }

        return (storePath, remaining, null);
    }
}
