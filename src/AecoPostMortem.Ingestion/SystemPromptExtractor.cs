using System.Text.Json;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-12: pulls the system prompt out of a <c>system.message</c> RAW event and hashes it, so it can
/// be deduplicated by content rather than stored once per event. RAW keeps every event's own
/// verbatim copy regardless (FR-2); this is the additional, content-addressed representation that
/// keeps a measured 337 near-duplicate system messages (median 54,335 characters, data map Part 6)
/// from becoming 337 near-duplicate rows in <see cref="SystemPromptText"/>.
/// </summary>
public static class SystemPromptExtractor
{
    const string EventType = "system.message";

    /// <summary>
    /// The prompt text a <c>system.message</c> event carries, or <see langword="null"/> for any
    /// other event type, or one whose <c>data.content</c> field is missing or not a string.
    /// Deterministic and re-derivable: the same payload always yields the same hash, which is what
    /// lets a session resolve its own full text by re-running this over its own RAW event and
    /// joining the result against the stored dedup table (Gherkin: "each session still resolves to
    /// its own full prompt text").
    /// </summary>
    public static SystemPromptText? Extract(RawEvent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (!string.Equals(raw.EventType, EventType, StringComparison.Ordinal))
        {
            return null;
        }

        using var document = JsonDocument.Parse(raw.Payload);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = content.GetString()!;
        return new SystemPromptText(RawPayload.ContentHashOfText(text), text);
    }

    /// <summary>
    /// Every distinct prompt text among <paramref name="events"/>, one row per content hash — the
    /// set <see cref="SystemPromptTextBatch.Append"/> is meant to be called with. Events that carry
    /// no prompt (anything but <c>system.message</c>, or one with no usable content) contribute
    /// nothing.
    /// </summary>
    public static IReadOnlyList<SystemPromptText> DistinctTexts(IEnumerable<RawEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<SystemPromptText>();

        foreach (var raw in events)
        {
            if (Extract(raw) is not { } extracted)
            {
                continue;
            }

            if (seen.Add(extracted.ContentHash))
            {
                distinct.Add(extracted);
            }
        }

        return distinct;
    }
}
