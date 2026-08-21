using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-17 (issue #27): orchestration over <c>AecoPostMortem.Rules.HookFailureCheck</c> — turning
/// the corpus's session population and its failed hook events into a <c>Finding</c> whose
/// suggestion states both denominators together, whose evidence quotes the failed event's own
/// fields, and a <c>CheckRegistryEntry</c> that reports clean once the hook stops failing.
/// </summary>
public sealed class HookFailureFindingTests
{
    static readonly IReadOnlyList<string> MeasuredSessions =
        [.. Enumerable.Range(1, 33).Select(i => $"tool-call-{i}"), "no-tool-call-1", "no-tool-call-2"];

    static readonly IReadOnlySet<string> MeasuredSessionsWithToolCall =
        Enumerable.Range(1, 33).Select(i => $"tool-call-{i}").ToHashSet();

    [Fact]
    public void Both_denominators_are_stated_together_and_neither_appears_alone()
    {
        var failures = BuildMeasuredFailures();

        var (findings, registry) = HookFailureFinding.Build(MeasuredSessions, MeasuredSessionsWithToolCall, failures);

        var finding = Assert.Single(findings);

        // The only way to reach the rendered text is through BuildSuggestion(HookFailureCounts) —
        // there is no code path that renders one figure without the other.
        Assert.Contains("34 of 35", finding.Suggestion!.Text);
        Assert.Contains("32 of the 33", finding.Suggestion!.Text);
        Assert.Equal("The pre-commit-guard hook failed in 34 of 35 sessions.", finding.Headline);
        Assert.Equal(35, registry.Population);
        Assert.Equal(1, registry.FindingCount);
    }

    [Fact]
    public void The_evidence_quotes_the_hook_events_success_flag_and_error_text()
    {
        var failures = new[]
        {
            new HookFailureEvent
            {
                SessionId = "s1",
                HookName = "pre-commit-guard",
                Success = false,
                Error = "spawn ENOENT: guard.sh not found",
            },
        };

        var (findings, _) = HookFailureFinding.Build(["s1"], new HashSet<string> { "s1" }, failures);

        var finding = Assert.Single(findings);

        Assert.Contains(finding.Evidence, item => item.Field == "data.success" && item.Value == "false");
        Assert.Contains(
            finding.Evidence,
            item => item.Field == "data.error" && item.Value == "spawn ENOENT: guard.sh not found");
    }

    [Fact]
    public void The_recurrence_key_is_the_hook_identity()
    {
        var failures = new[]
        {
            new HookFailureEvent { SessionId = "s1", HookName = "pre-commit-guard", Success = false, Error = "boom" },
            new HookFailureEvent { SessionId = "s2", HookName = "pre-commit-guard", Success = false, Error = "boom" },
        };

        var (findings, _) = HookFailureFinding.Build(
            ["s1", "s2"],
            new HashSet<string> { "s1", "s2" },
            failures);

        var finding = Assert.Single(findings);

        Assert.Equal("pre-commit-guard", finding.Recurrence.Key);
        Assert.Equal(2, finding.Recurrence.Occurrences.Count);
        Assert.Equal(FindingClass.Waste, finding.Class);
    }

    /// <summary>Distinct hook identities produce distinct findings, each carrying the same
    /// corpus-wide denominators.</summary>
    [Fact]
    public void Distinct_hook_identities_produce_distinct_findings()
    {
        var failures = new[]
        {
            new HookFailureEvent { SessionId = "s1", HookName = "pre-commit-guard", Success = false, Error = "a" },
            new HookFailureEvent { SessionId = "s2", HookName = "post-write-lint", Success = false, Error = "b" },
        };

        var (findings, registry) = HookFailureFinding.Build(
            ["s1", "s2"],
            new HashSet<string> { "s1", "s2" },
            failures);

        Assert.Equal(2, findings.Count);
        Assert.Equal(2, registry.FindingCount);
        Assert.Contains(findings, f => f.Recurrence.Key == "pre-commit-guard");
        Assert.Contains(findings, f => f.Recurrence.Key == "post-write-lint");
    }

    /// <summary>The edge case named in issue #27: this finding is expected to disappear from the
    /// digest on its own once the operator fixes the hook — a clean check, not a refused one.</summary>
    [Fact]
    public void No_failures_produces_no_findings_and_a_clean_registry_entry()
    {
        var (findings, registry) = HookFailureFinding.Build(["s1", "s2"], new HashSet<string> { "s1" }, []);

        Assert.Empty(findings);
        Assert.Equal(CheckRunStatus.Ran, registry.Status);
        Assert.Equal(2, registry.Population);
        Assert.Equal(0, registry.FindingCount);
        Assert.Equal(Provenance.Observed, registry.Provenance);
    }

    [Fact]
    public void Every_finding_carries_observed_provenance()
    {
        var failures = new[]
        {
            new HookFailureEvent { SessionId = "s1", HookName = "pre-commit-guard", Success = false, Error = "a" },
        };

        var (findings, _) = HookFailureFinding.Build(["s1"], new HashSet<string> { "s1" }, failures);

        Assert.All(findings, finding => Assert.Equal(Provenance.Observed, finding.Provenance));
    }

    static HookFailureEvent[] BuildMeasuredFailures()
    {
        var toolCallFailures = Enumerable.Range(1, 32)
            .Select(i => new HookFailureEvent
            {
                SessionId = $"tool-call-{i}",
                HookName = "pre-commit-guard",
                Success = false,
                Error = "spawn ENOENT: guard.sh not found",
            });

        var noToolCallFailures = new[]
        {
            new HookFailureEvent
            {
                SessionId = "no-tool-call-1",
                HookName = "pre-commit-guard",
                Success = false,
                Error = "spawn ENOENT: guard.sh not found",
            },
            new HookFailureEvent
            {
                SessionId = "no-tool-call-2",
                HookName = "pre-commit-guard",
                Success = false,
                Error = "spawn ENOENT: guard.sh not found",
            },
        };

        return [.. toolCallFailures, .. noToolCallFailures];
    }
}
