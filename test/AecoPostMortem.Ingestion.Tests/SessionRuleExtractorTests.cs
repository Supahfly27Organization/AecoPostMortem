using AecoPostMortem.Data;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-26 (issue #32): resolves a session's <c>&lt;custom_instruction&gt;</c> blocks from its own
/// RAW <c>system.message</c> events, and only from those — never from a markdown file on disk.
/// </summary>
public sealed class SessionRuleExtractorTests
{
    const string ProviderVersion = "0.0.339";
    const string Stamp = "2026-08-09T20:14:36.758Z";

    /// <summary>Scenario 3, behaviourally: extraction is exercised end to end from RawEvents built
    /// entirely in memory. No session-state directory, no file, is ever created by this test — so a
    /// rule statement can only have come from the RawEvent payload, never from disk.</summary>
    [Fact]
    public void Extraction_recovers_statements_from_in_memory_raw_events_with_no_file_on_disk()
    {
        const string prompt = """
            <custom_instruction>
            CLAUDE.md
            - Prefer rg over grep.
            </custom_instruction>
            """;

        var events = new[] { SystemMessage("session-1", prompt) };

        var result = SessionRuleExtractor.Extract("session-1", events);

        Assert.True(result.HasInstructionBlocks);
        var block = Assert.Single(result.Blocks);
        Assert.Equal("CLAUDE.md", block.SourceFile);
        Assert.Equal("Prefer rg over grep.", Assert.Single(block.Statements).Text);
    }

    [Fact]
    public void A_session_with_no_system_message_events_has_no_instruction_blocks()
    {
        var events = new[]
        {
            new RawEvent(
                "session-1", 0, "session.start", Stamp, ProviderVersion,
                "events.jsonl", 0, RawPayload.ContentHashOfText("{}"), """{"type":"session.start"}"""),
        };

        var result = SessionRuleExtractor.Extract("session-1", events);

        Assert.False(result.HasInstructionBlocks);
        Assert.Empty(result.Blocks);
    }

    [Fact]
    public void A_sessions_blocks_are_unioned_across_its_own_system_message_events()
    {
        const string firstPrompt = """
            <custom_instruction>
            CLAUDE.md
            - Rule one.
            </custom_instruction>
            """;
        const string secondPrompt = """
            <custom_instruction>
            AGENTS.md
            - Rule two.
            </custom_instruction>
            """;

        var events = new[]
        {
            SystemMessage("session-1", firstPrompt, sequence: 0),
            SystemMessage("session-1", secondPrompt, sequence: 5),
        };

        var result = SessionRuleExtractor.Extract("session-1", events);

        Assert.Equal(2, result.Blocks.Count);
        Assert.Contains(result.Blocks, block => block.SourceFile == "CLAUDE.md");
        Assert.Contains(result.Blocks, block => block.SourceFile == "AGENTS.md");
    }

    static RawEvent SystemMessage(string sessionId, string content, long sequence = 0) => new(
        sessionId,
        sequence,
        "system.message",
        Stamp,
        ProviderVersion,
        $"events-{sessionId}.jsonl",
        sequence,
        RawPayload.ContentHashOfText(content),
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "system.message",
            ["data"] = new Dictionary<string, string> { ["content"] = content },
        }));
}
