using System.Text;
using System.Text.Json;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-3 and FR-6: read one <c>events.jsonl</c> line by line into RAW rows, without silently
/// dropping anything. The provider version and event-schema version come from line 1 only — the
/// file is never scanned for a later declaration. A trailing line with no terminating newline is
/// unfinished, not malformed: reading stops there and the byte offset it starts at is reported as
/// the high-water mark. A line that fails to parse is skipped and counted, never fatal, and never
/// remembered as permanently bad — the whole file is read again on the next run, so a line that
/// completes between runs is retried automatically.
/// </summary>
public static class SessionEventReader
{
    /// <summary>What a session's provider version reads as when line 1 does not declare one — an
    /// unparseable, missing or non-<c>session.start</c> first line. Not scanning past line 1 (FR-3)
    /// means this is possible, and it must not stop the line from still being read as an event.</summary>
    public const string UnknownProviderVersion = "unknown";

    public static SessionReadResult Read(string sessionId, string sourceFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        using var stream = SourceFiles.OpenRead(sourceFile);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        var lines = SplitLines(bytes);

        var (providerVersion, eventSchemaVersion) = lines.Count > 0
            ? ReadDeclaredVersion(lines[0].Content.Span)
            : (UnknownProviderVersion, (long?)null);

        var parser = EventEnvelopeParsers.For(eventSchemaVersion);

        var events = new List<RawEvent>();
        long offset = 0;
        long lineIndex = 0;
        long skipped = 0;

        foreach (var line in lines)
        {
            if (!line.Terminated)
            {
                break;
            }

            var startOffset = offset;
            offset += line.Content.Length + 1;

            if (TryBuildEvent(parser, sessionId, lineIndex, sourceFile, startOffset, providerVersion, line.Content.Span, out var raw))
            {
                events.Add(raw);
            }
            else
            {
                skipped++;
            }

            lineIndex++;
        }

        return new SessionReadResult(
            sourceFile,
            events,
            LinesRead: lineIndex,
            SkippedLines: skipped,
            HighWaterOffset: offset,
            ProviderVersion: providerVersion,
            EventSchemaVersion: eventSchemaVersion);
    }

    /// <summary>FR-3: line 1 only. <see cref="JsonException"/> — malformed JSON, or bytes that are
    /// not valid UTF-8 — reads the same as a first line that simply does not declare a version:
    /// unknown, not an error, and not a reason to look further into the file.</summary>
    static (string ProviderVersion, long? EventSchemaVersion) ReadDeclaredVersion(ReadOnlySpan<byte> firstLine)
    {
        try
        {
            using var document = JsonDocument.Parse(firstLine.ToArray());
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeProperty)
                || typeProperty.ValueKind != JsonValueKind.String
                || typeProperty.GetString() != "session.start"
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return (UnknownProviderVersion, null);
            }

            var providerVersion =
                data.TryGetProperty("copilotVersion", out var copilotVersion)
                && copilotVersion.ValueKind == JsonValueKind.String
                && copilotVersion.GetString() is { } version
                    ? version
                    : UnknownProviderVersion;

            long? eventSchemaVersion =
                data.TryGetProperty("version", out var declaredVersion)
                && declaredVersion.ValueKind == JsonValueKind.Number
                && declaredVersion.TryGetInt64(out var schemaVersion)
                    ? schemaVersion
                    : null;

            return (providerVersion, eventSchemaVersion);
        }
        catch (JsonException)
        {
            return (UnknownProviderVersion, null);
        }
    }

    static bool TryBuildEvent(
        IEventEnvelopeParser parser,
        string sessionId,
        long sequence,
        string sourceFile,
        long byteOffset,
        string providerVersion,
        ReadOnlySpan<byte> lineBytes,
        out RawEvent raw)
    {
        raw = null!;

        string payload;
        try
        {
            payload = RawPayload.FromUtf8(lineBytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!parser.TryParse(lineBytes, out var eventType, out var timestamp)
            || eventType is null
            || timestamp is null)
        {
            return false;
        }

        raw = new RawEvent(
            sessionId,
            sequence,
            eventType,
            timestamp,
            providerVersion,
            sourceFile,
            byteOffset,
            RawPayload.ContentHash(lineBytes),
            payload);
        return true;
    }

    /// <summary>Splits raw bytes on <c>\n</c> only — nothing else about a line's bytes is
    /// interpreted, so the payload stays byte-exact (DOMAIN_MODEL.md's round-trip invariant). The
    /// final segment is unterminated exactly when the file does not end in <c>\n</c>: a session file
    /// that is still being written by a live Copilot session.</summary>
    static List<(ReadOnlyMemory<byte> Content, bool Terminated)> SplitLines(byte[] bytes)
    {
        var lines = new List<(ReadOnlyMemory<byte>, bool)>();

        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                lines.Add((bytes.AsMemory(start, i - start), true));
                start = i + 1;
            }
        }

        if (start < bytes.Length)
        {
            lines.Add((bytes.AsMemory(start, bytes.Length - start), false));
        }

        return lines;
    }
}

/// <summary>
/// One <c>events.jsonl</c> read: the RAW rows it produced, and the stats FR-6 and FR-14 need —
/// lines read, lines skipped, and the byte offset reading stopped at. <see cref="LinesRead"/> counts
/// complete, newline-terminated lines only; an unterminated trailing line is in neither count
/// (Scenario: "A partial trailing line is unfinished, not malformed").
/// </summary>
public sealed record SessionReadResult(
    string SourceFile,
    IReadOnlyList<RawEvent> Events,
    long LinesRead,
    long SkippedLines,
    long HighWaterOffset,
    string ProviderVersion,
    long? EventSchemaVersion);
