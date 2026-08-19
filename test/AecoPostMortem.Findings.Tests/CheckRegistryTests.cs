namespace AecoPostMortem.Findings.Tests;

/// <summary>Scenarios 4 and 5 of the finding contract (issue #23): every check is registered whether
/// or not it fired, and a refused check is distinguishable from a clean one.</summary>
public sealed class CheckRegistryTests
{
    [Fact]
    public void Every_check_appears_regardless_of_status()
    {
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "contradiction-check",
                    Status = CheckRunStatus.Ran,
                    Population = 35,
                    FindingCount = 0,
                },
                new CheckRegistryEntry
                {
                    CheckId = "written-content-forbidden-symbol",
                    Status = CheckRunStatus.Refused,
                    Population = 3,
                    RefusalReason = "scope mechanism ambiguous",
                },
            ],
        };

        Assert.Equal(2, registry.Entries.Count);
        Assert.Contains(registry.Entries, entry => entry.CheckId == "contradiction-check");
        Assert.Contains(registry.Entries, entry => entry.CheckId == "written-content-forbidden-symbol");
    }

    [Fact]
    public void A_refused_check_is_distinguishable_from_a_clean_one_not_both_zero()
    {
        var refused = new CheckRegistryEntry
        {
            CheckId = "written-content-forbidden-symbol",
            Status = CheckRunStatus.Refused,
            Population = 3,
            RefusalReason = "scope mechanism ambiguous",
        };

        var clean = new CheckRegistryEntry
        {
            CheckId = "contradiction-check",
            Status = CheckRunStatus.Ran,
            Population = 35,
            FindingCount = 0,
        };

        Assert.Null(refused.FindingCount);
        Assert.NotNull(clean.FindingCount);
        Assert.Equal(0, clean.FindingCount);
        Assert.NotEqual(refused.Status, clean.Status);
    }

    [Fact]
    public void Population_is_required_even_when_refused()
    {
        var refused = new CheckRegistryEntry
        {
            CheckId = "written-content-forbidden-symbol",
            Status = CheckRunStatus.Refused,
            Population = 3,
            RefusalReason = "scope mechanism ambiguous",
        };

        Assert.Equal(3, refused.Population);
    }
}
