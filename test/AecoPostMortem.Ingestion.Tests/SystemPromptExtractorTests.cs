using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-12: system-prompt text extracted from <c>system.message.data.content</c> and deduplicated by
/// content hash, so a measured 337 near-duplicate system messages (median 54,335 characters, data
/// map Part 6) do not become 337 near-duplicate rows.
/// </summary>
public sealed class SystemPromptExtractorTests
{
    const string ProviderVersion = "0.0.339";
    const string Stamp = "2026-08-09T20:14:36.758Z";

    [Fact]
    public void A_system_message_event_yields_its_extracted_text()
    {
        var raw = SystemMessage("session-1", "You are a coding agent.");

        var extracted = SystemPromptExtractor.Extract(raw);

        Assert.NotNull(extracted);
        Assert.Equal("You are a coding agent.", extracted!.Text);
        Assert.Equal(RawPayload.ContentHashOfText("You are a coding agent."), extracted.ContentHash);
    }

    [Theory]
    [InlineData("assistant.turn_start")]
    [InlineData("tool.execution_start")]
    [InlineData("session.start")]
    public void A_non_system_message_event_yields_nothing(string eventType)
    {
        var raw = new RawEvent(
            "session-1", 0, eventType, Stamp, ProviderVersion,
            "events.jsonl", 0,
            RawPayload.ContentHashOfText("{}"),
            """{"type":"whatever"}""");

        Assert.Null(SystemPromptExtractor.Extract(raw));
    }

    [Fact]
    public void A_system_message_with_no_content_field_yields_nothing()
    {
        var raw = new RawEvent(
            "session-1", 0, "system.message", Stamp, ProviderVersion,
            "events.jsonl", 0,
            RawPayload.ContentHashOfText("{}"),
            """{"type":"system.message","data":{}}""");

        Assert.Null(SystemPromptExtractor.Extract(raw));
    }

    /// <summary>Acceptance criterion 1, in full: many sessions carrying the same prompt text
    /// collapse to one stored row, and each session still resolves its own full text.</summary>
    [Fact]
    public void Many_sessions_with_the_same_prompt_dedupe_to_one_stored_row_and_each_still_resolves_its_own_text()
    {
        using var temporary = new TemporaryStore();
        const string prompt = "You are a coding agent. Follow AGENTS.md and CLAUDE.md.";

        var sessions = new[] { "session-1", "session-2", "session-3" };
        var events = sessions.Select(id => SystemMessage(id, prompt)).ToArray();

        using var context = temporary.Store.Open();

        var distinct = SystemPromptExtractor.DistinctTexts(events);
        SystemPromptTextBatch.Append(context, distinct);

        Assert.Equal(1, context.Set<SystemPromptText>().Count());

        foreach (var raw in events)
        {
            var extracted = SystemPromptExtractor.Extract(raw)!;
            var stored = context.Set<SystemPromptText>().Single(row => row.ContentHash == extracted.ContentHash);

            Assert.Equal(prompt, stored.Text);
        }
    }

    /// <summary>Distinct prompt text across sessions stays distinct — this is deduplication, not
    /// collapse-everything.</summary>
    [Fact]
    public void Sessions_with_different_prompts_each_keep_their_own_stored_text()
    {
        using var temporary = new TemporaryStore();
        var events = new[]
        {
            SystemMessage("session-1", "prompt A"),
            SystemMessage("session-2", "prompt B"),
        };

        using var context = temporary.Store.Open();
        SystemPromptTextBatch.Append(context, SystemPromptExtractor.DistinctTexts(events));

        Assert.Equal(2, context.Set<SystemPromptText>().Count());
    }

    static RawEvent SystemMessage(string sessionId, string content) => new(
        sessionId,
        0,
        "system.message",
        Stamp,
        ProviderVersion,
        $"events-{sessionId}.jsonl",
        0,
        RawPayload.ContentHashOfText(content),
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "system.message",
            ["data"] = new Dictionary<string, string> { ["content"] = content },
        }));
}
