using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Walks every session <see cref="SessionDiscovery"/> finds under a Copilot session-state root,
/// ingests each through <see cref="SessionIngestor"/> — which is where FR-7's exclusion and the
/// retroactive purge of a session excluded after it was already stored both happen — and rolls the
/// results up into FR-14's <see cref="CoverageReport"/>. Nothing here reads or writes RAW directly;
/// every store interaction still goes through <see cref="SessionIngestor.Ingest"/>, the same door a
/// single-session caller uses, so the coverage report can never disagree with what was actually
/// persisted.
/// </summary>
public static class IngestionRun
{
    public static CoverageReport Run(
        PostMortemContext context,
        string sessionStateRoot,
        IReadOnlyList<string> excludedRoots)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionStateRoot);
        ArgumentNullException.ThrowIfNull(excludedRoots);

        var discovery = SessionDiscovery.Discover(sessionStateRoot);

        var sessionsIngested = 0;
        var sessionsExcluded = new List<ExcludedSession>();
        long linesParsed = 0;
        long linesSkipped = 0;
        var eventsByType = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var session in discovery.Sessions)
        {
            if (session.EventsFile is not { } eventsFile)
            {
                continue;
            }

            var result = SessionIngestor.Ingest(context, session.SessionId, eventsFile, excludedRoots);

            linesParsed += result.Read.LinesRead;
            linesSkipped += result.Read.SkippedLines;

            if (result.Exclusion.Excluded)
            {
                sessionsExcluded.Add(new ExcludedSession(session.SessionId, result.Exclusion.Reason!));

                // FR-7's "no event from that session is persisted" has to hold for the derived
                // layer too, not only RAW — a session ingested before its cwd was excluded still
                // carries NORMALIZED rows from that earlier run until they are purged here.
                NormalizedLayerWriter.DeleteForSession(context, session.SessionId);
                continue;
            }

            // FR-5's refusal, not FR-7's: a rewritten file's read still contributes to
            // LinesParsed/LinesSkipped above (its lines were genuinely read this run), but nothing
            // from it reached RAW (EventsInserted is 0), so it must not read as "ingested" or have
            // its events folded into EventsByType — that would misrepresent what the store actually
            // holds after this run.
            if (result.RewriteDetected)
            {
                continue;
            }

            sessionsIngested++;

            foreach (var raw in result.Read.Events)
            {
                eventsByType[raw.EventType] = eventsByType.GetValueOrDefault(raw.EventType) + 1;
            }

            NormalizedLayerWriter.Derive(context, session.SessionId);
        }

        return new CoverageReport(
            discovery.Sessions.Count,
            sessionsIngested,
            sessionsExcluded,
            linesParsed,
            linesSkipped,
            eventsByType);
    }
}
