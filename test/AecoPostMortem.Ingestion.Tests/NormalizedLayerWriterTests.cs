using System.Text.Json;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Ties <see cref="SessionBuilder"/>, <see cref="AecoPostMortem.Ingestion.ExecutionRecordBuilder"/>,
/// <see cref="SkillBuilder"/>, <see cref="HookBuilder"/> and <see cref="PermissionBuilder"/>
/// together into the writer <c>ingest</c> and <c>rebuild</c> both call: derive one session's
/// NORMALIZED rows from its own RAW events, replacing whatever was there before. Before the first
/// four of these landed, nothing in the repository ever wrote their tables, so
/// <c>AecoPostMortem.Api.ApiHost.GetSession</c> always 404'd against a real store.
/// </summary>
public sealed class NormalizedLayerWriterTests
{
    static RawEvent Event(
        string sessionId, long sequence, string type, string timestamp, string id, object data)
    {
        var payload = JsonSerializer.Serialize(new { type, id, data });
        return new RawEvent(
            sessionId, sequence, type, timestamp, "1.0.40",
            $"~/.copilot/session-state/{sessionId}/events.jsonl", sequence, $"hash-{sessionId}-{sequence}", payload);
    }

    static RawEvent[] AFullSession(string sessionId) =>
    [
        Event(sessionId, 0, "session.start", "2026-05-07T14:16:48.682Z", "e0",
            new { version = 1, copilotVersion = "1.0.40", context = new { cwd = @"F:\git\UpFront" } }),
        Event(sessionId, 1, "assistant.turn_start", "2026-05-07T14:16:49.000Z", "e1", new { turnId = "turn-1" }),
        Event(sessionId, 2, "tool.execution_start", "2026-05-07T14:16:50.000Z", "e2",
            new { toolCallId = "tc-1", toolName = "view", arguments = new { path = "/a" } }),
        Event(sessionId, 3, "assistant.turn_end", "2026-05-07T14:16:51.000Z", "e3", new { turnId = "turn-1" }),
        Event(sessionId, 4, "skill.invoked", "2026-05-07T14:16:52.000Z", "e4", new { name = "a-skill" }),
        Event(sessionId, 5, "hook.start", "2026-05-07T14:16:53.000Z", "e5", new { hookInvocationId = "inv-1", hookType = "sessionStart" }),
        Event(sessionId, 6, "hook.end", "2026-05-07T14:16:54.000Z", "e6", new { hookInvocationId = "inv-1", hookType = "sessionStart", success = true }),
        Event(sessionId, 7, "permission.requested", "2026-05-07T14:16:55.000Z", "e7",
            new { requestId = "req-1", permissionRequest = new { toolCallId = "tc-1" } }),
        Event(sessionId, 8, "permission.completed", "2026-05-07T14:16:56.000Z", "e8",
            new { requestId = "req-1", toolCallId = "tc-1", result = new { kind = "approved" } }),
    ];

    static void Seed(PostMortemContext context, IEnumerable<RawEvent> events)
    {
        context.RawEvents.AddRange(events);
        context.SaveChanges();
    }

    [Fact]
    public void Deriving_a_session_writes_its_session_turns_and_tool_calls()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, AFullSession("session-1"));

        NormalizedLayerWriter.Derive(context, "session-1");

        var session = Assert.Single(context.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.Equal(@"F:\git\UpFront", session.Cwd);

        var turn = Assert.Single(context.Turns);
        Assert.Equal("turn-1", turn.TurnId);

        var toolCall = Assert.Single(context.ToolCalls);
        Assert.Equal("tc-1", toolCall.ToolCallId);
    }

    [Fact]
    public void Deriving_a_session_writes_its_skills_and_hooks()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, AFullSession("session-1"));

        NormalizedLayerWriter.Derive(context, "session-1");

        var skill = Assert.Single(context.Skills);
        Assert.Equal("a-skill", skill.Name);

        var hook = Assert.Single(context.Hooks);
        Assert.Equal("inv-1", hook.EventId);
        Assert.True(hook.Success);
    }

    [Fact]
    public void Deriving_a_session_writes_its_permissions()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, AFullSession("session-1"));

        NormalizedLayerWriter.Derive(context, "session-1");

        var permission = Assert.Single(context.Permissions);
        Assert.Equal("req-1", permission.EventId);
        Assert.Equal("approved", permission.ResultKind);
    }

    [Fact]
    public void Deriving_the_same_session_twice_replaces_rather_than_duplicates()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, AFullSession("session-1"));

        NormalizedLayerWriter.Derive(context, "session-1");
        NormalizedLayerWriter.Derive(context, "session-1");

        Assert.Single(context.Sessions);
        Assert.Single(context.Turns);
        Assert.Single(context.ToolCalls);
        Assert.Single(context.Skills);
        Assert.Single(context.Hooks);
        Assert.Single(context.Permissions);
    }

    [Fact]
    public void Deriving_one_session_leaves_another_sessions_rows_untouched()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, AFullSession("session-1"));
        Seed(context, AFullSession("session-2"));

        NormalizedLayerWriter.Derive(context, "session-1");
        NormalizedLayerWriter.Derive(context, "session-2");

        Assert.Equal(2, context.Sessions.Count());
        Assert.Equal(2, context.Turns.Count());
        Assert.Equal(2, context.ToolCalls.Count());
    }

    /// <summary>Real-corpus finding, not a hypothetical: scanning the live reference corpus showed
    /// <c>assistant.turn_start.data.turnId</c> repeats within a session on 27 of 35 real sessions —
    /// it is a small, cycling counter, not a stable per-turn identity. Two genuinely different turns
    /// sharing the same displayed <c>turnId</c> must still both persist, keyed by their own
    /// <c>turn_start</c> event's envelope id — the same "no natural id, so the event's own id is the
    /// key" pattern <c>Skill</c>/<c>Hook</c> already use.</summary>
    [Fact]
    public void Two_turns_sharing_the_same_displayed_turn_id_both_persist()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context,
        [
            Event("session-1", 0, "session.start", "2026-05-07T14:16:48.682Z", "e0",
                new { version = 1, copilotVersion = "1.0.40", context = new { cwd = @"F:\git\UpFront" } }),
            Event("session-1", 1, "assistant.turn_start", "2026-05-07T14:16:49.000Z", "e1", new { turnId = "0" }),
            Event("session-1", 2, "assistant.turn_end", "2026-05-07T14:16:50.000Z", "e2", new { turnId = "0" }),
            Event("session-1", 3, "assistant.turn_start", "2026-05-07T14:16:51.000Z", "e3", new { turnId = "0" }),
            Event("session-1", 4, "assistant.turn_end", "2026-05-07T14:16:52.000Z", "e4", new { turnId = "0" }),
        ]);

        NormalizedLayerWriter.Derive(context, "session-1");

        Assert.Equal(2, context.Turns.Count());
    }

    [Fact]
    public void A_session_whose_first_event_is_not_session_start_produces_no_rows_at_all()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, [Event("session-1", 0, "assistant.turn_start", "2026-05-07T14:16:49.000Z", "e0", new { turnId = "turn-1" })]);

        NormalizedLayerWriter.Derive(context, "session-1");

        Assert.Empty(context.Sessions);
        Assert.Empty(context.Turns);
    }

    [Fact]
    public void Deleting_a_session_removes_every_one_of_its_derived_rows()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, AFullSession("session-1"));
        Seed(context, AFullSession("session-2"));
        NormalizedLayerWriter.Derive(context, "session-1");
        NormalizedLayerWriter.Derive(context, "session-2");

        NormalizedLayerWriter.DeleteForSession(context, "session-1");

        Assert.Empty(context.Sessions.Where(s => s.SessionId == "session-1"));
        Assert.Empty(context.Turns.Where(t => t.SessionId == "session-1"));
        Assert.Empty(context.ToolCalls.Where(t => t.SessionId == "session-1"));
        Assert.Empty(context.Skills.Where(s => s.SessionId == "session-1"));
        Assert.Empty(context.Hooks.Where(h => h.SessionId == "session-1"));
        Assert.Empty(context.Permissions.Where(p => p.SessionId == "session-1"));

        Assert.Single(context.Sessions.Where(s => s.SessionId == "session-2"));
    }

    /// <summary>The sequence <c>AecoPostMortem.Cli</c>'s <c>rebuild</c> command and
    /// <c>AecoPostMortem.Api</c>'s <c>POST /api/rebuild</c> both need — drop-and-recreate the derived
    /// schema, then re-derive every session RAW still holds — factored out here so both callers share
    /// one definition of "rebuild" rather than each repeating the loop over
    /// <see cref="Derive(PostMortemContext, string)"/> independently.</summary>
    [Fact]
    public void RebuildAll_recreates_the_schema_and_repopulates_every_session_raw_holds()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();
        Seed(context, AFullSession("session-1"));
        Seed(context, AFullSession("session-2"));

        var sessionIds = NormalizedLayerWriter.RebuildAll(context);

        Assert.Equal(["session-1", "session-2"], sessionIds.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(2, context.Sessions.Count());
        Assert.Equal(2, context.Turns.Count());
        Assert.Equal(2, context.ToolCalls.Count());
        Assert.Equal(2, context.RawEvents.Select(e => e.SessionId).Distinct().Count());
    }

    [Fact]
    public void RebuildAll_against_a_store_with_no_raw_events_repopulates_nothing_and_does_not_throw()
    {
        using var workspace = new IngestionTestWorkspace();
        using var context = workspace.Store.Open();

        var sessionIds = NormalizedLayerWriter.RebuildAll(context);

        Assert.Empty(sessionIds);
        Assert.Empty(context.Sessions);
    }
}
