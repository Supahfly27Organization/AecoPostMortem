using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-22 (S-09, issue #18): resolves one subagent's own report straight from a session's own
/// <see cref="RawEvent"/>s — the same envelope reading <see cref="StepEvidenceLookup"/> already does
/// for a step's Thinking/Raw evidence. The data map measured <c>read_agent</c> completions at a
/// median 48 characters against subagent reports whose median is far longer, ending in the literal
/// marker <c>"(Full response provided to agent)"</c> — this lookup never reads a
/// <c>tool.execution_complete</c> result at all, so that stub can never surface as a lane's output by
/// construction, not by a filter that has to remember to exclude it.
/// </summary>
public static class SubagentOutputLookup
{
    public static SubagentOutputEnvelope Find(IReadOnlyList<RawEvent> sessionEvents, Agent agent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvents);
        ArgumentNullException.ThrowIfNull(agent);

        // Scenario 4: failure is a rendered state, not a hypothesis — a measured 6 `subagent.failed`
        // events exist. The more urgent, more specific claim wins over any output lookup, the same
        // ordering `SessionRecording.DetermineStatus` gives its own two checks.
        if (agent.Outcome == AgentOutcome.Failed)
        {
            return new SubagentOutputEnvelope.Failed
            {
                Error = agent.Error is { Length: > 0 } error
                    ? error
                    : "The subagent failed; no error was recorded.",
            };
        }

        string? lastText = null;

        foreach (var raw in sessionEvents.OrderBy(e => e.Sequence).Where(e => e.EventType == "assistant.message"))
        {
            if (EventEnvelopeReader.TryRead(raw, out var envelope)
                && envelope.AgentId == agent.AgentId
                && GetString(envelope.Data, "content") is { Length: > 0 } text)
            {
                lastText = text;
            }
        }

        return lastText is { } present
            ? new SubagentOutputEnvelope.Present { Text = present }
            : new SubagentOutputEnvelope.NotRecorded
            {
                Reason = "No output was recorded for this subagent.",
            };
    }

    static string? GetString(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
