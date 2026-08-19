using AecoPostMortem.Data;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-26: resolves a session's <c>&lt;custom_instruction&gt;</c> blocks from its own RAW
/// <c>system.message</c> events. This is the one caller <see cref="RuleStatementExtractor"/>
/// deliberately cannot be — that type takes plain prompt text with no idea where it came from; this
/// type is what supplies that text from the ingested store, and only from there. It never opens a
/// file: its only input is <see cref="RawEvent"/>, already landed in RAW by <see cref="SessionIngestor"/>
/// — Scenario 3's "reads only the ingested store, never any markdown file on disk" (issue #32).
/// </summary>
public static class SessionRuleExtractor
{
    /// <summary>
    /// Every instruction block across a session's own <c>system.message</c> events, unioned — a
    /// session can carry more than one distinct prompt text (a measured 1–3 per session, data map
    /// Part 6). A session with none of these events yields
    /// <see cref="SessionInstructionBlocks.HasInstructionBlocks"/> <see langword="false"/>, recorded
    /// distinctly from a session whose block(s) matched no list item (Scenario 4).
    /// </summary>
    public static SessionInstructionBlocks Extract(string sessionId, IEnumerable<RawEvent> sessionEvents)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(sessionEvents);

        var blocks = new List<InstructionBlock>();

        foreach (var raw in sessionEvents)
        {
            if (SystemPromptExtractor.Extract(raw) is not { } prompt)
            {
                continue;
            }

            blocks.AddRange(RuleStatementExtractor.ExtractBlocks(prompt.Text));
        }

        return new SessionInstructionBlocks { SessionId = sessionId, Blocks = blocks };
    }
}
