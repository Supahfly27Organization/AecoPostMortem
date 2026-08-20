namespace AecoPostMortem.Rules;

/// <summary>
/// One markdown list item recovered from a <c>&lt;custom_instruction&gt;</c> block (FR-26): the
/// exact text Copilot injected, with only its list marker stripped and the result trimmed — never
/// paraphrased, reflowed or otherwise altered. <see cref="SourceFile"/> is the block's own heading
/// (its first line, with a markdown heading marker stripped) — the file this statement was headed
/// by. Measured values include real filenames (<c>CLAUDE.md</c>, <c>AGENTS.md</c>) and non-file
/// headings Copilot also injects (<c>Agent workflow</c>, <c>Copilot instructions</c>); this type
/// carries whichever text the block was headed by verbatim, without deciding which kind it is.
/// </summary>
public sealed record RuleStatement
{
    public required string SourceFile { get; init; }

    public required string Text { get; init; }
}

/// <summary>
/// One <c>&lt;custom_instruction&gt;</c> block found in a system prompt: the source file it was
/// headed by, and every statement its list items yielded. <see cref="Statements"/> can be empty — a
/// block that carries only prose, no list item at all — which is a different fact from no block
/// existing in the first place (see <see cref="SessionInstructionBlocks.HasInstructionBlocks"/>).
/// </summary>
public sealed record InstructionBlock
{
    public required string SourceFile { get; init; }

    public required IReadOnlyList<RuleStatement> Statements { get; init; }
}
