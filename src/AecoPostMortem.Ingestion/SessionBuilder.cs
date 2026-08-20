using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Builds one <see cref="Session"/> row from a session's own <c>session.start</c> and
/// <c>session.shutdown</c> events — the NORMALIZED layer's own identity row, populated at ingest
/// time so <c>AecoPostMortem.Api.ApiHost.GetSession</c> has something to read.
/// </summary>
public static class SessionBuilder
{
    /// <summary>
    /// The "line 1 only" rule <see cref="SessionStartContext.ExtractCwd"/> already applies to
    /// <c>context.cwd</c> alone, applied here to the whole identity row: <paramref name="events"/>
    /// must be ordered by <see cref="RawEvent.Sequence"/>, and only its first entry is ever
    /// consulted for <c>session.start</c>. No events, or a first event that is not
    /// <c>session.start</c>, produce no <see cref="Session"/> at all.
    /// </summary>
    public static Session? Build(string sessionId, IReadOnlyList<RawEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0 || events[0].EventType != "session.start")
        {
            return null;
        }

        var start = events[0];
        using var startDocument = JsonDocument.Parse(start.Payload);
        var startRoot = startDocument.RootElement;

        // RAW never discards unknown or absent JSON (FR-2) — a line missing `data` entirely still
        // reaches RAW so long as it parsed and declared `type`/`timestamp`. `default` here is a
        // JsonElement of Kind Undefined, and every read below already treats a non-Object element
        // as "field absent" rather than indexing into it.
        var startData = startRoot.ValueKind == JsonValueKind.Object
            && startRoot.TryGetProperty("data", out var startDataElement)
                ? startDataElement
                : default;

        var context = startData.ValueKind == JsonValueKind.Object
            && startData.TryGetProperty("context", out var contextElement)
                ? contextElement
                : default;

        var shutdown = events.FirstOrDefault(e => e.EventType == "session.shutdown");
        var tokens = shutdown is null ? null : ReadTokenTotals(shutdown);

        return new Session
        {
            SessionId = sessionId,
            StartedAt = start.Timestamp,
            EndedAt = shutdown?.Timestamp,
            CopilotVersion = start.ProviderVersion,
            EventSchemaVersion = RawTextOrEmpty(startData, "version"),
            SourceFile = start.SourceFile,
            Cwd = StringOrEmpty(context, "cwd"),
            GitRoot = StringOrNull(context, "gitRoot"),
            Branch = StringOrNull(context, "branch"),
            HeadCommit = StringOrNull(context, "headCommit"),
            Repository = StringOrNull(context, "repository"),
            HostType = StringOrNull(context, "hostType"),
            BaseCommit = StringOrNull(context, "baseCommit"),
            InputTokens = tokens?.InputTokens,
            OutputTokens = tokens?.OutputTokens,
            CacheReadTokens = tokens?.CacheReadTokens,
            CacheWriteTokens = tokens?.CacheWriteTokens,
            ReasoningTokens = tokens?.ReasoningTokens,
            ModelCount = tokens?.ModelCount,
        };
    }

    /// <summary>
    /// <c>session.shutdown.data.modelMetrics</c> keys one entry per model, each carrying its own
    /// <c>usage</c> block — summed across every model present, per <see cref="Session"/>'s own
    /// remarks ("summed across models; ModelCount says how many were summed"). No
    /// <c>modelMetrics</c>, or an empty one, produces no totals at all rather than a zero-filled row.
    /// </summary>
    static TokenTotals? ReadTokenTotals(RawEvent shutdown)
    {
        using var document = JsonDocument.Parse(shutdown.Payload);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!data.TryGetProperty("modelMetrics", out var modelMetrics)
            || modelMetrics.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long input = 0, output = 0, cacheRead = 0, cacheWrite = 0, reasoning = 0;
        var modelCount = 0;

        foreach (var model in modelMetrics.EnumerateObject())
        {
            if (!model.Value.TryGetProperty("usage", out var usage))
            {
                continue;
            }

            input += LongOrZero(usage, "inputTokens");
            output += LongOrZero(usage, "outputTokens");
            cacheRead += LongOrZero(usage, "cacheReadTokens");
            cacheWrite += LongOrZero(usage, "cacheWriteTokens");
            reasoning += LongOrZero(usage, "reasoningTokens");
            modelCount++;
        }

        return modelCount == 0
            ? null
            : new TokenTotals(input, output, cacheRead, cacheWrite, reasoning, modelCount);
    }

    static string StringOrEmpty(JsonElement element, string property) =>
        StringOrNull(element, property) ?? string.Empty;

    static string RawTextOrEmpty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.GetRawText()
            : string.Empty;

    static string? StringOrNull(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static long LongOrZero(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;

    sealed record TokenTotals(
        long InputTokens,
        long OutputTokens,
        long CacheReadTokens,
        long CacheWriteTokens,
        long ReasoningTokens,
        int ModelCount);
}
