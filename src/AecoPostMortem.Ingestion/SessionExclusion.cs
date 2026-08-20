namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-7: whether a session's own <c>cwd</c> falls under an operator-configured exclusion root.
/// Pure and file-free — <see cref="ExclusionListSource"/> is what reads the operator's own
/// configured list; this type only decides against whatever list it is handed.
/// </summary>
/// <remarks>
/// A plain cwd-prefix match cannot distinguish an analysis session run from a repository from
/// ordinary feature work also run from it — both share the same cwd (FR-7's own stated risk). This
/// type does not try to solve that; it compensates with visibility rather than precision: the
/// exclusion list is operator-configurable, and every excluded session is reported with its reason
/// (FR-14), so the operator can see and correct over- or under-exclusion rather than have it happen
/// invisibly.
/// </remarks>
public static class SessionExclusion
{
    /// <summary>
    /// An unknown <paramref name="cwd"/> (see <see cref="SessionStartContext.ExtractCwd"/>) is
    /// never excluded, even against a non-empty list — this product cannot exclude a session it
    /// cannot place, and silently dropping one on that basis would be worse than measuring it.
    /// </summary>
    public static SessionExclusionOutcome Evaluate(string? cwd, IReadOnlyList<string> excludedRoots)
    {
        ArgumentNullException.ThrowIfNull(excludedRoots);

        if (cwd is null)
        {
            return SessionExclusionOutcome.NotExcluded;
        }

        foreach (var root in excludedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (IsUnder(cwd, root))
            {
                return SessionExclusionOutcome.ExcludedBy(cwd, root);
            }
        }

        return SessionExclusionOutcome.NotExcluded;
    }

    /// <summary>Boundary-checked so a sibling directory sharing a name prefix (<c>/repo</c> vs
    /// <c>/repository</c>) never matches, and separator-normalised so a cwd recorded on one
    /// platform still compares correctly against a root configured on another.</summary>
    static bool IsUnder(string cwd, string root)
    {
        var normalizedCwd = Normalize(cwd);
        var normalizedRoot = Normalize(root);

        if (normalizedRoot.Length == 0)
        {
            return false;
        }

        return normalizedCwd.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCwd.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}

/// <summary>Whether a session was excluded, and if so, the sentence FR-14's coverage report states
/// for it.</summary>
public sealed record SessionExclusionOutcome(bool Excluded, string? Reason)
{
    public static SessionExclusionOutcome NotExcluded { get; } = new(false, null);

    public static SessionExclusionOutcome ExcludedBy(string cwd, string root) =>
        new(true, $"session cwd '{cwd}' falls under the excluded repository root '{root}'.");
}
