namespace AecoPostMortem.Rules;

/// <summary>
/// FR-39 (S-35, issue #43): the Monitor comparison's adjacency refusal. Two rule-set versions are
/// comparable only when nothing sits between them in that repository's own chronological order —
/// comparing versions with an edit sitting between them would silently absorb whatever that edit
/// changed into a two-point comparison that never saw it, the same reasoning
/// <see cref="RuleSetVersionScope.RequireSingleVersion"/> gives for refusing a figure that spans a
/// rule edit rather than averaging across it. This is the reusable primitive the Monitor comparison
/// scopes itself with before computing anything, the exact role
/// <see cref="RuleSetVersionScope.RequireSingleVersion"/>'s own remarks anticipated for it.
/// </summary>
public static class RuleSetVersionAdjacency
{
    /// <summary>
    /// Confirms <paramref name="before"/> and <paramref name="after"/> name the same repository and
    /// sit immediately next to each other within <paramref name="versions"/> — that repository's own
    /// versions, in whatever order <see cref="RuleSetVersioning.Compute"/> produced them, ordered
    /// here by each version's own <see cref="RuleSetVersion.FirstSessionStartedAt"/>, tied-broken by
    /// <see cref="RuleSetVersion.FirstSessionId"/> for a total order regardless of arrival order —
    /// and returns the two full <see cref="RuleSetVersion"/> values (identity, window and session
    /// count) so a caller never has to look either back up by hash.
    /// </summary>
    /// <exception cref="MixedRuleSetVersionException"><paramref name="before"/> and
    /// <paramref name="after"/> name different repositories — there is no single chronological order
    /// to place them in.</exception>
    /// <exception cref="UnknownRuleSetVersionException">Either id names a version
    /// <paramref name="versions"/> does not contain.</exception>
    /// <exception cref="NonAdjacentRuleSetVersionsException">One or more versions sit between
    /// <paramref name="before"/> and <paramref name="after"/> in chronological order, or
    /// <paramref name="after"/> does not chronologically follow <paramref name="before"/> at
    /// all.</exception>
    public static (RuleSetVersion Before, RuleSetVersion After) RequireAdjacentPair(
        IReadOnlyList<RuleSetVersion> versions, RuleSetVersionId before, RuleSetVersionId after)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (!string.Equals(before.Repository, after.Repository, StringComparison.Ordinal))
        {
            throw new MixedRuleSetVersionException([before, after]);
        }

        var chronological = versions
            .Where(version => string.Equals(version.Repository, before.Repository, StringComparison.Ordinal))
            .OrderBy(version => version.FirstSessionStartedAt, StringComparer.Ordinal)
            .ThenBy(version => version.FirstSessionId, StringComparer.Ordinal)
            .ToArray();

        var beforeIndex = Array.FindIndex(
            chronological, version => string.Equals(version.Hash, before.Hash, StringComparison.Ordinal));
        if (beforeIndex < 0)
        {
            throw new UnknownRuleSetVersionException(before);
        }

        var afterIndex = Array.FindIndex(
            chronological, version => string.Equals(version.Hash, after.Hash, StringComparison.Ordinal));
        if (afterIndex < 0)
        {
            throw new UnknownRuleSetVersionException(after);
        }

        if (afterIndex != beforeIndex + 1)
        {
            var intervening = afterIndex > beforeIndex + 1
                ? chronological[(beforeIndex + 1)..afterIndex]
                : [];
            throw new NonAdjacentRuleSetVersionsException(
                chronological[beforeIndex], chronological[afterIndex], intervening);
        }

        return (chronological[beforeIndex], chronological[afterIndex]);
    }
}

/// <summary>
/// Thrown by <see cref="RuleSetVersionAdjacency.RequireAdjacentPair"/> when the two requested
/// versions are not consecutive — carrying every version that sits between them (empty when the
/// requested pair was equal or out of chronological order) so a refused comparison can name exactly
/// what it refused to skip past, the same "carry every distinct version" discipline
/// <see cref="MixedRuleSetVersionException"/> already follows for a figure spanning more than one
/// version outright.
/// </summary>
public sealed class NonAdjacentRuleSetVersionsException(
    RuleSetVersion before, RuleSetVersion after, IReadOnlyList<RuleSetVersion> intervening)
    : InvalidOperationException(
        "A comparison cannot be computed across rule-set versions that are not adjacent.")
{
    public RuleSetVersion Before { get; } = before;

    public RuleSetVersion After { get; } = after;

    public IReadOnlyList<RuleSetVersion> Intervening { get; } = intervening;
}
