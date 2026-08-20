namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-43 (S-38, issue #47): pairwise keyword-polarity contradiction detection over one rule-set
/// version's statements. The edge case that makes self-match exclusion the load-bearing
/// requirement, not an optimisation: a keyword-polarity first pass returned a measured 4
/// candidates and all 4 were spurious — three matched a statement against itself, because a
/// prohibition contains the phrase it prohibits ("do not use it" contains "use it").
/// </summary>
public sealed class ContradictionCheckTests
{
    static RuleStatement Statement(string text, string sourceFile = "CLAUDE.md") =>
        new() { SourceFile = sourceFile, Text = text };

    [Fact]
    public void A_directive_and_its_negation_over_the_same_wording_are_flagged()
    {
        RuleStatement[] statements =
        [
            Statement("Use `codebase-memory-mcp` before broad file search."),
            Statement("Do not use `codebase-memory-mcp` before broad file search."),
        ];

        var candidates = ContradictionCheck.Run(statements);

        Assert.Single(candidates);
    }

    /// <summary>The exact worked example from FR-43's own edge case: a real prohibition whose text
    /// literally contains the phrase it prohibits as a substring of itself. A single-statement list
    /// gives the check no second statement to compare it against — it must not flag the statement
    /// against itself.</summary>
    [Fact]
    public void A_real_prohibition_alone_in_the_set_is_never_flagged_against_itself()
    {
        RuleStatement[] statements = [Statement("Do not use it without checking the cache first.")];

        var candidates = ContradictionCheck.Run(statements);

        Assert.Empty(candidates);
    }

    /// <summary>Two occurrences of the identical prohibition (e.g. recovered from two different
    /// sessions and not yet deduplicated by the caller) share the same polarity — they agree, they
    /// do not conflict — and must not be flagged, even though each one's own text still contains the
    /// phrase it prohibits.</summary>
    [Fact]
    public void Two_identical_prohibitions_are_not_flagged_against_each_other()
    {
        RuleStatement[] statements =
        [
            Statement("Never commit directly to `main`."),
            Statement("Never commit directly to `main`."),
        ];

        var candidates = ContradictionCheck.Run(statements);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Unrelated_statements_of_differing_polarity_are_not_flagged()
    {
        RuleStatement[] statements =
        [
            Statement("Use `rg` instead of `grep`."),
            Statement("Never commit directly to `main`."),
        ];

        var candidates = ContradictionCheck.Run(statements);

        Assert.Empty(candidates);
    }

    /// <summary>Each pair is compared exactly once: three unrelated statements give three possible
    /// pairs, none of which conflict, so the result is empty rather than double-counted.</summary>
    [Fact]
    public void Each_pair_is_compared_exactly_once()
    {
        RuleStatement[] statements =
        [
            Statement("Use `rg` instead of `grep`."),
            Statement("Prefer `codebase-memory-mcp` for navigation."),
            Statement("Keep CLAUDE.md accurate."),
        ];

        var candidates = ContradictionCheck.Run(statements);

        Assert.Empty(candidates);
    }

    /// <summary>A real conflicting pair sitting among unrelated statements is reported exactly once,
    /// not once per ordering of the pair — proving the loop never revisits (i, j) as (j, i) too.</summary>
    [Fact]
    public void A_conflicting_pair_among_unrelated_statements_is_reported_exactly_once()
    {
        RuleStatement[] statements =
        [
            Statement("Keep CLAUDE.md accurate."),
            Statement("Use `codebase-memory-mcp` before broad file search."),
            Statement("Do not use `codebase-memory-mcp` before broad file search."),
        ];

        var candidates = ContradictionCheck.Run(statements);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Use `codebase-memory-mcp` before broad file search.", candidate.First.Text);
        Assert.Equal("Do not use `codebase-memory-mcp` before broad file search.", candidate.Second.Text);
    }

    [Fact]
    public void An_empty_statement_list_produces_no_candidates()
    {
        Assert.Empty(ContradictionCheck.Run([]));
    }
}
