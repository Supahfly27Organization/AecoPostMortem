using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Builds one <see cref="Permission"/> row per <c>permission.requested</c>/<c>permission.completed</c>
/// pair, matched by their shared <c>data.requestId</c> — the pair's own natural key, the same
/// "neither event's own envelope id ties the two together" shape <see cref="HookBuilder"/> already
/// follows for <c>hookInvocationId</c>. Populated at ingest time so <c>InterruptionLoadFinding</c>
/// has real rows to read instead of an empty table.
/// </summary>
public static class PermissionBuilder
{
    /// <summary>
    /// A request with no matching completion is still reported — <c>CompletedAt</c>/<c>ResultKind</c>
    /// null, the same "unfinished, not malformed" treatment <see cref="HookBuilder"/> gives an
    /// in-flight hook pair (a measured 1,033 requested against 1,031 completed means this is a real
    /// state, not an edge case). A completion with no matching request produces no row at all:
    /// <see cref="Permission.RequestedAt"/> is <c>required</c>, and there is nothing honest to put
    /// there.
    /// </summary>
    public static IReadOnlyList<Permission> Build(string sessionId, IReadOnlyList<RawEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(events);

        var requests = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);
        var completions = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);

        foreach (var raw in events)
        {
            if (raw.EventType is not ("permission.requested" or "permission.completed")
                || !EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            var requestId = JsonElementReading.StringOrNull(envelope.Data, "requestId");
            if (requestId is null)
            {
                continue;
            }

            var target = raw.EventType == "permission.requested" ? requests : completions;
            target[requestId] = (raw, envelope);
        }

        var permissions = new List<Permission>();

        foreach (var (requestId, (requestRaw, requestEnvelope)) in requests)
        {
            var hasCompletion = completions.TryGetValue(requestId, out var completion);

            permissions.Add(new Permission
            {
                SessionId = sessionId,
                EventId = requestId,
                RequestedAt = requestRaw.Timestamp,
                CompletedAt = hasCompletion ? completion.Raw.Timestamp : null,
                ResultKind = hasCompletion ? ResultKindOf(completion.Envelope.Data) : null,
                ToolCallId = hasCompletion
                    ? JsonElementReading.StringOrNull(completion.Envelope.Data, "toolCallId")
                    : PermissionRequestToolCallId(requestEnvelope.Data),
                OwnerKind = requestEnvelope.AgentId is null ? OwnerKind.Main : OwnerKind.Agent,
                AgentId = requestEnvelope.AgentId,
            });
        }

        return permissions;
    }

    /// <summary>From <c>permission.completed.data.result.kind</c> — a nested object, unlike every
    /// other field <see cref="JsonElementReading"/> reads directly off <c>data</c>.</summary>
    static string? ResultKindOf(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("result", out var result)
            ? JsonElementReading.StringOrNull(result, "kind")
            : null;

    /// <summary>From <c>permission.requested.data.permissionRequest.toolCallId</c> — the only place
    /// a tool call id is recorded when the request never completed.</summary>
    static string? PermissionRequestToolCallId(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("permissionRequest", out var request)
            ? JsonElementReading.StringOrNull(request, "toolCallId")
            : null;
}
