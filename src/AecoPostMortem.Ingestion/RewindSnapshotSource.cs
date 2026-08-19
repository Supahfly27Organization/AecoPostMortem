using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-13: <c>rewind-snapshots/index.json</c> read under the one-file-one-event rule — the same rule
/// a <c>.meta.json</c> style source would use — because it is a single JSON object, not a JSONL
/// stream. Unlike <c>.meta.json</c> it is rewritten in place as the session grows, so every read
/// lands at byte offset zero and RAW's identity triple is what tells two versions apart: the
/// content hash differs, so both are appended rather than one silently replacing the other.
/// </summary>
public static class RewindSnapshotSource
{
    /// <summary>The RAW event type recorded for a whole-file read of the index, distinct from any
    /// envelope type Copilot itself writes into <c>events.jsonl</c>.</summary>
    public const string EventType = "rewind_snapshot.index";

    /// <summary>
    /// Read the whole file as one RAW event. Goes through <see cref="SourceFiles.OpenRead"/>, the
    /// one door onto <c>~/.copilot/</c>, so a concurrent writer is never locked out.
    /// </summary>
    /// <param name="sessionId">The session directory the file belongs to.</param>
    /// <param name="sequence">This event's position in the caller's tape — the file itself carries
    /// no sequence of its own, since it is not a line in a stream.</param>
    /// <param name="timestamp">The event's own timestamp, supplied by the caller rather than read
    /// from the wall clock (§3.8): the index has no envelope timestamp of its own to lift out the
    /// way <c>events.jsonl</c> lines do.</param>
    /// <param name="providerVersion">The session's Copilot version, supplied by the caller for the
    /// same reason RAW's other rows carry it — an index cross-reference, not a value this file
    /// states about itself. Defaults to empty: a future ingest pipeline that already knows the
    /// session's version from <c>session.start</c> passes it; a caller that does not have it yet is
    /// not blocked by this parameter.</param>
    /// <param name="path">The index file's path.</param>
    public static RawEvent ReadAsEvent(
        string sessionId,
        long sequence,
        string timestamp,
        string path,
        string providerVersion = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timestamp);
        ArgumentNullException.ThrowIfNull(providerVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] bytes;
        using (var stream = SourceFiles.OpenRead(path))
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        var text = RawPayload.FromUtf8(bytes);

        return new RawEvent(
            sessionId,
            sequence,
            EventType,
            timestamp,
            providerVersion,
            path,
            ByteOffset: 0,
            RawPayload.ContentHash(bytes),
            text);
    }
}
