using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Piece 3's second slice: orchestrates <c>AecoPostMortem.Rules.BannedToolCheck</c> into
/// <c>FindingClass.RuleAdherenceToolChoice</c> findings — a banned tool that was actually called,
/// resolved against a real <see cref="ToolInvocationShape"/> corpus. Session attribution comes from
/// <see cref="ToolCall"/> directly (the check-shape layer carries no session), mirroring
/// <c>RepeatedFileReadFindingCheck</c>'s own split.
/// </summary>
public sealed class BannedToolFindingTests
{
    static RuleShapeMatch BannedMatch(string ruleText, string namedTool) => new()
    {
        Statement = new RuleStatement { SourceFile = "CLAUDE.md", Text = ruleText },
        Kind = RuleShapeKind.ToolIsBanned,
        OperandAText = namedTool,
    };

    static ToolCall Call(string sessionId, string toolName) => new()
    {
        SessionId = sessionId,
        ToolCallId = $"tc-{Guid.NewGuid()}",
        ToolName = toolName,
        StartedAt = "2026-08-16T00:00:00Z",
        OwnerKind = OwnerKind.Main,
    };

    [Fact]
    public void A_banned_tool_actually_called_produces_a_rule_adherence_finding()
    {
        var matches = new[] { BannedMatch("Never use grep.", "grep") };
        ToolInvocationShape[] invocations = [new() { ToolName = "grep", HasPattern = true }];
        var toolCalls = new[] { Call("session-1", "grep") };

        var result = BannedToolFinding.Run(matches, invocations, toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.RuleAdherenceToolChoice, finding.Class);
        Assert.Equal(Provenance.Derived, finding.Provenance);
        Assert.Contains(finding.Evidence, item => item.Field == "named_tool" && item.Value == "grep");
        Assert.Contains(finding.Evidence, item => item.Field == "call_count" && item.Value == "1");
        Assert.Single(finding.Recurrence.Occurrences);
        Assert.Equal("session-1", finding.Recurrence.Occurrences[0].SessionId);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
        Assert.Equal(1, result.RegistryEntry.Population);
    }

    [Fact]
    public void A_banned_tool_never_called_produces_no_finding()
    {
        var matches = new[] { BannedMatch("Never use rg.", "rg") };
        ToolInvocationShape[] invocations = [new() { ToolName = "grep", HasPattern = true }];
        var toolCalls = new[] { Call("session-1", "grep") };

        var result = BannedToolFinding.Run(matches, invocations, toolCalls);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    [Fact]
    public void A_banned_tool_called_across_two_sessions_lists_both_as_occurrences()
    {
        var matches = new[] { BannedMatch("Never use grep.", "grep") };
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "grep", HasPattern = true },
            new() { ToolName = "grep", HasPattern = true },
        ];
        var toolCalls = new[] { Call("session-1", "grep"), Call("session-2", "grep") };

        var result = BannedToolFinding.Run(matches, invocations, toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(2, finding.Recurrence.Occurrences.Count);
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-1");
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-2");
    }

    [Fact]
    public void A_non_banned_shape_match_is_ignored()
    {
        var matches = new[]
        {
            new RuleShapeMatch
            {
                Statement = new RuleStatement { SourceFile = "CLAUDE.md", Text = "Prefer rg over grep." },
                Kind = RuleShapeKind.PreferAOverB,
                OperandAText = "rg",
                OperandBText = "grep",
            },
        };
        ToolInvocationShape[] invocations = [new() { ToolName = "grep", HasPattern = true }];
        var toolCalls = new[] { Call("session-1", "grep") };

        var result = BannedToolFinding.Run(matches, invocations, toolCalls);

        Assert.Empty(result.Findings);
    }
}
