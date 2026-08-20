namespace AecoPostMortem.Rules;

/// <summary>
/// FR-28 (S-20, issue #33): every adherence figure must be scoped to one rule-set version and one
/// repository, and a figure that spans a rule edit must be impossible to compute, not merely
/// discouraged. This project has no adherence figure of its own yet — that check-shape is later
/// work — so this is the reusable primitive any future figure calls before it computes anything: it
/// hands back the one <see cref="RuleSetVersionId"/> every session shares, or refuses.
/// </summary>
public static class RuleSetVersionScope
{
    /// <summary>
    /// Returns the single <see cref="RuleSetVersionId"/> every session in <paramref name="sessions"/>
    /// shares. Throws <see cref="MixedRuleSetVersionException"/> when the sessions span more than one
    /// repository, more than one block-set hash, or both — refusing rather than averaging across an
    /// edit (FR-28).
    /// </summary>
    public static RuleSetVersionId RequireSingleVersion(IEnumerable<SessionRuleSet> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var ids = sessions
            .Select(session => new RuleSetVersionId
            {
                Repository = session.Repository,
                Hash = RuleSetVersionHasher.ComputeHash(session.Blocks),
            })
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            throw new ArgumentException(
                "A figure needs at least one session to be scoped to.", nameof(sessions));
        }

        if (ids.Length > 1)
        {
            throw new MixedRuleSetVersionException(ids);
        }

        return ids[0];
    }
}

/// <summary>
/// Thrown by <see cref="RuleSetVersionScope.RequireSingleVersion"/> when a figure's sessions span
/// more than one rule-set version — the refusal FR-28 requires, carrying every distinct version the
/// sessions actually spanned so a caller can report why the figure was refused.
/// </summary>
public sealed class MixedRuleSetVersionException(IReadOnlyList<RuleSetVersionId> versions)
    : InvalidOperationException(
        "A figure cannot be computed across more than one rule-set version or repository.")
{
    public IReadOnlyList<RuleSetVersionId> Versions { get; } = versions;
}
