using AecoPostMortem.Findings;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>Issue #23, Scenario 4: every check appears in the registry, whether or not it fired.
/// The malformed-line check is FR-6's own — this is where it registers itself.</summary>
public sealed class MalformedLineCheckTests
{
    static SessionReadResult ResultWith(long linesRead, long skippedLines) =>
        new(
            SourceFile: "events.jsonl",
            Events: [],
            LinesRead: linesRead,
            SkippedLines: skippedLines,
            HighWaterOffset: 0,
            ProviderVersion: SessionEventReader.UnknownProviderVersion,
            EventSchemaVersion: null);

    [Fact]
    public void A_completed_ingest_with_no_malformed_lines_still_registers_the_check()
    {
        var entry = MalformedLineCheck.From([ResultWith(linesRead: 5, skippedLines: 0)]);

        Assert.Equal(MalformedLineCheck.CheckId, entry.CheckId);
        Assert.Equal(CheckRunStatus.Ran, entry.Status);
        Assert.Equal(5, entry.Population);
        Assert.Equal(0, entry.FindingCount);
    }

    [Fact]
    public void A_completed_ingest_with_malformed_lines_reports_the_skipped_count()
    {
        var entry = MalformedLineCheck.From([ResultWith(linesRead: 5, skippedLines: 2)]);

        Assert.Equal(5, entry.Population);
        Assert.Equal(2, entry.FindingCount);
    }

    [Fact]
    public void Population_and_finding_count_sum_across_every_file_in_the_run()
    {
        var entry = MalformedLineCheck.From(
        [
            ResultWith(linesRead: 3, skippedLines: 1),
            ResultWith(linesRead: 4, skippedLines: 0),
        ]);

        Assert.Equal(7, entry.Population);
        Assert.Equal(1, entry.FindingCount);
    }
}
