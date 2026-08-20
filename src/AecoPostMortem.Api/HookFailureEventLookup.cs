using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Findings;
using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-17's own gap, closed here rather than in <c>AecoPostMortem.Ingestion.HookBuilder</c>:
/// <see cref="AecoPostMortem.Data.Execution.Hook"/> has no error-text column (by design —
/// <c>AecoPostMortem.Findings/CLAUDE.md</c>, "evidence quotes the field Copilot wrote"), so a
/// hook-failure finding's error text can only come from a fresh read of the session's own RAW
/// <c>hook.end</c> events, the same narrow-RAW-read pattern <see cref="StepEvidenceLookup"/> and
/// <see cref="SubagentOutputLookup"/> already use rather than widening the derived layer for one
/// check's evidence.
/// </summary>
public static class HookFailureEventLookup
{
    /// <summary>
    /// Pairs <c>hook.start</c>/<c>hook.end</c> by their shared <c>hookInvocationId</c> — the same
    /// matching <c>HookBuilder.Build</c> performs — and returns one <see cref="HookFailureEvent"/>
    /// per pair whose <c>hook.end.data.success</c> is <see langword="false"/>. A pair with no
    /// completion, or one that succeeded, contributes nothing: <see cref="HookFailureFinding"/> only
    /// ever takes failures as input.
    /// </summary>
    public static IReadOnlyList<HookFailureEvent> Find(string sessionId, IReadOnlyList<RawEvent> sessionEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(sessionEvents);

        var starts = new HashSet<string>(StringComparer.Ordinal);
        var ends = new Dictionary<string, EventEnvelope>(StringComparer.Ordinal);

        foreach (var raw in sessionEvents)
        {
            if (raw.EventType is not ("hook.start" or "hook.end") || !EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            if (envelope.Data.ValueKind != JsonValueKind.Object
                || !envelope.Data.TryGetProperty("hookInvocationId", out var invocationIdProperty)
                || invocationIdProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var invocationId = invocationIdProperty.GetString()!;

            if (raw.EventType == "hook.start")
            {
                starts.Add(invocationId);
            }
            else
            {
                ends[invocationId] = envelope;
            }
        }

        var failures = new List<HookFailureEvent>();

        foreach (var invocationId in starts)
        {
            if (!ends.TryGetValue(invocationId, out var end) || !IsFailure(end.Data))
            {
                continue;
            }

            failures.Add(new HookFailureEvent
            {
                SessionId = sessionId,
                HookName = HookNameOf(end.Data),
                Success = false,
                Error = ErrorOf(end.Data),
            });
        }

        return failures;
    }

    static bool IsFailure(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("success", out var success)
        && success.ValueKind == JsonValueKind.False;

    static string HookNameOf(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("hookType", out var hookType)
        && hookType.ValueKind == JsonValueKind.String
            ? hookType.GetString()!
            : string.Empty;

    /// <summary>From <c>hook.end.data.error.message</c> — a nested object, present only for a
    /// failed pair.</summary>
    static string? ErrorOf(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("error", out var error)
        && error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty("message", out var message)
        && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : null;
}
