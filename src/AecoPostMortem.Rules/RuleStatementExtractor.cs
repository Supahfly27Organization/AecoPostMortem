using System.Text.RegularExpressions;

namespace AecoPostMortem.Rules;

/// <summary>
/// FR-26: recovers rule statements from the <c>&lt;custom_instruction&gt;</c> blocks Copilot
/// injects into a session's system prompt. Pure text in, structured data out — no file, no
/// session, no storage of any kind; the caller supplies the prompt text already resolved from the
/// ingested store (this project may reach neither the source repository nor the store itself, see
/// this project's own CLAUDE.md and its non-negotiable invariant).
/// </summary>
public static partial class RuleStatementExtractor
{
    /// <summary>
    /// Every <c>&lt;custom_instruction&gt;</c> block in <paramref name="systemPromptText"/>, each
    /// reduced to the source file it was headed by (its first non-blank line, with a markdown
    /// heading marker stripped) and the statements its list items yielded (Scenario 2: "the
    /// extraction unit is one list item, normalised"). A block that carries no list item still
    /// appears, with an empty <see cref="InstructionBlock.Statements"/> — extraction never drops a
    /// block silently, only ever reports what it found in it.
    /// </summary>
    public static IReadOnlyList<InstructionBlock> ExtractBlocks(string systemPromptText)
    {
        ArgumentNullException.ThrowIfNull(systemPromptText);

        var blocks = new List<InstructionBlock>();

        foreach (Match blockMatch in BlockPattern().Matches(systemPromptText))
        {
            var lines = blockMatch.Groups["body"].Value.Split(['\r', '\n']);

            var headerIndex = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
            if (headerIndex < 0)
            {
                continue; // an empty block names no source file and carries no statements
            }

            var sourceFile = lines[headerIndex].TrimStart('#', ' ', '\t').Trim();
            if (sourceFile.Length == 0)
            {
                // A heading that strips to nothing (e.g. a bare "###") names no source file, so the
                // whole block is dropped — any list items after it go with it. Not observed in the
                // measured corpus; a deliberate trade-off, not an oversight (see
                // A_block_whose_heading_strips_to_nothing_is_dropped_entirely).
                continue;
            }

            var statements = new List<RuleStatement>();
            for (var i = headerIndex + 1; i < lines.Length; i++)
            {
                var itemMatch = ListItemPattern().Match(lines[i]);
                if (!itemMatch.Success)
                {
                    continue;
                }

                var text = itemMatch.Groups["text"].Value.Trim();
                if (text.Length > 0)
                {
                    statements.Add(new RuleStatement { SourceFile = sourceFile, Text = text });
                }
            }

            blocks.Add(new InstructionBlock { SourceFile = sourceFile, Statements = statements });
        }

        return blocks;
    }

    [GeneratedRegex(
        "<custom_instruction>(?<body>.*?)</custom_instruction>",
        RegexOptions.Singleline)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"^[ \t]*(?:[-*+]|\d+[.)])[ \t]+(?<text>.+)$")]
    private static partial Regex ListItemPattern();
}
