using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Builds one <see cref="Hook"/> row per <c>hook.start</c>/<c>hook.end</c> pair, matched by their
/// shared <c>data.hookInvocationId</c> (the pair's own natural key — unlike <see cref="Skill"/>,
/// neither event's own envelope <c>id</c> ties the two together). Populated at ingest time so a
/// hook-failure check has real rows to read instead of an empty table.
/// </summary>
public static class HookBuilder
{
    /// <summary>
    /// A start with no matching end is still reported — <c>EndedAt</c>/<c>Success</c> null, the
    /// same "unfinished, not malformed" treatment the rest of this project gives an in-flight event.
    /// An end with no matching start produces no row at all: <see cref="Hook.StartedAt"/> is
    /// <c>required</c>, and there is nothing honest to put there.
    /// </summary>
    public static IReadOnlyList<Hook> Build(string sessionId, IReadOnlyList<RawEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(events);

        var starts = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);
        var ends = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);

        foreach (var raw in events)
        {
            if (raw.EventType is not ("hook.start" or "hook.end") || !EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            var invocationId = StringOrNull(envelope.Data, "hookInvocationId");
            if (invocationId is null)
            {
                continue;
            }

            var target = raw.EventType == "hook.start" ? starts : ends;
            target[invocationId] = (raw, envelope);
        }

        var hooks = new List<Hook>();

        foreach (var (invocationId, (startRaw, startEnvelope)) in starts)
        {
            var hasEnd = ends.TryGetValue(invocationId, out var end);

            hooks.Add(new Hook
            {
                SessionId = sessionId,
                EventId = invocationId,
                Name = StringOrEmpty(startEnvelope.Data, "hookType"),
                StartedAt = startRaw.Timestamp,
                EndedAt = hasEnd ? end.Raw.Timestamp : null,
                Success = hasEnd ? BoolOrNull(end.Envelope.Data, "success") : null,
                OwnerKind = startEnvelope.AgentId is null ? OwnerKind.Main : OwnerKind.Agent,
                AgentId = startEnvelope.AgentId,
            });
        }

        return hooks;
    }

    static string StringOrEmpty(JsonElement data, string property) => StringOrNull(data, property) ?? string.Empty;

    static string? StringOrNull(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static bool? BoolOrNull(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
