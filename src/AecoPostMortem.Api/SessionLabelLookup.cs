using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// A session's own display label (Digest session-naming, Slice 2): the first five words of its
/// earliest real prompt, so a session link on the Digest reads as something other than a bare GUID.
/// Simpler than <see cref="PromptTextLookup"/> — a session's first prompt is unambiguous by
/// definition, so this needs no <c>turn_start</c>/<c>interactionId</c> join at all, only the earliest
/// <c>user.message</c> event by <see cref="RawEvent.Sequence"/>. Truncation happens here, not in the
/// browser — the same "the app derives nothing, the server states the fact" discipline
/// <c>Masthead</c>/<c>AdherenceFigureBlock</c> already hold to on the web side.
/// </summary>
public static class SessionLabelLookup
{
    const int WordCount = 5;

    /// <summary><see langword="null"/> when this session carries no <c>user.message</c> event with
    /// real content — "absence in, absence out", the same discipline
    /// <see cref="HookFailureEventLookup.Find"/> already follows rather than serving a placeholder
    /// string a caller would have to special-case.</summary>
    public static string? Find(string sessionId, IReadOnlyList<RawEvent> sessionEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(sessionEvents);

        var earliest = sessionEvents
            .Where(e => e.EventType == "user.message")
            .OrderBy(e => e.Sequence)
            .Select(TryReadContent)
            .FirstOrDefault(content => content is { Length: > 0 });

        return earliest is null ? null : Truncate(earliest);
    }

    static string? TryReadContent(RawEvent raw)
    {
        if (!EventEnvelopeReader.TryRead(raw, out var envelope)
            || envelope.Data.ValueKind != JsonValueKind.Object
            || !envelope.Data.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return content.GetString();
    }

    static string Truncate(string content)
    {
        var words = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return words.Length <= WordCount
            ? string.Join(' ', words)
            : string.Join(' ', words.Take(WordCount)) + "…";
    }
}
