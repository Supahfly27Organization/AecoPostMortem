namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-27 (issue #33, S-20): a rule-set version is identified by the block set its sessions carried,
/// scoped per repository, and rendered with the first and last session that carried it plus the
/// session count — the edge case is a measured 6 versions over 32 days across 25 sessions in one
/// repository, so a version's sample is often small enough that the count must render alongside it.
/// </summary>
public sealed class RuleSetVersioningTests
{
    static InstructionBlock Block(string sourceFile, params string[] statements) => new()
    {
        SourceFile = sourceFile,
        Statements = statements
            .Select(text => new RuleStatement { SourceFile = sourceFile, Text = text })
            .ToArray(),
    };

    static SessionRuleSet Session(
        string sessionId, string? repository, string startedAt, params InstructionBlock[] blocks) =>
        new()
        {
            SessionId = sessionId,
            Repository = repository,
            StartedAt = startedAt,
            Blocks = blocks,
        };

    [Fact]
    public void Sessions_sharing_an_identical_block_set_share_a_version()
    {
        var ruleSet = Block("CLAUDE.md", "Prefer rg over grep.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", ruleSet),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z", ruleSet),
        ];

        var versions = RuleSetVersioning.Compute(sessions);

        var version = Assert.Single(versions);
        Assert.Equal(2, version.SessionCount);
    }

    [Fact]
    public void Sessions_with_different_block_sets_in_the_same_repository_produce_different_versions()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A.")),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z", Block("CLAUDE.md", "Rule B.")),
        ];

        var versions = RuleSetVersioning.Compute(sessions);

        Assert.Equal(2, versions.Count);
    }

    [Fact]
    public void The_same_block_set_in_different_repositories_produces_separate_versions()
    {
        var ruleSet = Block("CLAUDE.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", ruleSet),
            Session("s2", "repo-b", "2026-01-01T00:00:00Z", ruleSet),
        ];

        var versions = RuleSetVersioning.Compute(sessions);

        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions.Single(v => v.Repository == "repo-a").SessionCount);
        Assert.Equal(1, versions.Single(v => v.Repository == "repo-b").SessionCount);
    }

    [Fact]
    public void A_versions_window_is_its_first_and_last_session_in_time_order()
    {
        var ruleSet = Block("CLAUDE.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", ruleSet),
            Session("s2", "repo-a", "2026-01-05T00:00:00Z", ruleSet),
            Session("s3", "repo-a", "2026-01-10T00:00:00Z", ruleSet),
        ];

        var version = Assert.Single(RuleSetVersioning.Compute(sessions));

        Assert.Equal("s1", version.FirstSessionId);
        Assert.Equal("s3", version.LastSessionId);
    }

    [Fact]
    public void The_window_is_derived_from_time_order_not_input_order()
    {
        var ruleSet = Block("CLAUDE.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("later", "repo-a", "2026-01-10T00:00:00Z", ruleSet),
            Session("earlier", "repo-a", "2026-01-01T00:00:00Z", ruleSet),
        ];

        var version = Assert.Single(RuleSetVersioning.Compute(sessions));

        Assert.Equal("earlier", version.FirstSessionId);
        Assert.Equal("later", version.LastSessionId);
    }

    [Fact]
    public void An_empty_corpus_produces_no_versions()
    {
        Assert.Empty(RuleSetVersioning.Compute([]));
    }

    [Fact]
    public void A_single_session_is_its_own_version_with_a_sample_size_of_one()
    {
        SessionRuleSet[] sessions =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A."))];

        var version = Assert.Single(RuleSetVersioning.Compute(sessions));

        Assert.Equal("s1", version.FirstSessionId);
        Assert.Equal("s1", version.LastSessionId);
        Assert.Equal(1, version.SessionCount);
    }

    [Fact]
    public void Sessions_carrying_no_repository_still_group_into_a_version()
    {
        var ruleSet = Block("CLAUDE.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("s1", null, "2026-01-01T00:00:00Z", ruleSet),
            Session("s2", null, "2026-01-02T00:00:00Z", ruleSet),
        ];

        var version = Assert.Single(RuleSetVersioning.Compute(sessions));

        Assert.Null(version.Repository);
        Assert.Equal(2, version.SessionCount);
    }
}
