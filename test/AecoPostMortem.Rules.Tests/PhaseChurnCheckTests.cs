namespace AecoPostMortem.Rules.Tests;

/// <summary>Scenarios 2, 3 and 4 of issue #29 (FR-19): a return to an earlier phase under the
/// corpus-derived ordering is counted, each finding carries the vocabulary and ordering that
/// produced it, and each finding states its own denominator — returns against that session's own
/// total declared intents, never a corpus-wide one (the edge case: a measured 104 returns across
/// 352 intents in the worst session would swamp every other session without per-session
/// normalisation).</summary>
public sealed class PhaseChurnCheckTests
{
    [Fact]
    public void A_return_to_an_earlier_phase_is_counted()
    {
        // Corpus-wide ordering: explore(0), implement(1), test(2).
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "explore", Sequence = 1 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 2 },
            new() { SessionId = "s1", Phase = "test", Sequence = 3 },
            new() { SessionId = "s1", Phase = "explore", Sequence = 4 }, // a return
        ];

        var findings = PhaseChurnCheck.Run(intents);

        var finding = Assert.Single(findings);
        Assert.Equal("s1", finding.SessionId);
        Assert.Equal(1, finding.Returns);
    }

    [Fact]
    public void Each_return_below_the_highest_phase_reached_so_far_is_counted_separately()
    {
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "explore", Sequence = 1 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 2 },
            new() { SessionId = "s1", Phase = "test", Sequence = 3 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 4 }, // return 1
            new() { SessionId = "s1", Phase = "explore", Sequence = 5 }, // return 2
        ];

        var finding = Assert.Single(PhaseChurnCheck.Run(intents));

        Assert.Equal(2, finding.Returns);
    }

    [Fact]
    public void A_monotonic_session_under_the_derived_ordering_has_no_returns()
    {
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "explore", Sequence = 1 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 2 },
            new() { SessionId = "s1", Phase = "test", Sequence = 3 },
        ];

        var finding = Assert.Single(PhaseChurnCheck.Run(intents));

        Assert.Equal(0, finding.Returns);
    }

    [Fact]
    public void Repeating_the_same_phase_in_place_is_not_a_return()
    {
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "explore", Sequence = 1 },
            new() { SessionId = "s1", Phase = "explore", Sequence = 2 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 3 },
        ];

        var finding = Assert.Single(PhaseChurnCheck.Run(intents));

        Assert.Equal(0, finding.Returns);
    }

    [Fact]
    public void The_finding_states_returns_against_total_declared_intents_for_that_session()
    {
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "explore", Sequence = 1 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 2 },
            new() { SessionId = "s1", Phase = "test", Sequence = 3 },
            new() { SessionId = "s1", Phase = "explore", Sequence = 4 },
        ];

        var finding = Assert.Single(PhaseChurnCheck.Run(intents));

        Assert.Equal(1, finding.Returns);
        Assert.Equal(4, finding.TotalIntents);
    }

    [Fact]
    public void Each_sessions_denominator_is_its_own_not_the_corpus_wide_total()
    {
        // A long session with one return must not be normalised against a short session's total,
        // and vice versa — each finding's denominator is that session's own intent count.
        DeclaredIntent[] longSession =
        [
            new() { SessionId = "long", Phase = "explore", Sequence = 1 },
            new() { SessionId = "long", Phase = "implement", Sequence = 2 },
            new() { SessionId = "long", Phase = "test", Sequence = 3 },
            new() { SessionId = "long", Phase = "explore", Sequence = 4 },
            new() { SessionId = "long", Phase = "implement", Sequence = 5 },
            new() { SessionId = "long", Phase = "test", Sequence = 6 },
        ];
        DeclaredIntent[] shortSession =
        [
            new() { SessionId = "short", Phase = "explore", Sequence = 7 },
            new() { SessionId = "short", Phase = "implement", Sequence = 8 },
        ];

        var findings = PhaseChurnCheck.Run(longSession.Concat(shortSession));

        var longFinding = findings.Single(finding => finding.SessionId == "long");
        var shortFinding = findings.Single(finding => finding.SessionId == "short");

        Assert.Equal(6, longFinding.TotalIntents);
        Assert.Equal(2, shortFinding.TotalIntents);
    }

    [Fact]
    public void The_finding_carries_the_vocabulary_and_ordering_used_to_produce_it()
    {
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "explore", Sequence = 1 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 2 },
            new() { SessionId = "s2", Phase = "test", Sequence = 3 },
        ];

        var findings = PhaseChurnCheck.Run(intents);

        Assert.All(
            findings,
            finding => Assert.Equal(["explore", "implement", "test"], finding.Vocabulary));
    }

    [Fact]
    public void Sessions_are_detected_independently_of_one_another()
    {
        DeclaredIntent[] intents =
        [
            new() { SessionId = "churns", Phase = "explore", Sequence = 1 },
            new() { SessionId = "churns", Phase = "implement", Sequence = 2 },
            new() { SessionId = "churns", Phase = "explore", Sequence = 3 },
            new() { SessionId = "clean", Phase = "explore", Sequence = 4 },
            new() { SessionId = "clean", Phase = "implement", Sequence = 5 },
        ];

        var findings = PhaseChurnCheck.Run(intents);

        Assert.Equal(1, findings.Single(finding => finding.SessionId == "churns").Returns);
        Assert.Equal(0, findings.Single(finding => finding.SessionId == "clean").Returns);
    }

    [Fact]
    public void A_session_declaring_no_intents_produces_no_finding_rather_than_a_zero()
    {
        DeclaredIntent[] intents =
            [new() { SessionId = "has-intents", Phase = "explore", Sequence = 1 }];

        var findings = PhaseChurnCheck.Run(intents);

        Assert.DoesNotContain(findings, finding => finding.SessionId == "no-intents");
        Assert.Single(findings);
    }

    [Fact]
    public void An_empty_corpus_produces_no_findings()
    {
        Assert.Empty(PhaseChurnCheck.Run([]));
    }

    [Fact]
    public void Detection_is_computed_fresh_from_the_intents_passed_in_not_cached()
    {
        DeclaredIntent[] firstCorpus =
            [new() { SessionId = "s1", Phase = "explore", Sequence = 1 }];
        DeclaredIntent[] secondCorpus =
            [new() { SessionId = "s2", Phase = "implement", Sequence = 1 }];

        var first = PhaseChurnCheck.Run(firstCorpus);
        var second = PhaseChurnCheck.Run(secondCorpus);

        Assert.Contains(first, finding => finding.SessionId == "s1");
        Assert.DoesNotContain(second, finding => finding.SessionId == "s1");
        Assert.Contains(second, finding => finding.SessionId == "s2");
    }
}
