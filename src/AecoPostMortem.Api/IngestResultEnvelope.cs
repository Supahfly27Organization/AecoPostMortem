using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// POST /api/ingest's response contract: FR-14's <see cref="CoverageReport"/> carried onto the wire
/// verbatim, plus how long the run actually took. The duration is measured server-side, around the
/// real <see cref="IngestionRun.Run"/> call, rather than left for a client to infer from request/
/// response timestamps — a client-side measurement would also fold in network latency, which is
/// zero here (loopback) but would make this contract lie the moment it is ever reused. This mirrors
/// the CLI's own stdout report (<c>AecoPostMortem.Cli.CommandRunner.WriteCoverageReport</c>) field
/// for field, so a browser and a terminal reading the same run never disagree about what it found.
/// </summary>
public sealed record IngestResultEnvelope(
    int SessionsFound,
    int SessionsIngested,
    IReadOnlyList<ExcludedSessionEnvelope> SessionsExcluded,
    long LinesParsed,
    long LinesSkipped,
    IReadOnlyDictionary<string, long> EventsByType,
    double DurationSeconds)
{
    public static IngestResultEnvelope From(CoverageReport report, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new IngestResultEnvelope(
            report.SessionsFound,
            report.SessionsIngested,
            report.SessionsExcluded
                .Select(excluded => new ExcludedSessionEnvelope(excluded.SessionId, excluded.Reason))
                .ToList(),
            report.LinesParsed,
            report.LinesSkipped,
            report.EventsByType,
            duration.TotalSeconds);
    }
}

/// <summary>One session FR-7 kept out of the store this run, and why — <see cref="ExcludedSession"/>
/// carried onto the wire unchanged.</summary>
public sealed record ExcludedSessionEnvelope(string SessionId, string Reason);
