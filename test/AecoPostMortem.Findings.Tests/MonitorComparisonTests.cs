using System.Reflection;
using System.Runtime.CompilerServices;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-39 (S-35, issue #43): the Monitor comparison — adherence for the same rule, computed under one
/// shared resolution, on either side of an adjacent rule-set-version edit. The reference edit
/// measured 41.8% before against 71.7% after on a measured 3 and 4 sessions (PRD discovery finding
/// 4) — that demonstrates the method, not a conclusion about the edit, so this suite also proves the
/// comparison reproduces exactly those figures without needing new sessions (Scenario 4).
/// </summary>
public sealed class MonitorComparisonTests
{
    static RuleSetVersion Version(string hash, string firstSessionId, int sessionCount) => new()
    {
        Id = new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = hash },
        FirstSessionId = firstSessionId,
        LastSessionId = firstSessionId,
        SessionCount = sessionCount,
    };

    static ToolInvocationShape[] Calls(string toolName, int count) =>
        Enumerable.Repeat(new ToolInvocationShape { ToolName = toolName, HasPattern = true }, count).ToArray();

    [Fact]
    public void Two_adjacent_versions_are_compared_under_one_shared_resolution()
    {
        RuleSetVersion[] versions =
        [
            Version("hash-1", "s1", 3),
            Version("hash-2", "s4", 4),
        ];

        var beforeCalls = Calls("rg", 4).Concat(Calls("grep", 6)).ToArray();
        var afterCalls = Calls("rg", 9).Concat(Calls("grep", 1)).ToArray();

        var comparison = MonitorComparison.Compare(
            versions,
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-1" },
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-2" },
            operandAText: "rg",
            operandBText: "grep",
            beforeInvocations: beforeCalls,
            afterInvocations: afterCalls);

        // One stated resolution: both sides resolved the same operand text through the same layer.
        Assert.Equal(comparison.Before.Adherent.Layer, comparison.After.Adherent.Layer);
        Assert.Equal(comparison.Before.Adherent.OperandText, comparison.After.Adherent.OperandText);
        Assert.Equal(OperandResolutionLayer.ExactToolName, comparison.Before.Adherent.Layer);

        // Only the call counts differ between sides.
        Assert.Equal(4, comparison.Before.AdherentCalls);
        Assert.Equal(9, comparison.After.AdherentCalls);
    }

    [Fact]
    public void Sample_sizes_are_carried_on_each_side_alongside_the_percentage()
    {
        RuleSetVersion[] versions =
        [
            Version("hash-1", "s1", 3),
            Version("hash-2", "s4", 4),
        ];

        var comparison = MonitorComparison.Compare(
            versions,
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-1" },
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-2" },
            operandAText: "rg",
            operandBText: "grep",
            beforeInvocations: Calls("rg", 1),
            afterInvocations: Calls("rg", 1));

        Assert.Equal(3, comparison.BeforeVersion.SessionCount);
        Assert.Equal(4, comparison.AfterVersion.SessionCount);
    }

    [Theory]
    [InlineData(nameof(MonitorComparison.BeforeVersion))]
    [InlineData(nameof(MonitorComparison.AfterVersion))]
    [InlineData(nameof(MonitorComparison.Before))]
    [InlineData(nameof(MonitorComparison.After))]
    public void Every_member_is_required_so_a_comparison_cannot_be_served_half_populated(string propertyName)
    {
        var property = typeof(MonitorComparison).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void Non_adjacent_versions_are_refused_naming_the_intervening_version()
    {
        RuleSetVersion[] versions =
        [
            Version("hash-1", "s1", 3),
            Version("hash-2", "s2", 5),
            Version("hash-3", "s3", 4),
        ];

        var exception = Assert.Throws<NonAdjacentRuleSetVersionsException>(() =>
            MonitorComparison.Compare(
                versions,
                new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-1" },
                new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-3" },
                operandAText: "rg",
                operandBText: "grep",
                beforeInvocations: Calls("rg", 1),
                afterInvocations: Calls("rg", 1)));

        var intervening = Assert.Single(exception.Intervening);
        Assert.Equal("hash-2", intervening.Hash);
    }

    /// <summary>Scenario 4: the reference corpus's own 2026-05-23 edit — 3 sessions measuring 41.8%,
    /// 4 sessions measuring 71.7% (PRD discovery finding 4, FR-39). The corpus's raw session bytes
    /// are not committed (`fixtures/README.md` — they carry the operator's own prompts and patches),
    /// so this fixture is the same call-count shape that produced those two published percentages,
    /// not a replay of the original session files.</summary>
    [Fact]
    public void The_reference_corpus_reproduces_the_measured_41_8_to_71_7_percent_edit()
    {
        RuleSetVersion[] versions =
        [
            Version("1a47450a", "s-2026-05-22", 3),
            Version("9579a981", "s-2026-05-23", 4),
        ];

        // 23 of 55 calls preferred rg -> 41.818...% (rounds to the published 41.8%).
        var beforeCalls = Calls("rg", 23).Concat(Calls("grep", 32)).ToArray();

        // 76 of 106 calls preferred rg -> 71.698...% (rounds to the published 71.7%).
        var afterCalls = Calls("rg", 76).Concat(Calls("grep", 30)).ToArray();

        var comparison = MonitorComparison.Compare(
            versions,
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "1a47450a" },
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "9579a981" },
            operandAText: "rg",
            operandBText: "grep",
            beforeInvocations: beforeCalls,
            afterInvocations: afterCalls);

        Assert.Equal(41.8, Math.Round(comparison.Before.Percentage!.Value, 1));
        Assert.Equal(3, comparison.BeforeVersion.SessionCount);

        Assert.Equal(71.7, Math.Round(comparison.After.Percentage!.Value, 1));
        Assert.Equal(4, comparison.AfterVersion.SessionCount);
    }
}
