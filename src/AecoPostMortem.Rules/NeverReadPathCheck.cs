namespace AecoPostMortem.Rules;

/// <summary>Plain input to <see cref="NeverReadPathCheck"/>: one rule statement's own text and the
/// one path it prohibits touching.</summary>
public sealed record NeverReadPathMention
{
    public required string SourceText { get; init; }

    public required string NamedPath { get; init; }
}

/// <summary>One banned-path mention that at least one real tool call touched, and how many times.
/// Unlike <see cref="BannedToolUsage"/>, there is no "unresolved" state here — a path operand always
/// produces a determinate answer against the corpus (accessed some number of times, including zero);
/// a mention that matched nothing simply produces no result, the same "no clean case reported" shape
/// <see cref="BannedToolCheck"/> already follows.</summary>
public sealed record NeverReadPathViolation
{
    public required string SourceText { get; init; }

    public required string NamedPath { get; init; }

    public required int AccessCount { get; init; }

    public required IReadOnlyList<string> SessionIds { get; init; }
}

/// <summary>
/// Piece 3's adherence check for <see cref="RuleShapeKind.NeverReadPath"/>: a prohibition names a
/// path, and the only adherence-worthy question is whether a real tool call touched a path matching
/// it. Matching is on a path-segment boundary, never a bare substring: real observed
/// <see cref="ReadEvent.Path"/> values are absolute, while a rule's own operand is typically a
/// relative fragment (this repository's own rule, "Never read
/// `src/AecoPostMortem.Data/Migrations/`", is itself an example) — an unqualified substring match
/// would let an operand like "Data" wrongly match a directory named "DataAccessLayer".
/// </summary>
public static class NeverReadPathCheck
{
    public static IReadOnlyList<NeverReadPathViolation> Run(
        IEnumerable<NeverReadPathMention> mentions,
        IEnumerable<ReadEvent> events)
    {
        ArgumentNullException.ThrowIfNull(mentions);
        ArgumentNullException.ThrowIfNull(events);

        var readEvents = events as IReadOnlyCollection<ReadEvent> ?? events.ToList();

        var results = new List<NeverReadPathViolation>();

        foreach (var mention in mentions)
        {
            var operand = NormalizeSegments(mention.NamedPath);
            var matches = readEvents.Where(readEvent => Matches(operand, NormalizeSegments(readEvent.Path))).ToArray();
            if (matches.Length == 0)
            {
                continue;
            }

            results.Add(new NeverReadPathViolation
            {
                SourceText = mention.SourceText,
                NamedPath = mention.NamedPath,
                AccessCount = matches.Length,
                SessionIds = matches.Select(readEvent => readEvent.SessionId).Distinct().Order(StringComparer.Ordinal).ToArray(),
            });
        }

        return results;
    }

    static string NormalizeSegments(string path) => path.Replace('\\', '/').Trim('/');

    /// <summary>A normalized observed path matches a normalized operand when the operand appears
    /// whole, aligned on a '/' boundary at both ends (or the string's own start/end) — as the whole
    /// path, a leading segment run, a trailing segment run, or a segment run in the middle. Never a
    /// bare <see cref="string.Contains(string)"/>. Case-insensitive: real observed paths are Windows
    /// filesystem paths, and Windows filesystems are case-insensitive — the same reasoning
    /// <c>Ingestion.SessionExclusion</c>'s own path-prefix matcher already follows.</summary>
    static bool Matches(string operand, string observed) =>
        observed.Equals(operand, StringComparison.OrdinalIgnoreCase)
        || observed.StartsWith(operand + "/", StringComparison.OrdinalIgnoreCase)
        || observed.EndsWith("/" + operand, StringComparison.OrdinalIgnoreCase)
        || observed.Contains("/" + operand + "/", StringComparison.OrdinalIgnoreCase);
}
