using System.Text.Json;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-2/FR-6's envelope fields — <c>type</c> and <c>ts</c> — read out of one line. A line that is
/// not a JSON object, or is missing either field as a string, is malformed rather than parsed.
/// </summary>
public interface IEventEnvelopeParser
{
    bool TryParse(ReadOnlySpan<byte> line, out string? eventType, out string? timestamp);
}

/// <summary>The one envelope shape measured in the corpus (event-schema version 1, 35 of 35
/// sessions). A version this product has not seen still needs an envelope reader — see
/// <see cref="EventEnvelopeParsers.For"/> — so this shape is also the fallback.</summary>
public sealed class EventEnvelopeParserV1 : IEventEnvelopeParser
{
    public bool TryParse(ReadOnlySpan<byte> line, out string? eventType, out string? timestamp)
    {
        eventType = null;
        timestamp = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line.ToArray());
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetString(root, "type", out eventType))
            {
                return false;
            }

            if (!TryGetString(root, "ts", out timestamp))
            {
                return false;
            }

            return true;
        }
    }

    static bool TryGetString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null;
    }
}

/// <summary>
/// FR-3: parsers are registered against the event-schema version line 1 declares, not scanned for.
/// A version not in this registry — tomorrow's, not yet measured — still ingests: it falls back to
/// the one shape measured today rather than refusing.
/// </summary>
public static class EventEnvelopeParsers
{
    public const long DefaultSchemaVersion = 1;

    static readonly IReadOnlyDictionary<long, IEventEnvelopeParser> Registry =
        new Dictionary<long, IEventEnvelopeParser> { [DefaultSchemaVersion] = new EventEnvelopeParserV1() };

    public static IEventEnvelopeParser For(long? eventSchemaVersion) =>
        eventSchemaVersion is { } version && Registry.TryGetValue(version, out var parser)
            ? parser
            : Registry[DefaultSchemaVersion];
}
