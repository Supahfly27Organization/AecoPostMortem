using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// The RAW read <see cref="ToolInvocationShapeLookup"/> and <see cref="ParamCarryingCallLookup"/> both
/// need: one <see cref="ToolArguments"/> per <c>tool.execution_start</c> event, keyed by the call it
/// belongs to. Factored out so the two lookups share one RAW-parsing pass rather than each walking
/// <c>rawEvents</c> a second time to answer the same "what did this call's arguments look like"
/// question.
/// </summary>
static class RawToolArguments
{
    public static Dictionary<(string SessionId, string ToolCallId), ToolArguments> ByCall(
        IReadOnlyList<RawEvent> rawEvents)
    {
        var byCall = new Dictionary<(string, string), ToolArguments>();

        foreach (var raw in rawEvents)
        {
            if (raw.EventType != "tool.execution_start" || !EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            if (envelope.Data.ValueKind != JsonValueKind.Object
                || !envelope.Data.TryGetProperty("toolCallId", out var toolCallIdProperty)
                || toolCallIdProperty.ValueKind != JsonValueKind.String
                || !envelope.Data.TryGetProperty("arguments", out var argumentsElement))
            {
                continue;
            }

            byCall[(raw.SessionId, toolCallIdProperty.GetString()!)] =
                ToolArguments.Parse(argumentsElement.GetRawText());
        }

        return byCall;
    }
}
