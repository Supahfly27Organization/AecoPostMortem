namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-28 (issue #33, S-20): an adherence figure spanning two rule-set versions must be refused, not
/// averaged into a number that describes a version that never existed — the PRD's own documented
/// failure mode collapses a measured 41.8%-71.7% range into one figure this way. This project has no
/// adherence figure of its own yet (that is a later story), so this is the reusable primitive any
/// future figure scopes itself with before it computes anything.
/// </summary>
public sealed class RuleSetVersionScopeTests
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
    public void Sessions_sharing_one_version_are_accepted()
    {
        var ruleSet = Block("CLAUDE.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", ruleSet),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z", ruleSet),
        ];

        var id = RuleSetVersionScope.RequireSingleVersion(sessions);

        Assert.Equal("repo-a", id.Repository);
    }

    [Fact]
    public void A_figure_spanning_two_different_rule_set_versions_is_refused()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A.")),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z", Block("CLAUDE.md", "Rule A, edited.")),
        ];

        Assert.Throws<MixedRuleSetVersionException>(
            () => RuleSetVersionScope.RequireSingleVersion(sessions));
    }

    [Fact]
    public void A_figure_spanning_two_repositories_is_refused_even_with_an_identical_block_set()
    {
        var ruleSet = Block("CLAUDE.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", ruleSet),
            Session("s2", "repo-b", "2026-01-01T00:00:00Z", ruleSet),
        ];

        Assert.Throws<MixedRuleSetVersionException>(
            () => RuleSetVersionScope.RequireSingleVersion(sessions));
    }

    [Fact]
    public void No_sessions_to_scope_a_figure_to_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => RuleSetVersionScope.RequireSingleVersion([]));
    }
}
