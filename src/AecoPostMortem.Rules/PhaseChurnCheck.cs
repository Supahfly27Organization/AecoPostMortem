namespace AecoPostMortem.Rules;

/// <summary>
/// One session's phase-churn result: how many times it returned to a phase it had already moved
/// past under the corpus-derived ordering, against how many phases it declared in total (Scenario 4
/// of issue #29) — its own denominator, never the corpus-wide total, because a measured 104 returns
/// across 352 intents in the worst session would otherwise swamp every shorter session. Carries the
/// <see cref="Vocabulary"/> and ordering that produced it (Scenario 2), so a result never renders
/// without the derivation that could make two implementations disagree.
/// </summary>
public sealed record PhaseChurnResult
{
    public required string SessionId { get; init; }

    public required int Returns { get; init; }

    public required int TotalIntents { get; init; }

    public required IReadOnlyList<string> Vocabulary { get; init; }
}

/// <summary>
/// FR-19's check shape: Scenarios 1, 2, 3 and 4 of issue #29. Detects each session's returns to an
/// earlier phase under an ordering derived fresh from the corpus passed in, normalised by that
/// session's own total. A session that declares no intents contributes no
/// <see cref="PhaseChurnResult"/> at all — grouping is over the intents themselves, so an absent
/// session never appears as a zero (the edge case named in issue #29). Deciding which of these
/// results are worth surfacing as a finding — e.g. only sessions that actually churned — is
/// <c>AecoPostMortem.Findings</c>'s job, not this one's, the same split
/// <c>FailedToolCallsCheck</c>'s own CLAUDE.md entry documents.
/// </summary>
public static class PhaseChurnCheck
{
    public static IReadOnlyList<PhaseChurnResult> Run(IEnumerable<DeclaredIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        var corpus = intents as IReadOnlyCollection<DeclaredIntent> ?? intents.ToArray();
        var vocabulary = PhaseOrdering.Derive(corpus);
        var position = vocabulary
            .Select((phase, index) => (phase, index))
            .ToDictionary(entry => entry.phase, entry => entry.index, StringComparer.Ordinal);

        return corpus
            .GroupBy(intent => intent.SessionId, StringComparer.Ordinal)
            .Select(session => Evaluate(session.Key, session, position, vocabulary))
            .OrderBy(result => result.SessionId, StringComparer.Ordinal)
            .ToArray();
    }

    static PhaseChurnResult Evaluate(
        string sessionId,
        IEnumerable<DeclaredIntent> sessionIntents,
        IReadOnlyDictionary<string, int> position,
        IReadOnlyList<string> vocabulary)
    {
        var ordered = sessionIntents.OrderBy(intent => intent.Sequence).ToArray();
        var highestReached = -1;
        var returns = 0;

        foreach (var intent in ordered)
        {
            var index = position[intent.Phase];

            if (index < highestReached)
            {
                returns++;
            }
            else
            {
                highestReached = index;
            }
        }

        return new PhaseChurnResult
        {
            SessionId = sessionId,
            Returns = returns,
            TotalIntents = ordered.Length,
            Vocabulary = vocabulary,
        };
    }
}
