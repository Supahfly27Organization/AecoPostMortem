namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-39 (S-35, issue #43): the Monitor comparison's adjacency refusal — two rule-set versions are
/// comparable only when nothing sits between them in that repository's own chronological order.
/// Comparing versions with an edit sitting between them would silently absorb whatever that edit
/// changed into a two-point comparison that never saw it, the same reasoning
/// <see cref="RuleSetVersionScope.RequireSingleVersion"/> gives for refusing a figure that spans a
/// rule edit rather than averaging across it.
/// </summary>
public sealed class RuleSetVersionAdjacencyTests
{
    static RuleSetVersion Version(
        string repository, string hash, string firstSessionId, int sessionCount, string? startedAt = null) =>
        new()
        {
            Id = new RuleSetVersionId { Repository = repository, Hash = hash },
            FirstSessionId = firstSessionId,
            FirstSessionStartedAt = startedAt ?? firstSessionId,
            LastSessionId = firstSessionId,
            SessionCount = sessionCount,
        };

    [Fact]
    public void Two_adjacent_versions_are_returned_as_before_and_after()
    {
        RuleSetVersion[] versions =
        [
            Version("repo-a", "hash-1", "s1", 3),
            Version("repo-a", "hash-2", "s4", 4),
        ];

        var (before, after) = RuleSetVersionAdjacency.RequireAdjacentPair(
            versions,
            new RuleSetVersionId { Repository = "repo-a", Hash = "hash-1" },
            new RuleSetVersionId { Repository = "repo-a", Hash = "hash-2" });

        Assert.Equal("hash-1", before.Hash);
        Assert.Equal(3, before.SessionCount);
        Assert.Equal("hash-2", after.Hash);
        Assert.Equal(4, after.SessionCount);
    }

    [Fact]
    public void Two_versions_with_one_between_them_are_refused_naming_the_intervening_version()
    {
        RuleSetVersion[] versions =
        [
            Version("repo-a", "hash-1", "s1", 3),
            Version("repo-a", "hash-2", "s2", 5),
            Version("repo-a", "hash-3", "s3", 4),
        ];

        var exception = Assert.Throws<NonAdjacentRuleSetVersionsException>(() =>
            RuleSetVersionAdjacency.RequireAdjacentPair(
                versions,
                new RuleSetVersionId { Repository = "repo-a", Hash = "hash-1" },
                new RuleSetVersionId { Repository = "repo-a", Hash = "hash-3" }));

        var intervening = Assert.Single(exception.Intervening);
        Assert.Equal("hash-2", intervening.Hash);
    }

    [Fact]
    public void Two_versions_in_different_repositories_are_refused()
    {
        RuleSetVersion[] versions =
        [
            Version("repo-a", "hash-1", "s1", 3),
            Version("repo-b", "hash-2", "s2", 4),
        ];

        Assert.Throws<MixedRuleSetVersionException>(() =>
            RuleSetVersionAdjacency.RequireAdjacentPair(
                versions,
                new RuleSetVersionId { Repository = "repo-a", Hash = "hash-1" },
                new RuleSetVersionId { Repository = "repo-b", Hash = "hash-2" }));
    }

    [Fact]
    public void A_requested_version_the_repository_never_carried_is_refused()
    {
        RuleSetVersion[] versions = [Version("repo-a", "hash-1", "s1", 3)];

        Assert.Throws<UnknownRuleSetVersionException>(() =>
            RuleSetVersionAdjacency.RequireAdjacentPair(
                versions,
                new RuleSetVersionId { Repository = "repo-a", Hash = "hash-1" },
                new RuleSetVersionId { Repository = "repo-a", Hash = "hash-unknown" }));
    }

    [Fact]
    public void Adjacency_is_determined_by_real_start_time_not_by_session_id_text()
    {
        // hash-2's own FirstSessionId ("aaa") sorts before hash-1's ("zzz") under ordinal string
        // comparison, but hash-1 genuinely started first in time — RequireAdjacentPair must order by
        // FirstSessionStartedAt, not by the opaque session id text, or this pair (real chronological
        // neighbours) would be refused as non-adjacent.
        RuleSetVersion[] versions =
        [
            Version("repo-a", "hash-1", firstSessionId: "zzz", sessionCount: 3, startedAt: "2026-01-01T00:00:00Z"),
            Version("repo-a", "hash-2", firstSessionId: "aaa", sessionCount: 4, startedAt: "2026-01-05T00:00:00Z"),
        ];

        var (before, after) = RuleSetVersionAdjacency.RequireAdjacentPair(
            versions,
            new RuleSetVersionId { Repository = "repo-a", Hash = "hash-1" },
            new RuleSetVersionId { Repository = "repo-a", Hash = "hash-2" });

        Assert.Equal("hash-1", before.Hash);
        Assert.Equal("hash-2", after.Hash);
    }

    [Fact]
    public void No_averaged_figure_is_offered_when_versions_are_refused()
    {
        RuleSetVersion[] versions =
        [
            Version("repo-a", "hash-1", "s1", 3),
            Version("repo-a", "hash-2", "s2", 5),
            Version("repo-a", "hash-3", "s3", 4),
        ];

        // RequireAdjacentPair has exactly one success path: a tuple of two RuleSetVersion values.
        // There is no overload, parameter or catch clause anywhere in this project that could turn a
        // refusal into a third, averaged result -- the refusal throws, and that is the only outcome
        // for non-adjacent input.
        Assert.Throws<NonAdjacentRuleSetVersionsException>(() =>
            RuleSetVersionAdjacency.RequireAdjacentPair(
                versions,
                new RuleSetVersionId { Repository = "repo-a", Hash = "hash-1" },
                new RuleSetVersionId { Repository = "repo-a", Hash = "hash-3" }));
    }
}
