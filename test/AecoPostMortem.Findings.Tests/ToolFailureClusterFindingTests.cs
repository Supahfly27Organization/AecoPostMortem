using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-46 (S-40, issue #51): reuses S-14's <see cref="FailedToolCallsCheck"/> rate computation — this
/// project does not recompute failure rates from scratch — and turns each tool's rate into a
/// <see cref="FindingClass.MissingCapability"/> cluster, cross-referenced to the adherence finding
/// for any rule that mandates the same tool.
/// </summary>
public sealed class ToolFailureClusterFindingTests
{
    [Fact]
    public void Only_tools_with_recorded_failures_produce_a_cluster()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "clean-tool", succeeded: true),
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: true),
        };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.MissingCapability, finding.Class);
        Assert.Contains(finding.Evidence, item => item.Field == "toolIdentity" && item.Value == "flaky-tool");
    }

    /// <summary>Scenario 1 (issue #51): "each is reported with failures over calls and its rate."</summary>
    [Fact]
    public void A_cluster_reports_its_failures_calls_and_rate()
    {
        var outcomes = new[]
        {
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: false),
            Outcome("session-1", "flaky-tool", succeeded: true),
            Outcome("session-2", "flaky-tool", succeeded: true),
        };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "failures" && item.Value == "2");
        Assert.Contains(finding.Evidence, item => item.Field == "calls" && item.Value == "4");
        Assert.Contains(finding.Evidence, item => item.Field == "percentage" && item.Value == "50");
        Assert.Contains(finding.Evidence, item => item.Field == "sessionCount" && item.Value == "2");
        Assert.Equal(
            "flaky-tool fails 2 of 4 calls (50%) — possibly a missing capability, not a rule violation.",
            finding.Headline);
    }

    /// <summary>FR-46: "match tool names exactly, and state the convention on the table" — a
    /// failure-rate table without its matching convention is the same defect FR-33 names for an
    /// adherence figure without its resolution.</summary>
    [Fact]
    public void The_matching_convention_is_stated_on_every_cluster()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-tool", succeeded: false) };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "matchConvention" && item.Value == "exact");
    }

    /// <summary>Scenario 2 (issue #51): a tool a rule mandates that also fails often links to the
    /// adherence finding for that rule, labelled as a hypothesis. The link target is the pair a
    /// <c>RuleAdherenceToolChoice</c> finding is identified by (FR-57: class + recurrence key, which
    /// <see cref="FindingClassRegistry"/> declares as "the rule statement" for that class) — so the
    /// evidence quotes exactly what a caller needs to look that finding up.</summary>
    [Fact]
    public void A_cluster_for_a_mandated_tool_links_to_its_adherence_finding_labelled_as_a_hypothesis()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-mcp-tool", succeeded: false) };
        var rule = new RuleStatement { SourceFile = "CLAUDE.md", Text = "Always use flaky-mcp-tool for X." };
        var mandatedTools = new[] { new MandatedTool { ToolIdentity = "flaky-mcp-tool", Rule = rule } };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "mandatingRuleSourceFile" && item.Value == "CLAUDE.md");
        Assert.Contains(finding.Evidence, item => item.Field == "mandatingRuleText" && item.Value == rule.Text);
        Assert.Contains(finding.Evidence, item => item.Field == "mandatingRuleLinkKind" && item.Value == "hypothesis");
    }

    /// <summary>A tool no rule mandates carries no link at all — the cross-reference is not a field
    /// forced to a null/empty state, it is simply absent (the same "absence in, absence out"
    /// discipline <c>SilentCheckEnvelope.From</c> documents).</summary>
    [Fact]
    public void A_cluster_for_a_tool_no_rule_mandates_carries_no_link()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-tool", succeeded: false) };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        var finding = Assert.Single(result.Findings);
        Assert.DoesNotContain(finding.Evidence, item => item.Field == "mandatingRuleSourceFile");
        Assert.DoesNotContain(finding.Evidence, item => item.Field == "mandatingRuleText");
        Assert.DoesNotContain(finding.Evidence, item => item.Field == "mandatingRuleLinkKind");
    }

    /// <summary>A mandated tool with a clean record produces no cluster at all — mirroring
    /// <see cref="Only_tools_with_recorded_failures_produce_a_cluster"/>: being mandated is not
    /// itself grounds for a finding, only a recorded failure is.</summary>
    [Fact]
    public void A_mandated_tool_with_no_failures_produces_no_cluster()
    {
        var outcomes = new[] { Outcome("session-1", "reliable-tool", succeeded: true) };
        var rule = new RuleStatement { SourceFile = "CLAUDE.md", Text = "Always use reliable-tool for X." };
        var mandatedTools = new[] { new MandatedTool { ToolIdentity = "reliable-tool", Rule = rule } };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Every_cluster_carries_inferred_provenance()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-tool", succeeded: false) };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(Provenance.Inferred, finding.Provenance);
    }

    [Fact]
    public void The_finding_carries_no_resolution_because_it_is_not_an_adherence_figure()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-tool", succeeded: false) };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        var finding = Assert.Single(result.Findings);
        Assert.Null(finding.Resolution);
    }

    [Fact]
    public void The_recurrence_key_is_the_tool_identity_the_operand_carried()
    {
        var outcomes = new[] { Outcome("session-1", "flaky-tool", succeeded: false) };

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("flaky-tool", finding.Recurrence.Key);
        Assert.Contains(finding.Recurrence.Occurrences, occurrence => occurrence.SessionId == "session-1");
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

        var result = ToolFailureClusterFinding.Run(outcomes, mandatedTools: []);

        Assert.Equal(ToolFailureClusterFinding.CheckId, result.RegistryEntry.CheckId);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(3, result.RegistryEntry.Population);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
        Assert.Equal(Provenance.Inferred, result.RegistryEntry.Provenance);
    }

    [Fact]
    public void No_outcomes_still_registers_a_clean_run_not_a_refusal()
    {
        var result = ToolFailureClusterFinding.Run([], mandatedTools: []);

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
