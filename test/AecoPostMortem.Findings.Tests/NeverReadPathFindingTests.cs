using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Piece 3's third slice: orchestrates <c>AecoPostMortem.Rules.NeverReadPathCheck</c> into
/// <c>FindingClass.RuleAdherenceToolChoice</c> findings — a banned path that a real tool call
/// actually touched. Unlike <c>BannedToolFinding</c>, no <see cref="ToolInvocationShape"/> corpus is
/// needed: <see cref="ReadEvent"/>s are built straight from <see cref="ToolCall.Path"/>, regardless
/// of tool name, since <c>NeverReadPath</c>'s own grammar covers read/open/access/modify/edit/list —
/// broader than the narrow "view only" mapping <c>RepeatedFileReadFindingCheck</c> uses for its own,
/// different question.
/// </summary>
public sealed class NeverReadPathFindingTests
{
    static RuleShapeMatch NeverReadPathMatch(string ruleText, string namedPath) => new()
    {
        Statement = new RuleStatement { SourceFile = "CLAUDE.md", Text = ruleText },
        Kind = RuleShapeKind.NeverReadPath,
        OperandAText = namedPath,
    };

    static ToolCall Call(string sessionId, string toolName, string? path) => new()
    {
        SessionId = sessionId,
        ToolCallId = $"tc-{Guid.NewGuid()}",
        ToolName = toolName,
        StartedAt = "2026-08-16T00:00:00Z",
        OwnerKind = OwnerKind.Main,
        Path = path,
    };

    [Fact]
    public void A_banned_path_actually_touched_produces_a_rule_adherence_finding()
    {
        var matches = new[] { NeverReadPathMatch("Never read `src/Secrets/`.", "src/Secrets/") };
        var toolCalls = new[] { Call("session-1", "view", @"F:\git\AecoPostMortem\src\Secrets\key.txt") };

        var result = NeverReadPathFinding.Run(matches, toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.RuleAdherenceToolChoice, finding.Class);
        Assert.Equal(Provenance.Derived, finding.Provenance);
        Assert.Contains(finding.Evidence, item => item.Field == "named_path" && item.Value == "src/Secrets/");
        Assert.Contains(finding.Evidence, item => item.Field == "access_count" && item.Value == "1");
        Assert.Single(finding.Recurrence.Occurrences);
        Assert.Equal("session-1", finding.Recurrence.Occurrences[0].SessionId);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
        Assert.Equal(1, result.RegistryEntry.Population);
    }

    [Fact]
    public void A_banned_path_never_touched_produces_no_finding()
    {
        var matches = new[] { NeverReadPathMatch("Never read `src/Secrets/`.", "src/Secrets/") };
        var toolCalls = new[] { Call("session-1", "view", @"F:\git\AecoPostMortem\src\Public\readme.md") };

        var result = NeverReadPathFinding.Run(matches, toolCalls);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    [Fact]
    public void A_call_with_no_path_is_never_matched()
    {
        var matches = new[] { NeverReadPathMatch("Never read `src/Secrets/`.", "src/Secrets/") };
        var toolCalls = new[] { Call("session-1", "run_shell", path: null) };

        var result = NeverReadPathFinding.Run(matches, toolCalls);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_non_read_tool_call_still_counts_because_the_shape_covers_more_than_reading()
    {
        // NeverReadPath's own grammar covers open/access/modify/edit/list, not only "read" — a
        // write-shaped call touching the banned path is still a violation.
        var matches = new[] { NeverReadPathMatch("Never edit `src/Secrets/`.", "src/Secrets/") };
        var toolCalls = new[] { Call("session-1", "edit", @"F:\git\AecoPostMortem\src\Secrets\key.txt") };

        var result = NeverReadPathFinding.Run(matches, toolCalls);

        Assert.Single(result.Findings);
    }

    [Fact]
    public void A_banned_path_touched_across_two_sessions_lists_both_as_occurrences()
    {
        var matches = new[] { NeverReadPathMatch("Never read `src/Secrets/`.", "src/Secrets/") };
        var toolCalls = new[]
        {
            Call("session-1", "view", @"F:\git\AecoPostMortem\src\Secrets\key.txt"),
            Call("session-2", "view", @"F:\git\AecoPostMortem\src\Secrets\other.txt"),
        };

        var result = NeverReadPathFinding.Run(matches, toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(2, finding.Recurrence.Occurrences.Count);
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-1");
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-2");
    }

    [Fact]
    public void A_non_never_read_path_shape_match_is_ignored()
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
        var toolCalls = new[] { Call("session-1", "grep", null) };

        var result = NeverReadPathFinding.Run(matches, toolCalls);

        Assert.Empty(result.Findings);
    }
}
