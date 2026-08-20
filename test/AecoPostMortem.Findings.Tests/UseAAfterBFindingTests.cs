using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Orchestrates <see cref="UseAAfterBCheck"/> into <c>FindingClass.RuleAdherenceToolChoice</c>
/// findings: a later-tool call with no earlier prerequisite call anywhere before it in the same
/// session. Both operands resolve against the real <see cref="ToolInvocationShape"/> corpus the same
/// way <c>BannedToolFinding</c>'s single operand does, and ordering comes straight from
/// <see cref="ToolCall.StartedAt"/> — no new RAW parsing needed.
/// </summary>
public sealed class UseAAfterBFindingTests
{
    static RuleShapeMatch UseAAfterBMatch(string ruleText, string laterTool, string earlierTool) => new()
    {
        Statement = new RuleStatement { SourceFile = "CLAUDE.md", Text = ruleText },
        Kind = RuleShapeKind.UseAAfterB,
        OperandAText = laterTool,
        OperandBText = earlierTool,
    };

    static ToolCall Call(string sessionId, string toolCallId, string toolName, string startedAt) => new()
    {
        SessionId = sessionId,
        ToolCallId = toolCallId,
        ToolName = toolName,
        StartedAt = startedAt,
        OwnerKind = OwnerKind.Main,
    };

    static ToolInvocationShape Invocation(string toolName) => new() { ToolName = toolName };

    [Fact]
    public void A_later_call_with_no_earlier_prerequisite_produces_a_rule_adherence_finding()
    {
        var matches = new[] { UseAAfterBMatch("Use rg after glob.", "rg", "glob") };
        var invocations = new[] { Invocation("rg"), Invocation("glob") };
        var toolCalls = new[] { Call("session-1", "c1", "rg", "2026-08-16T00:00:00Z") };

        var result = UseAAfterBFinding.Run(matches, invocations, toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.RuleAdherenceToolChoice, finding.Class);
        Assert.Equal(Provenance.Derived, finding.Provenance);
        Assert.Contains(finding.Evidence, item => item.Field == "later_tool" && item.Value == "rg");
        Assert.Contains(finding.Evidence, item => item.Field == "earlier_tool" && item.Value == "glob");
        Assert.Contains(finding.Evidence, item => item.Field == "violation_count" && item.Value == "1");
        Assert.Single(finding.Recurrence.Occurrences);
        Assert.Equal("session-1", finding.Recurrence.Occurrences[0].SessionId);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
        Assert.Equal(1, result.RegistryEntry.Population);
    }

    [Fact]
    public void A_later_call_preceded_by_the_earlier_call_produces_no_finding()
    {
        var matches = new[] { UseAAfterBMatch("Use rg after glob.", "rg", "glob") };
        var invocations = new[] { Invocation("rg"), Invocation("glob") };
        var toolCalls = new[]
        {
            Call("session-1", "c1", "glob", "2026-08-16T00:00:00Z"),
            Call("session-1", "c2", "rg", "2026-08-16T00:00:01Z"),
        };

        var result = UseAAfterBFinding.Run(matches, invocations, toolCalls);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    [Fact]
    public void A_mention_whose_operand_never_resolves_produces_no_finding()
    {
        var matches = new[] { UseAAfterBMatch("Use rg after glob.", "rg", "glob") };
        var invocations = new[] { Invocation("rg") };
        var toolCalls = new[] { Call("session-1", "c1", "rg", "2026-08-16T00:00:00Z") };

        var result = UseAAfterBFinding.Run(matches, invocations, toolCalls);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_non_use_a_after_b_shape_match_is_ignored()
    {
        var matches = new[]
        {
            new RuleShapeMatch
            {
                Statement = new RuleStatement { SourceFile = "CLAUDE.md", Text = "Never use grep." },
                Kind = RuleShapeKind.ToolIsBanned,
                OperandAText = "grep",
            },
        };
        var invocations = new[] { Invocation("grep") };
        var toolCalls = new[] { Call("session-1", "c1", "grep", "2026-08-16T00:00:00Z") };

        var result = UseAAfterBFinding.Run(matches, invocations, toolCalls);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_violation_across_two_sessions_lists_both_as_occurrences()
    {
        var matches = new[] { UseAAfterBMatch("Use rg after glob.", "rg", "glob") };
        var invocations = new[] { Invocation("rg"), Invocation("glob") };
        var toolCalls = new[]
        {
            Call("session-1", "c1", "rg", "2026-08-16T00:00:00Z"),
            Call("session-2", "c2", "rg", "2026-08-16T00:00:00Z"),
        };

        var result = UseAAfterBFinding.Run(matches, invocations, toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(2, finding.Recurrence.Occurrences.Count);
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-1");
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-2");
    }
}
