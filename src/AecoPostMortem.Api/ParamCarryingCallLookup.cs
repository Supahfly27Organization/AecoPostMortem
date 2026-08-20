using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Ingestion;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// Piece 3's fifth and final slice: the real <see cref="ParamCarryingCall"/> corpus
/// <see cref="AlwaysPassParamCheck"/> resolves its mentions against. <see cref="ParamCarryingCall.SpawnsAgent"/>
/// is read the same structural way <see cref="ToolInvocationShapeLookup"/> already reads it
/// (<see cref="Agent.SpawningToolCallId"/>) — no new RAW parsing for that half.
/// <see cref="ParamCarryingCall.ArgumentKeys"/> reads every field name a call's own RAW
/// <c>tool.execution_start.data.arguments</c> carried, not one fixed set: unlike
/// <see cref="ToolInvocationShape"/>'s four closed argument-shape booleans, the parameter a rule names
/// is arbitrary, so this lookup cannot know in advance which key a caller will ask about.
/// </summary>
public static class ParamCarryingCallLookup
{
    /// <summary>
    /// One <see cref="ParamCarryingCall"/> per <paramref name="toolCalls"/> row. A call whose own
    /// <c>tool.execution_start</c> event is missing, or whose <c>arguments</c> is not object-shaped —
    /// the same <c>apply_patch</c> wrinkle <see cref="ToolInvocationShapeLookup"/> already guards
    /// against — carries no argument keys at all, rather than guessing from the raw text.
    /// </summary>
    public static IReadOnlyList<ParamCarryingCall> BuildAll(
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents,
        IReadOnlyList<RawEvent> rawEvents)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(rawEvents);

        var spawningCallIds = agents
            .Select(agent => (agent.SessionId, agent.SpawningToolCallId))
            .ToHashSet();

        var argumentsByCall = RawToolArguments.ByCall(rawEvents);

        return toolCalls
            .Select(call =>
            {
                var arguments = argumentsByCall.GetValueOrDefault((call.SessionId, call.ToolCallId));
                var argumentKeys = arguments is { Kind: ToolArgumentKind.Object }
                    ? arguments.PropertyNames.ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);

                return new ParamCarryingCall
                {
                    SessionId = call.SessionId,
                    ToolCallId = call.ToolCallId,
                    SpawnsAgent = spawningCallIds.Contains((call.SessionId, call.ToolCallId)),
                    ArgumentKeys = argumentKeys,
                };
            })
            .ToList();
    }
}
