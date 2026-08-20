namespace AecoPostMortem.Api.Tests;

/// <summary>
/// S-48, Scenarios 2 and 3: the two empty states are different diagnoses with different fixes, and
/// must not collapse into one message.
/// </summary>
public sealed class AppStateReportTests
{
    [Fact]
    public void No_Copilot_directory_reports_no_source_found()
    {
        var report = AppStateReport.Diagnose(copilotSourceFound: false, storeHasBeenIngested: false);

        Assert.Equal(AppStateKind.NoSourceFound, report.Kind);
        Assert.Contains("no source", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_store_names_the_ingest_command_as_the_fix()
    {
        var report = AppStateReport.Diagnose(copilotSourceFound: true, storeHasBeenIngested: false);

        Assert.Equal(AppStateKind.EmptyStore, report.Kind);
        Assert.Contains("nothing has been ingested", report.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AppStateReport.IngestCommand, report.FixCommand);
    }

    [Fact]
    public void No_source_found_names_no_fix_command_it_is_distinct_from_an_empty_store()
    {
        var noSource = AppStateReport.Diagnose(copilotSourceFound: false, storeHasBeenIngested: false);
        var emptyStore = AppStateReport.Diagnose(copilotSourceFound: true, storeHasBeenIngested: false);

        Assert.Null(noSource.FixCommand);
        Assert.NotEqual(noSource.Message, emptyStore.Message);
        Assert.NotEqual(noSource.Kind, emptyStore.Kind);
    }

    [Fact]
    public void A_missing_Copilot_directory_takes_priority_over_an_empty_store()
    {
        // Both conditions are true at once on a fresh machine: no Copilot directory implies no
        // store could have been ingested either. The diagnosis names the root cause.
        var report = AppStateReport.Diagnose(copilotSourceFound: false, storeHasBeenIngested: false);

        Assert.Equal(AppStateKind.NoSourceFound, report.Kind);
    }

    [Fact]
    public void A_source_and_an_ingested_store_report_ready()
    {
        var report = AppStateReport.Diagnose(copilotSourceFound: true, storeHasBeenIngested: true);

        Assert.Equal(AppStateKind.Ready, report.Kind);
    }
}
