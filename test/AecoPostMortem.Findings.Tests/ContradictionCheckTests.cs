using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-43 (S-38, issue #47): orchestrates <see cref="Rules.ContradictionCheck"/> — this project's
/// own job is scoping comparison to one rule-set version at a time (never across an edit, FR-43's
/// second scenario) and registering the run so a clean pass states its denominator on the
/// silent-checks surface, the same "checks that found nothing" contract FR-42 (S-37) already
/// publishes for <c>"contradiction-check"</c> ahead of this story landing.
/// </summary>
public sealed class ContradictionCheckTests
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
    public void A_conflicting_pair_within_one_version_is_reported()
    {
        var blocks = Block(
            "CLAUDE.md",
            "Use `codebase-memory-mcp` before broad file search.",
            "Do not use `codebase-memory-mcp` before broad file search.");
        SessionRuleSet[] sessions = [Session("s1", "repo-a", "2026-01-01T00:00:00Z", blocks)];

        var result = ContradictionCheck.Run(sessions);

        Assert.Single(result.Candidates);
    }

    /// <summary>Scenario 2 (issue #47): several rule-set versions in the input, but a statement from
    /// one version is never compared against a statement from another — each version's block set
    /// hashes differently (a rule edited between two sessions), so the conflicting pair here only
    /// exists when the two halves are read as one version, never split across two.</summary>
    [Fact]
    public void Statements_from_two_different_rule_set_versions_are_never_compared()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z",
                Block("CLAUDE.md", "Use `codebase-memory-mcp` before broad file search.")),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z",
                Block("CLAUDE.md", "Do not use `codebase-memory-mcp` before broad file search.")),
        ];

        var result = ContradictionCheck.Run(sessions);

        Assert.Empty(result.Candidates);
    }

    /// <summary>The same conflicting pair, but both statements now sit inside the identical block
    /// set that every session in the version shares — the version-scoping groups them back together
    /// and the pair is reported, proving the previous test's emptiness is about version-scoping and
    /// not about the check being unable to find this pair at all.</summary>
    [Fact]
    public void The_same_pair_is_reported_once_both_statements_share_one_version()
    {
        var blocks = Block(
            "CLAUDE.md",
            "Use `codebase-memory-mcp` before broad file search.",
            "Do not use `codebase-memory-mcp` before broad file search.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", blocks),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z", blocks),
        ];

        var result = ContradictionCheck.Run(sessions);

        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Every_candidate_carries_inferred_provenance()
    {
        var blocks = Block(
            "CLAUDE.md",
            "Use `codebase-memory-mcp` before broad file search.",
            "Do not use `codebase-memory-mcp` before broad file search.");
        SessionRuleSet[] sessions = [Session("s1", "repo-a", "2026-01-01T00:00:00Z", blocks)];

        var result = ContradictionCheck.Run(sessions);

        Assert.Equal(Provenance.Inferred, result.Provenance);
    }

    [Fact]
    public void The_check_registers_with_its_population_and_finding_count()
    {
        var blocks = Block(
            "CLAUDE.md",
            "Use `codebase-memory-mcp` before broad file search.",
            "Do not use `codebase-memory-mcp` before broad file search.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", blocks),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z", blocks),
        ];

        var result = ContradictionCheck.Run(sessions);

        Assert.Equal(ContradictionCheck.CheckId, result.RegistryEntry.CheckId);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(2, result.RegistryEntry.Population);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
    }

    /// <summary>A clean run (no contradiction found across the whole corpus) still registers as
    /// <see cref="CheckRunStatus.Ran"/> with a real population — never a refusal — so this check
    /// appears on the "checks that found nothing" surface exactly as
    /// <c>SilentCheckEnvelopeTests.A_check_that_ran_and_found_nothing_states_its_denominator</c>
    /// (issue #46) already expects for <c>"contradiction-check"</c>.</summary>
    [Fact]
    public void No_sessions_still_registers_a_clean_run_not_a_refusal()
    {
        var result = ContradictionCheck.Run([]);

        Assert.Empty(result.Candidates);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(0, result.RegistryEntry.Population);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    /// <summary>The exact edge case FR-43 names: a real prohibition, alone in its version, whose own
    /// text contains the phrase it prohibits. Scoping through version grouping must not introduce a
    /// self-match the underlying <see cref="Rules.ContradictionCheck"/> already excludes by
    /// construction.</summary>
    [Fact]
    public void A_lone_prohibition_in_its_own_version_is_never_flagged_against_itself()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z",
                Block("CLAUDE.md", "Do not use it without checking the cache first.")),
        ];

        var result = ContradictionCheck.Run(sessions);

        Assert.Empty(result.Candidates);
    }
}
