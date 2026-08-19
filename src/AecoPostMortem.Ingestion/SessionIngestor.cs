using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Reads one session file and lands what parsed in RAW. The read is stateless — every call reads
/// the whole file again — which is what makes FR-6's retry rule true for free: a line skipped on one
/// run is attempted again on the next, because nothing records it as bad in between. RAW's own
/// identity index (Repo Rule 5, <see cref="RawEventBatch"/>) is what keeps a re-ingested line from
/// being inserted twice.
/// </summary>
public static class SessionIngestor
{
    public static SessionIngestResult Ingest(PostMortemContext context, string sessionId, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(context);

        var read = SessionEventReader.Read(sessionId, sourceFile);
        var inserted = RawEventBatch.Append(context, read.Events);

        return new SessionIngestResult(read, inserted);
    }
}

/// <summary>One file's read, plus how many of its events were newly inserted — fewer than
/// <see cref="SessionReadResult.Events"/>'s count on a re-ingest, since RAW's identity index skips
/// what is already stored.</summary>
public sealed record SessionIngestResult(SessionReadResult Read, int EventsInserted);
