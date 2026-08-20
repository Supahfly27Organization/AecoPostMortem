using AecoPostMortem.Findings;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>Issue #23, Scenario 4: every check appears in the registry, whether or not it fired.
/// The spawn-resolution check is FR-9's own.</summary>
public sealed class SpawnResolutionCheckTests
{
    [Fact]
    public void A_clean_reconstruction_still_registers_the_check()
    {
        var entry = SpawnResolutionCheck.From(examined: 5, unresolved: 0);

        Assert.Equal(SpawnResolutionCheck.CheckId, entry.CheckId);
        Assert.Equal(CheckRunStatus.Ran, entry.Status);
        Assert.Equal(5, entry.Population);
        Assert.Equal(0, entry.FindingCount);
    }

    [Fact]
    public void An_unresolved_spawn_is_reported_as_a_finding_not_dropped()
    {
        var entry = SpawnResolutionCheck.From(examined: 5, unresolved: 1);

        Assert.Equal(5, entry.Population);
        Assert.Equal(1, entry.FindingCount);
    }
}
