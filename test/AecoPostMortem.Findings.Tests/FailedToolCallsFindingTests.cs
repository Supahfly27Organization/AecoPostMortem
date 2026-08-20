using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Orchestration for issue #26 (S-14, FR-16): turns <see cref="FailedToolCallsCheck"/>'s per-tool
/// rates into <see cref="FindingClass.Waste"/> findings plus a check-registry entry.
/// <see cref="FailedToolCallsCheck"/> itself — grouping, the rate, the session count — is covered
/// in AecoPostMortem.Rules.Tests; this project's job is deciding which rates are worth surfacing
/// and shaping the result into the fixed seven-field <see cref="Finding"/> contract (issue #23).
/// </summary>
public sealed class FailedToolCallsFindingTests
{
    [Fact]
    public void Only_tools_with_recorded_failures_produce_a_finding()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "clean-tool", succeeded: true),
            Outcome("session-1", "clean-tool", succeeded: true),
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: true),
        };

        var result = FailedToolCallsFinding.Run(outcomes);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.Waste, finding.Class);
        Assert.Contains(finding.Evidence, item => item.Field == "toolIdentity" && item.Value == "flaky-tool");
    }

    [Fact]
    public void A_rate_never_appears_without_its_counts()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: true),
            Outcome("session-2", "flaky-tool", succeeded: true),
        };

        var result = FailedToolCallsFinding.Run(outcomes);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "failures" && item.Value == "2");
        Assert.Contains(finding.Evidence, item => item.Field == "calls" && item.Value == "4");
        Assert.Contains(finding.Evidence, item => item.Field == "percentage" && item.Value == "50");
        // The headline's own session count is the narrower "sessions that actually failed" figure
        // (1: only session-1), not the wider `sessionCount` evidence item above (2: every session
        // that called the tool at all, session-2's clean call included) — the same count that feeds
        // `Recurrence.Occurrences` and therefore the row's own "sessions affected" badge, so the two
        // numbers on one digest row can never disagree (caught in code review).
        Assert.Equal("flaky-tool failed 2 of 4 calls (50%) across 1 session.", finding.Headline);
    }

    /// <summary>The edge case named in issue #26: a measured 61.2% failure rate on a tool used in
    /// only 4 sessions is exactly the case where a bare percentage misleads — the session count
    /// must always accompany the rate, never be omittable.</summary>
    [Fact]
    public void A_high_rate_on_a_few_sessions_carries_the_session_count_in_the_finding()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-2", "flaky-tool", succeeded: false),
            Outcome("session-3", "flaky-tool", succeeded: true),
            Outcome("session-4", "flaky-tool", succeeded: true),
        };

        var result = FailedToolCallsFinding.Run(outcomes);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "sessionCount" && item.Value == "4");
        Assert.Contains(finding.Evidence, item => item.Field == "failures" && item.Value == "4");
        Assert.Contains(finding.Evidence, item => item.Field == "calls" && item.Value == "6");
    }

    [Fact]
    public void The_recurrence_key_is_the_tool_identity_the_operand_carried()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-tool", succeeded: false) };

        var result = FailedToolCallsFinding.Run(outcomes);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("flaky-tool", finding.Recurrence.Key);
        Assert.Contains(finding.Recurrence.Occurrences, occurrence => occurrence.SessionId == "session-1");
    }

    [Fact]
    public void The_finding_carries_no_resolution_because_it_is_not_an_adherence_figure()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-tool", succeeded: false) };

        var result = FailedToolCallsFinding.Run(outcomes);

        var finding = Assert.Single(result.Findings);
        Assert.Null(finding.Resolution);
    }

    [Fact]
    public void The_check_registers_with_its_population_and_finding_count()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "clean-tool", succeeded: true),
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: true),
        };

        var result = FailedToolCallsFinding.Run(outcomes);

        Assert.Equal(FailedToolCallsFinding.CheckId, result.RegistryEntry.CheckId);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(3, result.RegistryEntry.Population);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
    }

    [Fact]
    public void No_outcomes_still_registers_a_clean_run_not_a_refusal()
    {
        var result = FailedToolCallsFinding.Run([]);

        Assert.Empty(result.Findings);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(0, result.RegistryEntry.Population);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    static ToolCallOutcome Outcome(string sessionId, string toolIdentity, bool succeeded) =>
        new()
        {
            SessionId = sessionId,
            ToolIdentity = toolIdentity,
            Succeeded = succeeded,
        };
}
