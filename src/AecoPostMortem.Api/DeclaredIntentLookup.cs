using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Ingestion;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-19's own not-yet-wired gap, closed here: <c>Data.Execution.ToolCall</c> carries no field for
/// <c>report_intent</c>'s own <c>arguments.intent</c> (only <c>Path</c> is extracted today —
/// <c>AecoPostMortem.Ingestion/CLAUDE.md</c>), so a session's declared phases can only be read by a
/// fresh pass over its own RAW <c>tool.execution_start</c> events — the same narrow-RAW-read pattern
/// <see cref="HookFailureEventLookup"/> uses for its own not-yet-wired evidence. This is the one
/// place in the codebase allowed to name <c>report_intent</c> (Repo Rule 6 binds
/// <c>AecoPostMortem.Rules</c> only).
/// </summary>
public static class DeclaredIntentLookup
{
    const string DeclaringToolName = "report_intent";

    /// <summary>
    /// Every <c>report_intent</c> call's own <c>arguments.intent</c>, in whatever session it came
    /// from. <see cref="DeclaredIntent.Sequence"/> is the call's own RAW timestamp read as Unix
    /// milliseconds — <see cref="RawEvent.Sequence"/> only orders events within one session
    /// (<c>AecoPostMortem.Data/CLAUDE.md</c>'s own <c>raw_event(session_id, seq)</c> index), and
    /// <see cref="PhaseOrdering"/> needs an ordering that holds across every session in the corpus.
    /// A call whose <c>arguments</c> is not object-shaped, or carries no string <c>intent</c>, is
    /// excluded rather than guessed at — the same "a parser-defect edge case is excluded, not
    /// trusted" discipline <c>RepeatedFileReadFindingCheck.ReadEventsFrom</c> already follows.
    /// </summary>
    public static IReadOnlyList<DeclaredIntent> Find(string sessionId, IReadOnlyList<RawEvent> sessionEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(sessionEvents);

        var intents = new List<DeclaredIntent>();

        foreach (var raw in sessionEvents)
        {
            if (raw.EventType != "tool.execution_start" || !EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            if (envelope.Data.ValueKind != JsonValueKind.Object
                || !envelope.Data.TryGetProperty("toolName", out var toolName)
                || toolName.ValueKind != JsonValueKind.String
                || toolName.GetString() != DeclaringToolName
                || !envelope.Data.TryGetProperty("arguments", out var argumentsElement))
            {
                continue;
            }

            var phase = IntentOf(argumentsElement);
            if (phase is null)
            {
                continue;
            }

            intents.Add(new DeclaredIntent
            {
                SessionId = sessionId,
                Phase = phase,
                Sequence = DateTimeOffset.Parse(raw.Timestamp).ToUnixTimeMilliseconds(),
            });
        }

        return intents;
    }

    static string? IntentOf(JsonElement argumentsElement)
    {
        var arguments = ToolArguments.Parse(argumentsElement.GetRawText());
        return arguments.Kind == ToolArgumentKind.Object
            && arguments.TryGetProperty("intent", out var intent)
            && intent.ValueKind == JsonValueKind.String
                ? intent.GetString()
                : null;
    }
}
