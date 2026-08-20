using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Orchestration for issue #29 (FR-19): turns <see cref="PhaseChurnCheck"/>'s per-session results
/// into <see cref="FindingClass.Waste"/> findings plus a check-registry entry.
/// <see cref="PhaseChurnCheck"/> itself — vocabulary/ordering derivation, the return count, the
/// denominator — is covered in AecoPostMortem.Rules.Tests; this project's job is deciding which
/// results are worth surfacing (only sessions that actually churned, mirroring
/// <see cref="FailedToolCallsFinding"/>'s "only tools with recorded failures" filter) and shaping
/// the result into the fixed seven-field <see cref="Finding"/> contract (issue #23).
/// </summary>
public sealed class PhaseChurnFindingTests
{
    [Fact]
    public void Only_sessions_that_returned_to_an_earlier_phase_produce_a_finding()
    {
        DeclaredIntent[] intents =
        [
            Intent("clean", "explore", 1),
            Intent("clean", "implement", 2),
            Intent("churns", "explore", 3),
            Intent("churns", "implement", 4),
            Intent("churns", "explore", 5), // a return
        ];

        var result = PhaseChurnFinding.Run(intents);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.Waste, finding.Class);
        Assert.Contains(
            finding.Recurrence.Occurrences,
            occurrence => occurrence.SessionId == "churns");
    }

    [Fact]
    public void The_finding_is_derived_provenance_never_observed()
    {
        // FR-19: "a legitimate iteration is indistinguishable from drift on this data, so the
        // finding is Derived, and says so."
        DeclaredIntent[] intents =
        [
            Intent("s1", "explore", 1),
            Intent("s1", "implement", 2),
            Intent("s1", "explore", 3),
        ];

        var result = PhaseChurnFinding.Run(intents);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(Provenance.Derived, finding.Provenance);
    }

    [Fact]
    public void The_finding_states_returns_against_total_declared_intents_for_that_session()
    {
        DeclaredIntent[] intents =
        [
            Intent("s1", "explore", 1),
            Intent("s1", "implement", 2),
            Intent("s1", "test", 3),
            Intent("s1", "explore", 4),
        ];

        var result = PhaseChurnFinding.Run(intents);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "returns" && item.Value == "1");
        Assert.Contains(finding.Evidence, item => item.Field == "totalIntents" && item.Value == "4");
    }

    [Fact]
    public void The_finding_carries_the_vocabulary_and_ordering_used_to_produce_it()
    {
        DeclaredIntent[] intents =
        [
            Intent("s1", "explore", 1),
            Intent("s1", "implement", 2),
            Intent("s1", "test", 3),
            Intent("s1", "explore", 4),
        ];

        var result = PhaseChurnFinding.Run(intents);

        var finding = Assert.Single(result.Findings);
        var vocabulary = finding.Evidence
            .Where(item => item.Field == "vocabulary")
            .Select(item => item.Value)
            .ToArray();

        Assert.Equal(["explore", "implement", "test"], vocabulary);
    }

    [Fact]
    public void Each_sessions_denominator_is_its_own_not_a_corpus_wide_total()
    {
        var longSession = new[]
        {
            Intent("long", "explore", 1),
            Intent("long", "implement", 2),
            Intent("long", "test", 3),
            Intent("long", "explore", 4),
            Intent("long", "implement", 5),
            Intent("long", "test", 6),
        };
        var shortSession = new[] { Intent("short", "explore", 7), Intent("short", "implement", 8) };
        var churningShort = new[]
        {
            Intent("short", "explore", 7),
            Intent("short", "implement", 8),
            Intent("short", "explore", 9),
        };

        var result = PhaseChurnFinding.Run(longSession.Concat(churningShort).ToArray());

        var shortFinding = result.Findings.Single(finding =>
            finding.Recurrence.Occurrences.Any(o => o.SessionId == "short"));

        Assert.Contains(shortFinding.Evidence, item => item.Field == "totalIntents" && item.Value == "3");
    }

    [Fact]
    public void The_recurrence_key_is_the_session_id()
    {
        // Phase churn has no shared sub-object (path, hook, tool) to recur around the way the
        // other Waste checks do — it is a whole-session aggregate, so each session's own churn is
        // its own finding, keyed by that session's id.
        DeclaredIntent[] intents = [Intent("s1", "explore", 1), Intent("s1", "implement", 2), Intent("s1", "explore", 3)];

        var result = PhaseChurnFinding.Run(intents);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("s1", finding.Recurrence.Key);
        var occurrence = Assert.Single(finding.Recurrence.Occurrences);
        Assert.Equal("s1", occurrence.SessionId);
    }

    [Fact]
    public void The_finding_carries_no_resolution_because_it_is_not_an_adherence_figure()
    {
        DeclaredIntent[] intents = [Intent("s1", "explore", 1), Intent("s1", "implement", 2), Intent("s1", "explore", 3)];

        var result = PhaseChurnFinding.Run(intents);

        var finding = Assert.Single(result.Findings);
        Assert.Null(finding.Resolution);
    }

    [Fact]
    public void A_session_with_no_returns_produces_no_finding()
    {
        DeclaredIntent[] intents = [Intent("s1", "explore", 1), Intent("s1", "implement", 2)];

        var result = PhaseChurnFinding.Run(intents);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_session_declaring_no_intents_produces_no_finding_rather_than_a_zero()
    {
        // Nothing supplies a "no-intents" session as input at all — grouping is over the intents
        // themselves, so there is nothing this check could even enumerate for it.
        DeclaredIntent[] intents = [Intent("has-intents", "explore", 1)];

        var result = PhaseChurnFinding.Run(intents);

        Assert.Empty(result.Findings);
        Assert.DoesNotContain(
            result.Findings,
            finding => finding.Recurrence.Occurrences.Any(o => o.SessionId == "no-intents"));
    }

    [Fact]
    public void The_check_registers_with_its_session_population_and_finding_count()
    {
        DeclaredIntent[] intents =
        [
            Intent("clean", "explore", 1),
            Intent("clean", "implement", 2),
            Intent("churns", "explore", 3),
            Intent("churns", "implement", 4),
            Intent("churns", "explore", 5),
        ];

        var result = PhaseChurnFinding.Run(intents);

        Assert.Equal(PhaseChurnFinding.CheckId, result.RegistryEntry.CheckId);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(2, result.RegistryEntry.Population);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
        Assert.Equal(Provenance.Derived, result.RegistryEntry.Provenance);
    }

    [Fact]
    public void No_intents_still_registers_a_clean_run_not_a_refusal()
    {
        var result = PhaseChurnFinding.Run([]);

        Assert.Empty(result.Findings);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(0, result.RegistryEntry.Population);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    static DeclaredIntent Intent(string sessionId, string phase, long sequence) =>
        new() { SessionId = sessionId, Phase = phase, Sequence = sequence };
}
