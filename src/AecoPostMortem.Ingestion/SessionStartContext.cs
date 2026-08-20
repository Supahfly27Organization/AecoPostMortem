using System.Text.Json;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-7's key: <c>session.start.data.context.cwd</c>, measured present on 35 of 35 sessions
/// (<c>Data.Execution.Session.Cwd</c>). Reads it off a session's own <c>session.start</c> event —
/// the first event in <see cref="SessionEventReader.Read"/>'s own output, mirroring the "line 1
/// only" rule <see cref="SessionEventReader.ReadDeclaredVersion"/> already applies to
/// provider/schema version: it is never searched for elsewhere in the stream.
/// </summary>
public static class SessionStartContext
{
    /// <summary>
    /// No events, a first event that is not <c>session.start</c>, or a missing
    /// <c>context.cwd</c> field all read as unknown (<see langword="null"/>) rather than as an
    /// error — <see cref="SessionExclusion.Evaluate"/> treats an unknown cwd as never excluded,
    /// because this product cannot exclude a session it cannot place.
    /// </summary>
    public static string? ExtractCwd(IReadOnlyList<RawEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0 || events[0].EventType != "session.start")
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(events[0].Payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("context", out var context)
                || context.ValueKind != JsonValueKind.Object
                || !context.TryGetProperty("cwd", out var cwd)
                || cwd.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return cwd.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
