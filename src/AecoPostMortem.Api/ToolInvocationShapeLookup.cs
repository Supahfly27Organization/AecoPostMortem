using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Ingestion;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// The gap <c>RulesInventoryClassifier</c>'s own remarks name (<c>Api/CLAUDE.md</c>): nothing in this
/// codebase has ever built a real <see cref="ToolInvocationShape"/> corpus from RAW
/// <c>tool.execution_start.data.arguments</c>. <see cref="HasPath"/>/<see cref="ToolInvocationShape.McpServerName"/>
/// and <see cref="ToolInvocationShape.SpawnsAgent"/> need no new RAW parsing at all — <c>ToolCall.Path</c>,
/// <c>.McpServerName</c> and <c>Agent.SpawningToolCallId</c> are already real, populated NORMALIZED
/// columns (<c>Data/CLAUDE.md</c>) — only <see cref="ToolInvocationShape.HasPattern"/>/
/// <see cref="ToolInvocationShape.HasReplacement"/>/<see cref="ToolInvocationShape.HasFileText"/>/
/// <see cref="ToolInvocationShape.HasCommand"/> are read from each call's own RAW arguments, using
/// field names verified against the live 35-session reference corpus rather than guessed: <c>pattern</c>
/// (<c>rg</c>/<c>grep</c>/<c>glob</c>), <c>old_str</c>/<c>new_str</c> (<c>edit</c>), <c>file_text</c>
/// (<c>create</c>), <c>command</c> (<c>powershell</c>).
/// </summary>
public static class ToolInvocationShapeLookup
{
    /// <summary>
    /// One <see cref="ToolInvocationShape"/> per <paramref name="toolCalls"/> row. A call whose own
    /// <c>tool.execution_start</c> event is missing, or whose <c>arguments</c> is not object-shaped —
    /// the real wrinkle the corpus check caught: <c>apply_patch</c>'s own <c>arguments</c> is a JSON
    /// string (the whole patch body), not an object — reports all four object-only flags as
    /// <see langword="false"/> rather than guess at them from the raw text.
    /// </summary>
    public static IReadOnlyList<ToolInvocationShape> BuildAll(
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents,
        IReadOnlyList<RawEvent> rawEvents)
    {
        ArgumentNullException.ThrowIfNull(rawEvents);

        return BuildAll(toolCalls, agents, RawToolArguments.ByCall(rawEvents));
    }

    /// <summary>
    /// Overload for a caller that already built the shared RAW-arguments dictionary — today,
    /// <see cref="ApiHost.GetDigest"/>, which needs the identical dictionary for this lookup and for
    /// <see cref="ParamCarryingCallLookup"/> and would otherwise parse every <c>tool.execution_start</c>
    /// payload in scope twice.
    /// </summary>
    internal static IReadOnlyList<ToolInvocationShape> BuildAll(
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents,
        IReadOnlyDictionary<(string SessionId, string ToolCallId), ToolArguments> argumentsByCall)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(argumentsByCall);

        var spawningCallIds = agents
            .Select(agent => (agent.SessionId, agent.SpawningToolCallId))
            .ToHashSet();

        return toolCalls
            .Select(call =>
            {
                var arguments = argumentsByCall.GetValueOrDefault((call.SessionId, call.ToolCallId));

                return new ToolInvocationShape
                {
                    ToolName = call.ToolName,
                    HasPath = call.Path is not null,
                    HasPattern = HasField(arguments, "pattern"),
                    HasReplacement = HasField(arguments, "old_str") || HasField(arguments, "new_str"),
                    HasFileText = HasField(arguments, "file_text"),
                    HasCommand = HasField(arguments, "command"),
                    SpawnsAgent = spawningCallIds.Contains((call.SessionId, call.ToolCallId)),
                    McpServerName = call.McpServerName,
                };
            })
            .ToList();
    }

    static bool HasField(ToolArguments? arguments, string name) =>
        arguments is { Kind: ToolArgumentKind.Object } && arguments.TryGetProperty(name, out _);
}
