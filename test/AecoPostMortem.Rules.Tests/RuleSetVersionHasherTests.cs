namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-27's content hash: a rule-set version is identified by its block set, and PRD Part 8 Q4 left
/// open whether block ordering is stable across sessions — so the hash must not depend on it.
/// </summary>
public sealed class RuleSetVersionHasherTests
{
    static InstructionBlock Block(string sourceFile, params string[] statements) => new()
    {
        SourceFile = sourceFile,
        Statements = statements
            .Select(text => new RuleStatement { SourceFile = sourceFile, Text = text })
            .ToArray(),
    };

    [Fact]
    public void The_same_blocks_in_a_different_order_hash_identically()
    {
        InstructionBlock[] firstOrder =
        [
            Block("CLAUDE.md", "Prefer rg over grep."),
            Block("AGENTS.md", "Never force-push."),
        ];
        InstructionBlock[] secondOrder =
        [
            Block("AGENTS.md", "Never force-push."),
            Block("CLAUDE.md", "Prefer rg over grep."),
        ];

        Assert.Equal(
            RuleSetVersionHasher.ComputeHash(firstOrder),
            RuleSetVersionHasher.ComputeHash(secondOrder));
    }

    [Fact]
    public void A_different_block_set_hashes_differently()
    {
        InstructionBlock[] original = [Block("CLAUDE.md", "Prefer rg over grep.")];
        InstructionBlock[] edited = [Block("CLAUDE.md", "Prefer rg over grep, always.")];

        Assert.NotEqual(
            RuleSetVersionHasher.ComputeHash(original),
            RuleSetVersionHasher.ComputeHash(edited));
    }

    [Fact]
    public void An_empty_block_set_hashes_deterministically()
    {
        Assert.Equal(
            RuleSetVersionHasher.ComputeHash([]),
            RuleSetVersionHasher.ComputeHash([]));
    }

    [Fact]
    public void The_same_statement_text_headed_by_a_different_source_file_hashes_differently()
    {
        InstructionBlock[] first = [Block("CLAUDE.md", "Read the docs first.")];
        InstructionBlock[] second = [Block("AGENTS.md", "Read the docs first.")];

        Assert.NotEqual(
            RuleSetVersionHasher.ComputeHash(first),
            RuleSetVersionHasher.ComputeHash(second));
    }

    /// <summary>
    /// A regression for a real collision seam code review found in an earlier version of this
    /// hasher: joining a block's source file and statements with a separator character only avoids a
    /// collision if that character is guaranteed absent from the field content, which extracted rule
    /// text has no such guarantee about. A naive concatenation would make source file "AB" with
    /// statement "C" equal source file "A" with statement "BC" (both concatenate to "ABC"); the
    /// length-prefixed encoding must keep them apart regardless of what the fields contain.
    /// </summary>
    [Fact]
    public void Different_splits_between_source_file_and_statement_text_hash_differently()
    {
        InstructionBlock[] first = [Block("AB", "C")];
        InstructionBlock[] second = [Block("A", "BC")];

        Assert.NotEqual(
            RuleSetVersionHasher.ComputeHash(first),
            RuleSetVersionHasher.ComputeHash(second));
    }

    [Fact]
    public void A_block_with_no_statements_hashes_differently_from_no_block_at_all()
    {
        InstructionBlock[] blockWithNoStatements =
            [new InstructionBlock { SourceFile = "CLAUDE.md", Statements = [] }];

        Assert.NotEqual(
            RuleSetVersionHasher.ComputeHash(blockWithNoStatements),
            RuleSetVersionHasher.ComputeHash([]));
    }
}
