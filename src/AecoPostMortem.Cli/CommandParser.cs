namespace AecoPostMortem.Cli;

/// <summary>
/// What the operator asked for. Exactly one of the three states holds: a command, an unrecognised
/// word, or a request for the listing.
/// </summary>
public sealed record ParsedInvocation(
    CommandSpec? Command,
    string? UnrecognisedName,
    IReadOnlyList<string> Arguments)
{
    public bool ShowsListing => Command is null && UnrecognisedName is null;
}

/// <summary>Pure: no console, no environment, no file system. That is what makes it testable.</summary>
public static class CommandParser
{
    static readonly string[] HelpWords = ["help", "--help", "-h", "-?", "/?"];

    public static ParsedInvocation Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var words = arguments.Where(word => !string.IsNullOrWhiteSpace(word)).ToArray();

        if (words.Length == 0 || HelpWords.Contains(words[0], StringComparer.OrdinalIgnoreCase))
        {
            return new ParsedInvocation(null, null, []);
        }

        var command = CommandSurface.Find(words[0]);

        return command is null
            ? new ParsedInvocation(null, words[0], [])
            : new ParsedInvocation(command, null, words[1..]);
    }
}
