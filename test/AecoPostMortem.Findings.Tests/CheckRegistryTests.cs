using System.Reflection;
using System.Runtime.CompilerServices;

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
                    Provenance = Provenance.Inferred,
                },
                new CheckRegistryEntry
                {
                    CheckId = "written-content-forbidden-symbol",
                    Status = CheckRunStatus.Refused,
                    Population = 3,
                    RefusalReason = "scope mechanism ambiguous",
                    Provenance = Provenance.Derived,
                },
            ],
            SessionsInScope = 35,
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
            Provenance = Provenance.Derived,
        };

        var clean = new CheckRegistryEntry
        {
            CheckId = "contradiction-check",
            Status = CheckRunStatus.Ran,
            Population = 35,
            FindingCount = 0,
            Provenance = Provenance.Inferred,
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
            Provenance = Provenance.Derived,
        };

        Assert.Equal(3, refused.Population);
    }

    /// <summary>FR-42 (issue #46)'s provenance badge: a silent check's own provenance travels on the
    /// registry entry itself, the same "required, not validated" discipline <c>Finding.Provenance</c>
    /// already uses (<c>FindingTests</c>) — a caller cannot build an entry without stating it.</summary>
    [Fact]
    public void Provenance_is_a_required_member()
    {
        var property = typeof(CheckRegistryEntry).GetProperty(nameof(CheckRegistryEntry.Provenance));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void An_entry_carries_the_provenance_the_check_would_have_produced()
    {
        var entry = new CheckRegistryEntry
        {
            CheckId = "hook-failure",
            Status = CheckRunStatus.Ran,
            Population = 35,
            FindingCount = 0,
            Provenance = Provenance.Observed,
        };

        Assert.Equal(Provenance.Observed, entry.Provenance);
    }

    /// <summary>FR-42 (issue #46), the follow-on gap fixed here: <c>SessionsInScope</c> is the size of
    /// the whole analysis scope every entry's own <c>Population</c> was drawn from — distinct from any
    /// one check's narrower population — required the same "a caller-stated fact, never guessed"
    /// discipline <see cref="CheckRegistryEntry.Provenance"/> already uses.</summary>
    [Fact]
    public void SessionsInScope_is_a_required_member()
    {
        var property = typeof(CheckRegistry).GetProperty(nameof(CheckRegistry.SessionsInScope));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }
}
