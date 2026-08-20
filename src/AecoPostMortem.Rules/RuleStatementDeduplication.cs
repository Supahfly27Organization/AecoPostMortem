namespace AecoPostMortem.Rules;

/// <summary>
/// Every <see cref="InstructionBlock"/> one session's own system prompt(s) carried. Plain input —
/// the caller (which reads through the store) resolves a session to its blocks and hands the
/// result in; this project has no idea where the blocks came from.
/// </summary>
public sealed record SessionInstructionBlocks
{
    public required string SessionId { get; init; }

    public required IReadOnlyList<InstructionBlock> Blocks { get; init; }

    /// <summary>
    /// Distinguishes "this session carried no <c>custom_instruction</c> block at all" from "it
    /// carried a block, but that block's list items yielded no statement" — Scenario 4's own point
    /// (issue #32): the two are recorded differently, never collapsed into one empty-looking state.
    /// </summary>
    public bool HasInstructionBlocks => Blocks.Count > 0;
}

/// <summary>
/// One distinct statement and every session that carried it. FR-26's dedup step: the same list
/// item, headed by the same source file, recovered from several sessions collapses to one entry
/// here rather than one row per session — the measured 43 distinct statements from a measured 14
/// distinct blocks in the reference corpus is exactly this collapse.
/// </summary>
public sealed record RuleStatementOccurrence
{
    public required RuleStatement Statement { get; init; }

    public required IReadOnlyList<string> SessionIds { get; init; }
}

/// <summary>
/// Collapses identical statements recovered across many sessions to one occurrence each, while
/// keeping which sessions carried it (issue #32's edge case: "extraction must deduplicate across
/// sessions while preserving which sessions carried what"). A statement's identity is its
/// <see cref="RuleStatement.SourceFile"/> and <see cref="RuleStatement.Text"/> together — the same
/// wording headed by two different files is not the same statement.
/// </summary>
public static class RuleStatementDeduplication
{
    public static IReadOnlyList<RuleStatementOccurrence> Deduplicate(
        IEnumerable<SessionInstructionBlocks> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var bySignature = new Dictionary<(string SourceFile, string Text), Entry>();

        foreach (var session in sessions)
        {
            foreach (var statement in session.Blocks.SelectMany(block => block.Statements))
            {
                var key = (statement.SourceFile, statement.Text);
                if (!bySignature.TryGetValue(key, out var entry))
                {
                    entry = new Entry(statement, []);
                    bySignature[key] = entry;
                }

                if (!entry.SessionIds.Contains(session.SessionId, StringComparer.Ordinal))
                {
                    entry.SessionIds.Add(session.SessionId);
                }
            }
        }

        return bySignature.Values
            .Select(entry => new RuleStatementOccurrence
            {
                Statement = entry.Statement,
                SessionIds = entry.SessionIds.Order(StringComparer.Ordinal).ToArray(),
            })
            .OrderBy(occurrence => occurrence.Statement.SourceFile, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.Statement.Text, StringComparer.Ordinal)
            .ToArray();
    }

    sealed record Entry(RuleStatement Statement, List<string> SessionIds);
}
