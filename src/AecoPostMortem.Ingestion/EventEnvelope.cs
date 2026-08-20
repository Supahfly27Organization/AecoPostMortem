using System.Text.Json;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// The envelope fields FR-8 reconstructs causality and ownership from: <c>id</c> and <c>parentId</c>
/// — measured present on 100% of events, the direct analogue of Claude's <c>uuid</c>/<c>parentUuid</c>
/// — plus <c>agentId</c>, carried on subagent-scoped events only, and the event's own <c>data</c>
/// object. Absence of <c>agentId</c> means main thread, exactly: the data map cross-referenced every
/// <c>agentId</c> on a tool event against a known <c>subagent.started</c> handle and found zero that
/// were not one.
/// </summary>
public sealed record EventEnvelope(string Id, string? ParentId, string? AgentId, JsonElement Data);

/// <summary>
/// Reads <see cref="EventEnvelope"/> out of a <see cref="RawEvent"/>'s own <see cref="RawEvent.Payload"/>.
/// Separate from <see cref="EventEnvelopeParsers"/>, which reads only <c>type</c>/<c>ts</c> at RAW
/// ingest time — this reader is what execution-record reconstruction needs once a line is already
/// stored, and it is never used to decide whether a line is malformed (that already happened).
/// </summary>
public static class EventEnvelopeReader
{
    /// <summary>
    /// A line already accepted into RAW always has a <c>type</c> and a <c>ts</c>
    /// (<see cref="EventEnvelopeParsers"/> guarantees it), but nothing guarantees an envelope <c>id</c>
    /// — a line whose <c>id</c> is missing or not a string cannot take part in the causality chain, so
    /// it is refused here rather than assigned a synthetic identity that would look like a real one.
    /// </summary>
    public static bool TryRead(RawEvent raw, out EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(raw);
        envelope = null!;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw.Payload);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var idProperty)
                || idProperty.ValueKind != JsonValueKind.String
                || idProperty.GetString() is not { } id)
            {
                return false;
            }

            var parentId = root.TryGetProperty("parentId", out var parentIdProperty)
                && parentIdProperty.ValueKind == JsonValueKind.String
                    ? parentIdProperty.GetString()
                    : null;

            var agentId = root.TryGetProperty("agentId", out var agentIdProperty)
                && agentIdProperty.ValueKind == JsonValueKind.String
                    ? agentIdProperty.GetString()
                    : null;

            // Cloned so the element survives past the document's disposal at the end of this block.
            var data = root.TryGetProperty("data", out var dataProperty) && dataProperty.ValueKind == JsonValueKind.Object
                ? dataProperty.Clone()
                : default;

            envelope = new EventEnvelope(id, parentId, agentId, data);
            return true;
        }
    }
}
