using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Reads one session file and lands what parsed in RAW. The read is stateless — every call reads
/// the whole file again — which is what makes FR-6's retry rule true for free: a line skipped on one
/// run is attempted again on the next, because nothing records it as bad in between. RAW's own
/// identity index (Repo Rule 5, <see cref="RawEventBatch"/>) is what keeps a re-ingested line from
/// being inserted twice.
///
/// FR-5's second line of defence lives here too: before appending, every newly read event's byte
/// offset is checked against what RAW already stores for this <paramref name="sourceFile"/>
/// (<see cref="RawEventBatch.DetectRewrites"/>). A different content hash at an already-stored
/// offset means the file was rewritten, not grown — a resumed session continues the same byte
/// stream, so this can only mean the byte-offset identity assumption no longer holds. When that
/// happens nothing from this read is appended: the mismatch is reported on the result instead, so
/// it is surfaced rather than silently merged over what is already stored.
///
/// FR-7's self-exclusion is checked first, ahead of both the rewrite check and the append: a
/// session whose <c>session.start.data.context.cwd</c> falls under an excluded root
/// (<see cref="SessionExclusion"/>) never reaches <see cref="RawEventBatch.Append"/> at all — "at
/// ingest, not as a later filter" is true because there is no code path that appends its events
/// first and filters them out afterwards. The exclusion is also retroactive: if a session was
/// already ingested under a stale (or absent) exclusion list, the rows RAW already holds for it are
/// removed here too (<see cref="RawEventBatch.DeleteBySession"/>), so an operator who adds a root to
/// the list does not have to purge the store by hand to make it take effect on what is already
/// there.
/// </summary>
public static class SessionIngestor
{
    public static SessionIngestResult Ingest(
        PostMortemContext context,
        string sessionId,
        string sourceFile,
        IReadOnlyList<string>? excludedRoots = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var read = SessionEventReader.Read(sessionId, sourceFile);

        var cwd = SessionStartContext.ExtractCwd(read.Events);
        var exclusion = SessionExclusion.Evaluate(cwd, excludedRoots ?? []);

        if (exclusion.Excluded)
        {
            var purged = RawEventBatch.DeleteBySession(context, sessionId);
            return new SessionIngestResult(read, EventsInserted: 0, RewriteMismatches: [], exclusion, purged);
        }

        var mismatches = RawEventBatch.DetectRewrites(context, read.Events);
        if (mismatches.Count > 0)
        {
            return new SessionIngestResult(
                read, EventsInserted: 0, mismatches, SessionExclusionOutcome.NotExcluded, PurgedEventCount: 0);
        }

        var inserted = RawEventBatch.Append(context, read.Events);

        return new SessionIngestResult(
            read, inserted, RewriteMismatches: [], SessionExclusionOutcome.NotExcluded, PurgedEventCount: 0);
    }
}

/// <summary>One file's read, plus how many of its events were newly inserted — fewer than
/// <see cref="SessionReadResult.Events"/>'s count on a re-ingest, since RAW's identity index skips
/// what is already stored. <see cref="EventsInserted"/> is always <c>0</c> when
/// <see cref="RewriteDetected"/> is <c>true</c> or <see cref="Exclusion"/> is excluded: a detected
/// rewrite is refused outright rather than partially appended, since the point at which the byte
/// stream diverged from what is stored is not itself trustworthy as a resume point, and an excluded
/// session never reaches <see cref="RawEventBatch.Append"/> at all.</summary>
public sealed record SessionIngestResult(
    SessionReadResult Read,
    int EventsInserted,
    IReadOnlyList<RawRewriteMismatch> RewriteMismatches,
    SessionExclusionOutcome Exclusion,
    int PurgedEventCount)
{
    /// <summary>True when <see cref="Read"/>'s source file no longer holds FR-5's append-only
    /// assumption — its existing bytes at an already-stored offset no longer hash to what RAW has
    /// on file for that offset.</summary>
    public bool RewriteDetected => RewriteMismatches.Count > 0;
}
