namespace AecoPostMortem.Rules;

/// <summary>
/// Scenario 1 of issue #29 (FR-19): the phase vocabulary and its ordering, both derived from the
/// corpus rather than hard-coded — the same shape S-21 (issue #34) established for the tool
/// vocabulary (<see cref="ToolVocabulary"/>), applied to declared-intent phase labels instead of
/// tool names. "An earlier phase" has no meaning without an ordering, and neither the vocabulary nor
/// the ordering may be a list named in source code, because the next machine's corpus declares
/// different phases in a different sequence.
/// </summary>
public static class PhaseOrdering
{
    /// <summary>
    /// The distinct phases declared in <paramref name="intents"/>, ordered by the corpus-wide
    /// sequence at which each was first declared — across every session, not within one, per FR-19.
    /// A phase declared early in one session and late in another is ordered by whichever declaration
    /// came first.
    /// </summary>
    public static IReadOnlyList<string> Derive(IEnumerable<DeclaredIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        return intents
            .GroupBy(intent => intent.Phase, StringComparer.Ordinal)
            .Select(group => (Phase: group.Key, FirstDeclared: group.Min(intent => intent.Sequence)))
            .OrderBy(entry => entry.FirstDeclared)
            .ThenBy(entry => entry.Phase, StringComparer.Ordinal)
            .Select(entry => entry.Phase)
            .ToArray();
    }
}
