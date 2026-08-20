namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-14: what every ingest run states about itself, regardless of whether anything went wrong —
/// coverage is a report of what was seen, not just of what failed. <see cref="IngestionRun.Run"/> is
/// the only builder, so "the report" stays one shape everywhere it is read.
/// </summary>
public sealed record CoverageReport(
    int SessionsFound,
    int SessionsIngested,
    IReadOnlyList<ExcludedSession> SessionsExcluded,
    long LinesParsed,
    long LinesSkipped,
    IReadOnlyDictionary<string, long> EventsByType);

/// <summary>One session FR-7 kept out of the store this run, and the sentence FR-14 states for
/// it — <see cref="SessionExclusionOutcome.Reason"/>, carried forward rather than re-derived.</summary>
public sealed record ExcludedSession(string SessionId, string Reason);
