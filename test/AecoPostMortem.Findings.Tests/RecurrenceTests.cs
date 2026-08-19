namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-57: a finding's identity is <c>(class, class-specific key)</c> and is version-independent. A
/// finding whose rule spans several rule-set versions is one finding, not several — the per-version
/// breakdown is carried as <see cref="Recurrence.Occurrences"/> on that one value.
/// </summary>
public sealed class RecurrenceTests
{
    [Fact]
    public void A_recurrence_carries_its_key_and_at_least_one_occurrence()
    {
        var recurrence = new Recurrence
        {
            Key = "prefer rg over grep",
            Occurrences =
            [
                new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v3" },
            ],
        };

        Assert.Equal("prefer rg over grep", recurrence.Key);
        Assert.Single(recurrence.Occurrences);
    }

    [Fact]
    public void A_finding_spanning_several_rule_set_versions_is_one_recurrence_with_several_occurrences()
    {
        var recurrence = new Recurrence
        {
            Key = "prefer rg over grep",
            Occurrences =
            [
                new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v2" },
                new RecurrenceOccurrence { SessionId = "session-2", RuleSetVersion = "v3" },
            ],
        };

        Assert.Equal(2, recurrence.Occurrences.Count);
        Assert.Equal("prefer rg over grep", recurrence.Key);
    }

    [Fact]
    public void An_occurrence_may_carry_no_rule_set_version()
    {
        var occurrence = new RecurrenceOccurrence { SessionId = "session-1" };

        Assert.Null(occurrence.RuleSetVersion);
    }
}
