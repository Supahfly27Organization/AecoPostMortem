namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-26 (issue #32): recovers rule statements from the <c>&lt;custom_instruction&gt;</c> blocks
/// Copilot injects into a system prompt. Pure text in, structured text out.
/// </summary>
public sealed class RuleStatementExtractorTests
{
    [Fact]
    public void A_list_item_becomes_one_statement_with_its_marker_stripped_and_text_trimmed()
    {
        const string prompt = """
            <custom_instruction>
            CLAUDE.md
            -   Prefer querying codebase-memory-mcp for navigation.
            </custom_instruction>
            """;

        var blocks = RuleStatementExtractor.ExtractBlocks(prompt);

        var block = Assert.Single(blocks);
        Assert.Equal("CLAUDE.md", block.SourceFile);
        var statement = Assert.Single(block.Statements);
        Assert.Equal("CLAUDE.md", statement.SourceFile);
        Assert.Equal("Prefer querying codebase-memory-mcp for navigation.", statement.Text);
    }

    [Fact]
    public void Prose_lines_are_not_extracted_as_statements()
    {
        const string prompt = """
            <custom_instruction>
            CLAUDE.md
            This paragraph explains something but names no rule.
            - This is the only real rule.
            Another prose line that trails the list.
            </custom_instruction>
            """;

        var blocks = RuleStatementExtractor.ExtractBlocks(prompt);

        var block = Assert.Single(blocks);
        var statement = Assert.Single(block.Statements);
        Assert.Equal("This is the only real rule.", statement.Text);
    }

    [Theory]
    [InlineData("- dash marker")]
    [InlineData("* asterisk marker")]
    [InlineData("+ plus marker")]
    [InlineData("1. numbered marker")]
    [InlineData("2) parenthesised numbered marker")]
    public void Every_markdown_list_marker_style_is_recognised(string line)
    {
        var prompt = $"""
            <custom_instruction>
            CLAUDE.md
            {line}
            </custom_instruction>
            """;

        var blocks = RuleStatementExtractor.ExtractBlocks(prompt);

        var statement = Assert.Single(Assert.Single(blocks).Statements);
        Assert.False(
            statement.Text.StartsWith('-') || statement.Text.StartsWith('*')
            || statement.Text.StartsWith('+') || char.IsDigit(statement.Text[0]),
            $"marker was not stripped from '{statement.Text}'");
    }

    [Fact]
    public void The_source_file_is_the_blocks_first_line_with_a_heading_marker_stripped()
    {
        const string prompt = """
            <custom_instruction>
            # CLAUDE.md

            ## Working Rules
            - Do not narrate routine tool calls.
            </custom_instruction>
            """;

        var blocks = RuleStatementExtractor.ExtractBlocks(prompt);

        Assert.Equal("CLAUDE.md", Assert.Single(blocks).SourceFile);
    }

    [Fact]
    public void Text_with_no_custom_instruction_block_yields_no_blocks()
    {
        var blocks = RuleStatementExtractor.ExtractBlocks("You are a coding agent. Be concise.");

        Assert.Empty(blocks);
    }

    [Fact]
    public void Multiple_blocks_in_one_prompt_are_each_extracted_with_their_own_source_file()
    {
        const string prompt = """
            <custom_instruction>
            CLAUDE.md
            - Rule from CLAUDE.md.
            </custom_instruction>
            Some prompt text in between the two injected files.
            <custom_instruction>
            AGENTS.md
            - Rule from AGENTS.md.
            </custom_instruction>
            """;

        var blocks = RuleStatementExtractor.ExtractBlocks(prompt);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("CLAUDE.md", blocks[0].SourceFile);
        Assert.Equal("Rule from CLAUDE.md.", blocks[0].Statements[0].Text);
        Assert.Equal("AGENTS.md", blocks[1].SourceFile);
        Assert.Equal("Rule from AGENTS.md.", blocks[1].Statements[0].Text);
    }

    /// <summary>Scenario 4's block-level half: a block that carries no list item still appears,
    /// with an empty statement list rather than being dropped — the caller needs to be able to tell
    /// "this block matched nothing" apart from "no block existed at all".</summary>
    [Fact]
    public void A_block_with_only_prose_still_appears_with_no_statements()
    {
        const string prompt = """
            <custom_instruction>
            Copilot instructions
            Nothing in this block is a list item.
            </custom_instruction>
            """;

        var blocks = RuleStatementExtractor.ExtractBlocks(prompt);

        var block = Assert.Single(blocks);
        Assert.Equal("Copilot instructions", block.SourceFile);
        Assert.Empty(block.Statements);
    }
}
