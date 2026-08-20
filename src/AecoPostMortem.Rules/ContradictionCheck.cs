namespace AecoPostMortem.Rules;

/// <summary>
/// One pair of rule statements whose keyword polarity conflicts (FR-43, S-38, issue #47): one
/// states a directive and the other states its negation over the same wording. Pure candidate
/// detection — the caller decides what "in force together" means (one rule-set version's
/// deduplicated statements); this type carries no session, no version and no provenance, the same
/// plain-result discipline every other check shape in this project follows.
/// </summary>
public sealed record ContradictionCandidate
{
    public required RuleStatement First { get; init; }

    public required RuleStatement Second { get; init; }

    /// <summary>The shared, negation-stripped wording both statements reduced to — the literal
    /// text a reader can use to see why the two were flagged.</summary>
    public required string SharedWording { get; init; }
}

/// <summary>
/// FR-43's check shape: keyword-polarity contradiction detection over whatever statements the
/// caller hands in — scoping that set to one rule-set version is the caller's job (mirroring every
/// other check-shape in this project taking an already-resolved plain input, e.g.
/// <see cref="RuleSetVersionScope"/>'s own reasoning for a future adherence figure).
/// </summary>
/// <remarks>
/// <para>
/// <b>Self-match exclusion is the load-bearing requirement, not an optimisation</b> (FR-43's own
/// edge case): a keyword-polarity first pass returned a measured 4 candidates and all 4 were
/// spurious — three matched a statement against itself, because a prohibition contains the phrase
/// it prohibits ("do not use it" contains "use it"). <see cref="Run"/> only ever compares statement
/// <c>i</c> against statement <c>j</c> for <c>j &gt; i</c> — never <c>i</c> against itself, and
/// never the same pair twice in either order — so a statement can never be flagged against its own
/// text no matter how naturally its negated wording contains its own affirmative wording.
/// </para>
/// <para>
/// Two statements sharing the same polarity (both directives, or both prohibitions — including two
/// literally identical statements) never conflict here even if their wording is identical: polarity
/// must differ for a pair to be reported.
/// </para>
/// </remarks>
public static class ContradictionCheck
{
    /// <summary>
    /// Ordered longest-first so a longer marker ("does not"/"do not") is tried before a shorter one
    /// that could also match inside it, and so the search is a simple linear scan rather than a
    /// regex the operator's own free-form prose would need to be defended against.
    /// </summary>
    static readonly string[] NegationMarkers =
    [
        "does not ", "doesn't ", "should not ", "shouldn't ", "must not ", "mustn't ",
        "do not ", "don't ", "never ", "avoid ",
    ];

    public static IReadOnlyList<ContradictionCandidate> Run(IReadOnlyList<RuleStatement> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var candidates = new List<ContradictionCandidate>();

        for (var i = 0; i < statements.Count; i++)
        {
            for (var j = i + 1; j < statements.Count; j++)
            {
                if (TryGetSharedWording(statements[i], statements[j], out var sharedWording))
                {
                    candidates.Add(new ContradictionCandidate
                    {
                        First = statements[i],
                        Second = statements[j],
                        SharedWording = sharedWording,
                    });
                }
            }
        }

        return candidates;
    }

    static bool TryGetSharedWording(RuleStatement a, RuleStatement b, out string sharedWording)
    {
        sharedWording = "";

        var aNegated = TryStripNegation(a.Text, out var aCore);
        var bNegated = TryStripNegation(b.Text, out var bCore);

        // Polarity must differ: two prohibitions (or two directives) over the same wording agree
        // with each other, they do not conflict.
        if (aNegated == bNegated)
        {
            return false;
        }

        var normalizedA = Normalize(aCore);
        var normalizedB = Normalize(bCore);

        if (normalizedA.Length == 0 || !string.Equals(normalizedA, normalizedB, StringComparison.Ordinal))
        {
            return false;
        }

        sharedWording = aNegated ? aCore.Trim() : bCore.Trim();
        return true;
    }

    static bool TryStripNegation(string text, out string core)
    {
        foreach (var marker in NegationMarkers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                core = text.Remove(index, marker.Length);
                return true;
            }
        }

        core = text;
        return false;
    }

    static string Normalize(string text) =>
        text.Trim().TrimEnd('.', '!', '?').Trim().ToLowerInvariant();
}
