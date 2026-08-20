using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// Piece 3's fifth and final slice: the real <see cref="Rules.ParamCarryingCall"/> corpus
/// <see cref="Rules.AlwaysPassParamCheck"/> resolves its mentions against. <see cref="Rules.
/// ParamCarryingCall.SpawnsAgent"/> is read the same structural way <see cref="ToolInvocationShapeLookup"/>
/// already reads it (<see cref="Agent.SpawningToolCallId"/>); <see cref="Rules.ParamCarryingCall.
/// ArgumentKeys"/> reads a call's own RAW <c>tool.execution_start.data.arguments</c> field names —
/// every key, not one fixed set the way <see cref="ToolInvocationShapeLookup"/> reads its four closed
/// booleans, since the parameter a rule names is arbitrary.
/// </summary>
public sealed class ParamCarryingCallLookupTests
{
    static ToolCall ACall(string toolCallId, string toolName) => new()
    {
        SessionId = "s1",
        ToolCallId = toolCallId,
        ToolName = toolName,
        StartedAt = "2026-05-07T14:18:00.000Z",
        OwnerKind = OwnerKind.Main,
    };

    static RawEvent StartEvent(string toolCallId, string toolName, string argumentsJson, long sequence = 1) =>
        new("s1", sequence, "tool.execution_start", "2026-05-07T14:18:00.000Z", "1.0.0",
            "events.jsonl", sequence * 100, $"hash-{sequence}",
            "{\"id\":\"e" + sequence + "\",\"data\":{\"toolCallId\":\"" + toolCallId
                + "\",\"toolName\":\"" + toolName + "\",\"arguments\":" + argumentsJson + "}}");

    [Fact]
    public void No_tool_calls_yield_no_param_carrying_calls()
    {
        Assert.Empty(ParamCarryingCallLookup.BuildAll([], [], []));
    }

    [Fact]
    public void A_call_matching_an_agents_spawning_tool_call_id_spawns_an_agent()
    {
        var toolCalls = new[] { ACall("tc1", "task") };
        var agents = new[]
        {
            new Agent
            {
                SessionId = "s1",
                AgentId = "tc1",
                SpawningToolCallId = "tc1",
                Name = "general-purpose",
                DisplayName = "General purpose",
                StartedAt = "2026-05-07T14:18:00.000Z",
                Outcome = AgentOutcome.Completed,
            },
        };

        var call = Assert.Single(ParamCarryingCallLookup.BuildAll(toolCalls, agents, []));

        Assert.True(call.SpawnsAgent);
    }

    [Fact]
    public void A_call_matching_no_agents_spawning_tool_call_id_does_not_spawn_an_agent()
    {
        var toolCalls = new[] { ACall("tc1", "view") };

        var call = Assert.Single(ParamCarryingCallLookup.BuildAll(toolCalls, [], []));

        Assert.False(call.SpawnsAgent);
    }

    [Fact]
    public void An_object_shaped_arguments_value_exposes_every_field_name_it_carries()
    {
        var toolCalls = new[] { ACall("tc1", "task") };
        var rawEvents = new[] { StartEvent("tc1", "task", """{"prompt":"do X","model":"claude-sonnet-5"}""") };

        var call = Assert.Single(ParamCarryingCallLookup.BuildAll(toolCalls, [], rawEvents));

        Assert.Equal(new HashSet<string> { "prompt", "model" }, call.ArgumentKeys);
        Assert.True(call.ArgumentsRecorded);
    }

    /// <summary>Code review caught this: "no matching RAW event" must not be indistinguishable from
    /// "recorded, and the key genuinely wasn't there" — <see cref="ParamCarryingCall.ArgumentsRecorded"/>
    /// is <see langword="false"/> here, not merely an empty <see cref="ParamCarryingCall.ArgumentKeys"/>.</summary>
    [Fact]
    public void A_call_with_no_matching_raw_start_event_has_unrecorded_arguments()
    {
        var toolCalls = new[] { ACall("tc1", "task") };

        var call = Assert.Single(ParamCarryingCallLookup.BuildAll(toolCalls, [], []));

        Assert.Empty(call.ArgumentKeys);
        Assert.False(call.ArgumentsRecorded);
    }

    /// <summary>The same real wrinkle <see cref="ToolInvocationShapeLookup"/> already guards against:
    /// <c>apply_patch</c>'s own <c>arguments</c> is a JSON string, not an object — there are no field
    /// names to read off it, and no key presence question can honestly be answered either.</summary>
    [Fact]
    public void A_string_shaped_arguments_value_has_unrecorded_arguments()
    {
        var toolCalls = new[] { ACall("tc1", "apply_patch") };
        var rawEvents = new[]
        {
            StartEvent("tc1", "apply_patch", """"*** Begin Patch\n*** Add File: a.cs\n+content\n*** End Patch\n""""),
        };

        var call = Assert.Single(ParamCarryingCallLookup.BuildAll(toolCalls, [], rawEvents));

        Assert.Empty(call.ArgumentKeys);
        Assert.False(call.ArgumentsRecorded);
    }
}
