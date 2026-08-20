namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-26 (issue #32), Scenario 2's second half and its own edge case: identical statements across
/// sessions collapse to one, while the sessions that carried each one are still preserved — a
/// measured 43 distinct statements recovered from a measured 14 distinct blocks in the reference
/// corpus is the shape this dedup exists to reproduce.
/// </summary>
public sealed class RuleStatementDeduplicationTests
{
    static InstructionBlock Block(string sourceFile, params string[] statements) => new()
    {
        SourceFile = sourceFile,
        Statements = statements
            .Select(text => new RuleStatement { SourceFile = sourceFile, Text = text })
            .ToArray(),
    };

    [Fact]
    public void Identical_statements_across_sessions_deduplicate_to_one_occurrence()
    {
        var sessions = new[]
        {
            new SessionInstructionBlocks
            {
                SessionId = "session-1",
                Blocks = [Block("CLAUDE.md", "Prefer rg over grep.")],
            },
            new SessionInstructionBlocks
            {
                SessionId = "session-2",
                Blocks = [Block("CLAUDE.md", "Prefer rg over grep.")],
            },
        };

        var occurrences = RuleStatementDeduplication.Deduplicate(sessions);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("Prefer rg over grep.", occurrence.Statement.Text);
        Assert.Equal(["session-1", "session-2"], occurrence.SessionIds);
    }

    [Fact]
    public void Distinct_statement_text_remains_distinct()
    {
        var sessions = new[]
        {
            new SessionInstructionBlocks
            {
                SessionId = "session-1",
                Blocks = [Block("CLAUDE.md", "Rule A.")],
            },
            new SessionInstructionBlocks
            {
                SessionId = "session-2",
                Blocks = [Block("CLAUDE.md", "Rule B.")],
            },
        };

        var occurrences = RuleStatementDeduplication.Deduplicate(sessions);

        Assert.Equal(2, occurrences.Count);
    }

    [Fact]
    public void The_same_statement_text_headed_by_different_source_files_stays_distinct()
    {
        var sessions = new[]
        {
            new SessionInstructionBlocks
            {
                SessionId = "session-1",
                Blocks = [Block("CLAUDE.md", "Read the docs first.")],
            },
            new SessionInstructionBlocks
            {
                SessionId = "session-2",
                Blocks = [Block("AGENTS.md", "Read the docs first.")],
            },
        };

        var occurrences = RuleStatementDeduplication.Deduplicate(sessions);

        Assert.Equal(2, occurrences.Count);
    }

    [Fact]
    public void A_session_repeating_its_own_statement_across_two_blocks_counts_once()
    {
        var sessions = new[]
        {
            new SessionInstructionBlocks
            {
                SessionId = "session-1",
                Blocks =
                [
                    Block("CLAUDE.md", "Repeated rule."),
                    Block("CLAUDE.md", "Repeated rule."),
                ],
            },
        };

        var occurrences = RuleStatementDeduplication.Deduplicate(sessions);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal(["session-1"], occurrence.SessionIds);
    }

    /// <summary>Scenario 4, in full: a session that carried no <c>custom_instruction</c> block at
    /// all must be distinguishable from a session whose block(s) matched no list item.</summary>
    [Fact]
    public void A_session_with_no_instruction_blocks_is_distinct_from_one_whose_blocks_matched_nothing()
    {
        var noBlocksAtAll = new SessionInstructionBlocks { SessionId = "session-1", Blocks = [] };
        var blockWithNoStatements = new SessionInstructionBlocks
        {
            SessionId = "session-2",
            Blocks = [new InstructionBlock { SourceFile = "CLAUDE.md", Statements = [] }],
        };

        Assert.False(noBlocksAtAll.HasInstructionBlocks);
        Assert.True(blockWithNoStatements.HasInstructionBlocks);
    }
}
