using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Builds one <c>Data.Execution.Skill</c> row per <c>skill.invoked</c> event — the second piece of
/// the derived-layer writer (S-12/FR-25's own tape rendering already reads <see cref="Skill"/>'s
/// <c>PluginName</c>/<c>PluginVersion</c>, but nothing has ever populated the table itself).
/// </summary>
public sealed class SkillBuilderTests
{
    const string SessionId = "session-1";

    static RawEvent Event(
        long sequence, string type, string timestamp, string id, string? agentId, object data)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["id"] = id,
            ["data"] = data,
        };

        if (agentId is not null)
        {
            envelope["agentId"] = agentId;
        }

        var payload = JsonSerializer.Serialize(envelope);
        return new RawEvent(
            SessionId, sequence, type, timestamp, "1.0.40",
            @"~/.copilot/session-state/session-1/events.jsonl", sequence, $"hash-{sequence}", payload);
    }

    [Fact]
    public void No_skill_invocations_produce_no_rows()
    {
        var events = new[] { Event(0, "session.start", "2026-05-07T00:00:00Z", "e0", null, new { }) };

        Assert.Empty(SkillBuilder.Build(SessionId, events));
    }

    [Fact]
    public void A_main_thread_invocation_is_read_in_full()
    {
        var events = new[]
        {
            Event(0, "skill.invoked", "2026-05-07T14:17:00.000Z", "e0", null, new
            {
                name = "using-superpowers",
                path = @"C:\Users\david\.copilot\installed-plugins\superpowers-marketplace\superpowers\skills\using-superpowers\SKILL.md",
                description = "Use when starting any conversation",
                pluginName = "superpowers",
                pluginVersion = "5.0.7",
            }),
        };

        var skill = Assert.Single(SkillBuilder.Build(SessionId, events));

        Assert.Equal(SessionId, skill.SessionId);
        Assert.Equal("e0", skill.EventId);
        Assert.Equal("using-superpowers", skill.Name);
        Assert.Equal(
            @"C:\Users\david\.copilot\installed-plugins\superpowers-marketplace\superpowers\skills\using-superpowers\SKILL.md",
            skill.Path);
        Assert.Equal("Use when starting any conversation", skill.Description);
        Assert.Equal("superpowers", skill.PluginName);
        Assert.Equal("5.0.7", skill.PluginVersion);
        Assert.Equal("2026-05-07T14:17:00.000Z", skill.InvokedAt);
        Assert.Equal(OwnerKind.Main, skill.OwnerKind);
        Assert.Null(skill.AgentId);
    }

    [Fact]
    public void An_invocation_carrying_an_agent_id_is_owned_by_that_agent()
    {
        var events = new[]
        {
            Event(0, "skill.invoked", "2026-05-07T14:17:00.000Z", "e0", "agent-1", new { name = "systematic-debugging" }),
        };

        var skill = Assert.Single(SkillBuilder.Build(SessionId, events));

        Assert.Equal(OwnerKind.Agent, skill.OwnerKind);
        Assert.Equal("agent-1", skill.AgentId);
    }

    [Fact]
    public void Optional_fields_absent_from_the_event_are_null_not_a_thrown_exception()
    {
        var events = new[] { Event(0, "skill.invoked", "2026-05-07T14:17:00.000Z", "e0", null, new { name = "bare-skill" }) };

        var skill = Assert.Single(SkillBuilder.Build(SessionId, events));

        Assert.Equal("bare-skill", skill.Name);
        Assert.Null(skill.Path);
        Assert.Null(skill.Description);
        Assert.Null(skill.PluginName);
        Assert.Null(skill.PluginVersion);
    }

    [Fact]
    public void Every_invocation_produces_its_own_row_in_event_order()
    {
        var events = new[]
        {
            Event(0, "skill.invoked", "2026-05-07T14:17:00.000Z", "e0", null, new { name = "first" }),
            Event(1, "skill.invoked", "2026-05-07T14:18:00.000Z", "e1", null, new { name = "second" }),
        };

        var skills = SkillBuilder.Build(SessionId, events);

        Assert.Equal(["first", "second"], skills.Select(s => s.Name));
    }
}
