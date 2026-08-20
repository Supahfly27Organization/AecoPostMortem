using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-27's own not-yet-wired gap (`AecoPostMortem.Ingestion/CLAUDE.md`'s own remarks under S-20):
/// nothing yet resolves a whole store's <see cref="RawEvent"/>s into
/// <see cref="Rules.SessionRuleSet"/>s at scale. <see cref="SessionRuleSetLookup"/> is that corpus-wide
/// wiring, one <see cref="Rules.SessionRuleSet"/> per <see cref="Session"/> row.
/// </summary>
public sealed class SessionRuleSetLookupTests
{
    const string ProviderVersion = "0.0.339";
    const string Stamp = "2026-08-09T20:14:36.758Z";

    static Session ASession(string sessionId, string? repository, string startedAt) => new()
    {
        SessionId = sessionId,
        StartedAt = startedAt,
        EndedAt = null,
        CopilotVersion = ProviderVersion,
        EventSchemaVersion = "1",
        SourceFile = $@"~/.copilot/session-state/{sessionId}/events.jsonl",
        Cwd = @"C:\repo",
        Repository = repository,
    };

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

    [Fact]
    public void No_sessions_yield_no_rule_sets()
    {
        Assert.Empty(SessionRuleSetLookup.BuildAll([], []));
    }

    [Fact]
    public void A_session_with_no_system_message_events_yields_an_empty_block_list()
    {
        var sessions = new[] { ASession("s1", "org/repo", "2026-08-16T10:00:00Z") };

        var ruleSets = SessionRuleSetLookup.BuildAll(sessions, []);

        var set = Assert.Single(ruleSets);
        Assert.Equal("s1", set.SessionId);
        Assert.Equal("org/repo", set.Repository);
        Assert.Equal("2026-08-16T10:00:00Z", set.StartedAt);
        Assert.Empty(set.Blocks);
    }

    [Fact]
    public void A_sessions_own_custom_instruction_block_is_resolved_into_its_rule_set()
    {
        const string prompt = """
            <custom_instruction>
            CLAUDE.md
            - Prefer rg over grep.
            </custom_instruction>
            """;

        var sessions = new[] { ASession("s1", "org/repo", "2026-08-16T10:00:00Z") };
        var rawEvents = new[] { SystemMessage("s1", prompt) };

        var ruleSets = SessionRuleSetLookup.BuildAll(sessions, rawEvents);

        var set = Assert.Single(ruleSets);
        var block = Assert.Single(set.Blocks);
        Assert.Equal("CLAUDE.md", block.SourceFile);
        Assert.Equal("Prefer rg over grep.", Assert.Single(block.Statements).Text);
    }

    [Fact]
    public void Another_sessions_events_never_contribute_to_this_sessions_rule_set()
    {
        const string prompt = """
            <custom_instruction>
            CLAUDE.md
            - Prefer rg over grep.
            </custom_instruction>
            """;

        var sessions = new[]
        {
            ASession("s1", "org/repo", "2026-08-16T10:00:00Z"),
            ASession("s2", "org/repo", "2026-08-16T11:00:00Z"),
        };
        var rawEvents = new[] { SystemMessage("s1", prompt) };

        var ruleSets = SessionRuleSetLookup.BuildAll(sessions, rawEvents);

        Assert.Equal(2, ruleSets.Count);
        Assert.Single(ruleSets.Single(set => set.SessionId == "s1").Blocks);
        Assert.Empty(ruleSets.Single(set => set.SessionId == "s2").Blocks);
    }
}
