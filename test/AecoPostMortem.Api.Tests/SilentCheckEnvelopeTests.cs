using AecoPostMortem.Findings;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-42 (issue #46): the "checks that found nothing" surface. Reads the <see cref="CheckRegistry"/>
/// published by issue #23 and projects only the entries that ran clean — a check that never ran, or
/// one the registry has no entry for at all, must never be implied as clean (PRD §3.9).
/// </summary>
public sealed class SilentCheckEnvelopeTests
{
    [Fact]
    public void A_check_that_ran_and_found_nothing_states_its_denominator()
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
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        var entry = Assert.Single(silent);
        Assert.Equal("contradiction-check", entry.CheckId);
        Assert.Equal(35, entry.Population);
        Assert.Equal(0, entry.FindingCount);
    }

    [Fact]
    public void Every_clean_check_in_the_registry_appears_with_its_denominator()
    {
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "subagent-spawn-resolution",
                    Status = CheckRunStatus.Ran,
                    Population = 470,
                    FindingCount = 0,
                    Provenance = Provenance.Observed,
                },
                new CheckRegistryEntry
                {
                    CheckId = "malformed-line",
                    Status = CheckRunStatus.Ran,
                    Population = 56138,
                    FindingCount = 0,
                    Provenance = Provenance.Observed,
                },
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        Assert.Equal(2, silent.Count);
        Assert.Contains(silent, entry => entry.CheckId == "subagent-spawn-resolution" && entry.Population == 470);
        Assert.Contains(silent, entry => entry.CheckId == "malformed-line" && entry.Population == 56138);
    }

    [Fact]
    public void A_check_absent_from_the_registry_is_absent_from_the_surface_not_shown_as_clean()
    {
        // The contradiction check has not been built yet in this release: no entry for it exists at
        // all. Nothing in From() may synthesise one — absence in the registry means absence here.
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "malformed-line",
                    Status = CheckRunStatus.Ran,
                    Population = 56138,
                    FindingCount = 0,
                    Provenance = Provenance.Observed,
                },
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        Assert.DoesNotContain(silent, entry => entry.CheckId == "contradiction-check");
    }

    [Fact]
    public void A_refused_check_does_not_appear_as_a_clean_silent_check()
    {
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "written-content-forbidden-symbol",
                    Status = CheckRunStatus.Refused,
                    Population = 3,
                    RefusalReason = "scope mechanism ambiguous",
                    Provenance = Provenance.Derived,
                },
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        Assert.Empty(silent);
    }

    [Fact]
    public void A_check_that_ran_and_found_something_does_not_appear_on_this_surface()
    {
        // This surface is specifically "checks that found nothing" — a check that ran and did find
        // something is reported through the finding surface, not implied clean here.
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "tool-choice-adherence",
                    Status = CheckRunStatus.Ran,
                    Population = 40,
                    FindingCount = 3,
                    Provenance = Provenance.Derived,
                },
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        Assert.Empty(silent);
    }

    [Fact]
    public void A_mixed_registry_surfaces_only_the_clean_entries()
    {
        // All four entry kinds in one registry, exercised together, so the filter's behaviour is
        // proven to compose rather than merely holding in each single-kind test above.
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "subagent-spawn-resolution",
                    Status = CheckRunStatus.Ran,
                    Population = 470,
                    FindingCount = 0,
                    Provenance = Provenance.Observed,
                },
                new CheckRegistryEntry
                {
                    CheckId = "malformed-line",
                    Status = CheckRunStatus.Ran,
                    Population = 56138,
                    FindingCount = 0,
                    Provenance = Provenance.Observed,
                },
                new CheckRegistryEntry
                {
                    CheckId = "written-content-forbidden-symbol",
                    Status = CheckRunStatus.Refused,
                    Population = 3,
                    RefusalReason = "scope mechanism ambiguous",
                    Provenance = Provenance.Derived,
                },
                new CheckRegistryEntry
                {
                    CheckId = "tool-choice-adherence",
                    Status = CheckRunStatus.Ran,
                    Population = 40,
                    FindingCount = 3,
                    Provenance = Provenance.Derived,
                },
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        Assert.Equal(2, silent.Count);
        Assert.Contains(silent, entry => entry.CheckId == "subagent-spawn-resolution");
        Assert.Contains(silent, entry => entry.CheckId == "malformed-line");
        Assert.DoesNotContain(silent, entry => entry.CheckId == "written-content-forbidden-symbol");
        Assert.DoesNotContain(silent, entry => entry.CheckId == "tool-choice-adherence");
        Assert.DoesNotContain(silent, entry => entry.CheckId == "contradiction-check");
    }

    /// <summary>Mockup parity item #6 (`docs/product-superpowers/discovery/mockups/digest.html`'s
    /// `.ck` card carries a provenance badge): a silent check's own provenance travels onto the
    /// wire alongside its population and zero count — projected straight from the registry entry's
    /// own <see cref="CheckRegistryEntry.Provenance"/>, the same fixed-per-check fact
    /// <c>CheckRegistryTests</c> documents, never derived or guessed here.</summary>
    [Fact]
    public void A_clean_entry_carries_the_provenance_the_check_would_have_produced()
    {
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "hook-failure",
                    Status = CheckRunStatus.Ran,
                    Population = 35,
                    FindingCount = 0,
                    Provenance = Provenance.Observed,
                },
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        var entry = Assert.Single(silent);
        Assert.Equal(Provenance.Observed, entry.Provenance);
    }

    /// <summary>The badge's text label rides alongside the raw enum, the same
    /// "words are the distinguishing signal, not only styling" discipline FR-48 established for
    /// <c>FindingEnvelope.ProvenanceLabel</c> — reused verbatim here via
    /// <see cref="Findings.ProvenanceLabel.For"/> rather than a second wording table.</summary>
    [Fact]
    public void A_clean_entrys_provenance_label_matches_findings_own_fixed_wording()
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
            ],
        };

        var silent = SilentCheckEnvelope.From(registry);

        var entry = Assert.Single(silent);
        Assert.Equal(ProvenanceLabel.For(Provenance.Inferred), entry.ProvenanceLabel);
        Assert.Contains("hypothesis", entry.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);
    }
}
