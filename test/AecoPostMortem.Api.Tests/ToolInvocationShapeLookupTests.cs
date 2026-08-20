using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// The real corpus's own not-yet-built gap (<c>Api/CLAUDE.md</c>'s "RulesInventoryClassifier
/// deliberately never produces Watched" remark, <c>Rules/CLAUDE.md</c>'s own status note): nothing
/// has ever built a real <see cref="Rules.ToolInvocationShape"/> corpus from RAW <c>tool.execution_
/// start.data.arguments</c>. <see cref="ToolInvocationShapeLookup"/> is that corpus-wide wiring, field
/// names verified against the live 35-session reference corpus rather than guessed.
/// </summary>
public sealed class ToolInvocationShapeLookupTests
{
    static ToolCall ACall(
        string toolCallId, string toolName, string? path = null, string? mcpServerName = null) => new()
    {
        SessionId = "s1",
        ToolCallId = toolCallId,
        ToolName = toolName,
        StartedAt = "2026-05-07T14:18:00.000Z",
        Path = path,
        McpServerName = mcpServerName,
        OwnerKind = OwnerKind.Main,
    };

    static RawEvent StartEvent(string toolCallId, string toolName, string argumentsJson, long sequence = 1) =>
        new("s1", sequence, "tool.execution_start", "2026-05-07T14:18:00.000Z", "1.0.0",
            "events.jsonl", sequence * 100, $"hash-{sequence}",
            "{\"id\":\"e" + sequence + "\",\"data\":{\"toolCallId\":\"" + toolCallId
                + "\",\"toolName\":\"" + toolName + "\",\"arguments\":" + argumentsJson + "}}");

    [Fact]
    public void No_tool_calls_yield_no_invocation_shapes()
    {
        Assert.Empty(ToolInvocationShapeLookup.BuildAll([], [], []));
    }

    [Fact]
    public void Has_path_and_mcp_server_name_are_read_straight_off_the_tool_call_row()
    {
        var toolCalls = new[] { ACall("tc1", "view", path: "/a.cs", mcpServerName: "codebase-memory-mcp") };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], []));

        Assert.True(shape.HasPath);
        Assert.Equal("codebase-memory-mcp", shape.McpServerName);
    }

    [Fact]
    public void A_call_with_no_path_and_no_mcp_server_name_reports_neither()
    {
        var toolCalls = new[] { ACall("tc1", "powershell") };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], []));

        Assert.False(shape.HasPath);
        Assert.Null(shape.McpServerName);
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

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, agents, []));

        Assert.True(shape.SpawnsAgent);
    }

    [Fact]
    public void A_call_matching_no_agents_spawning_tool_call_id_does_not_spawn_an_agent()
    {
        var toolCalls = new[] { ACall("tc1", "view") };
        var agents = new[]
        {
            new Agent
            {
                SessionId = "s1",
                AgentId = "other",
                SpawningToolCallId = "tc-other",
                Name = "general-purpose",
                DisplayName = "General purpose",
                StartedAt = "2026-05-07T14:18:00.000Z",
                Outcome = AgentOutcome.Completed,
            },
        };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, agents, []));

        Assert.False(shape.SpawnsAgent);
    }

    /// <summary>Field name confirmed against real <c>rg</c>/<c>grep</c>/<c>glob</c> payloads in the
    /// live corpus: <c>arguments.pattern</c>.</summary>
    [Fact]
    public void A_search_calls_pattern_argument_sets_has_pattern()
    {
        var toolCalls = new[] { ACall("tc1", "rg") };
        var rawEvents = new[] { StartEvent("tc1", "rg", """{"pattern":"Saga","paths":"F:\\repo"}""") };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], rawEvents));

        Assert.True(shape.HasPattern);
        Assert.False(shape.HasReplacement);
        Assert.False(shape.HasFileText);
        Assert.False(shape.HasCommand);
    }

    /// <summary>Field names confirmed against a real <c>edit</c> payload: <c>arguments.old_str</c> /
    /// <c>arguments.new_str</c>.</summary>
    [Fact]
    public void An_edit_calls_old_str_and_new_str_arguments_set_has_replacement()
    {
        var toolCalls = new[] { ACall("tc1", "edit") };
        var rawEvents = new[]
        {
            StartEvent("tc1", "edit", """{"path":"F:\\repo\\a.cs","old_str":"a","new_str":"b"}"""),
        };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], rawEvents));

        Assert.True(shape.HasReplacement);
    }

    /// <summary>Field name confirmed against a real <c>create</c> payload: <c>arguments.file_text</c>.</summary>
    [Fact]
    public void A_create_calls_file_text_argument_sets_has_file_text()
    {
        var toolCalls = new[] { ACall("tc1", "create") };
        var rawEvents = new[]
        {
            StartEvent("tc1", "create", """{"path":"F:\\repo\\CLAUDE.md","file_text":"# CLAUDE.md"}"""),
        };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], rawEvents));

        Assert.True(shape.HasFileText);
    }

    /// <summary>Field name confirmed against a real <c>powershell</c> payload: <c>arguments.command</c>.</summary>
    [Fact]
    public void A_shell_calls_command_argument_sets_has_command()
    {
        var toolCalls = new[] { ACall("tc1", "powershell") };
        var rawEvents = new[]
        {
            StartEvent("tc1", "powershell", """{"command":"dotnet build","description":"Build"}"""),
        };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], rawEvents));

        Assert.True(shape.HasCommand);
    }

    /// <summary>The real wrinkle the corpus check caught: a real <c>apply_patch</c> call's own
    /// <c>arguments</c> value is a JSON string (the whole patch body), not an object — none of the
    /// four object-only fields exist on it. Reading them must fall through cleanly, never throw and
    /// never guess from the patch text.</summary>
    [Fact]
    public void A_string_shaped_arguments_value_sets_none_of_the_four_object_only_flags()
    {
        var toolCalls = new[] { ACall("tc1", "apply_patch") };
        var rawEvents = new[]
        {
            StartEvent("tc1", "apply_patch", """"*** Begin Patch\n*** Add File: a.cs\n+content\n*** End Patch\n""""),
        };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], rawEvents));

        Assert.False(shape.HasPattern);
        Assert.False(shape.HasReplacement);
        Assert.False(shape.HasFileText);
        Assert.False(shape.HasCommand);
    }

    [Fact]
    public void A_call_with_no_matching_raw_start_event_sets_none_of_the_four_object_only_flags()
    {
        var toolCalls = new[] { ACall("tc1", "rg") };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], []));

        Assert.False(shape.HasPattern);
        Assert.False(shape.HasReplacement);
        Assert.False(shape.HasFileText);
        Assert.False(shape.HasCommand);
    }

    [Fact]
    public void Tool_name_is_carried_verbatim_from_the_tool_call_row()
    {
        var toolCalls = new[] { ACall("tc1", "rg") };

        var shape = Assert.Single(ToolInvocationShapeLookup.BuildAll(toolCalls, [], []));

        Assert.Equal("rg", shape.ToolName);
    }
}
