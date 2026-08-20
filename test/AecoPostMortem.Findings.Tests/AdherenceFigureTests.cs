using System.Reflection;
using System.Runtime.CompilerServices;
using AecoPostMortem.Findings;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-33 (S-24, issue #38): an adherence figure and the resolution that produced it are one type,
/// not a percentage a caller may or may not pair with its provenance. The guard is the measured
/// fivefold spread on one rule that came from the resolution choice alone, so the percentage is a
/// computed property over the per-operand call counts — there is no member to set it independently
/// of the operands, and therefore no construction path that yields a bare figure.
/// </summary>
public sealed class AdherenceFigureTests
{
    static readonly RuleSetVersionId SampleVersion = new()
    {
        Repository = "AecoPostMortem",
        Hash = "b3f1c0",
    };

    static AdherenceFigure PreferAOverB(int adherentCalls, int divergentCalls) => new()
    {
        RuleVersion = SampleVersion,
        Adherent = new OperandResolution
        {
            OperandText = "rg",
            Layer = OperandResolutionLayer.ExactToolName,
            CallCount = adherentCalls,
        },
        Divergent =
        [
            new OperandResolution
            {
                OperandText = "Shell",
                Layer = OperandResolutionLayer.DerivedRole,
                CallCount = divergentCalls,
            },
        ],
    };

    [Fact]
    public void The_percentage_is_computed_from_the_per_operand_call_counts()
    {
        var figure = PreferAOverB(adherentCalls: 3, divergentCalls: 1);

        Assert.Equal(3, figure.AdherentCalls);
        Assert.Equal(4, figure.TotalCalls);
        Assert.Equal(75d, figure.Percentage);
    }

    /// <summary>The structural half of Scenario 2 ("a figure without its resolution ... cannot be
    /// returned"): mirrors <c>FailedToolCallsCheckTests.The_percentage_is_computed_never_a_settable_
    /// member</c>. A settable percentage would be exactly the bare figure FR-33 forbids, because it
    /// could then disagree with — or exist without — the operands that produced it.</summary>
    [Fact]
    public void The_percentage_is_never_a_settable_member()
    {
        var property = typeof(AdherenceFigure).GetProperty(nameof(AdherenceFigure.Percentage));

        Assert.NotNull(property);
        Assert.Null(property!.SetMethod);
    }

    [Theory]
    [InlineData(nameof(AdherenceFigure.RuleVersion))]
    [InlineData(nameof(AdherenceFigure.Adherent))]
    [InlineData(nameof(AdherenceFigure.Divergent))]
    public void The_rule_version_and_both_operand_sides_are_required_members(string propertyName)
    {
        var property = typeof(AdherenceFigure).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    /// <summary>Scenario 1: "the layer used per operand and the resulting call counts are shown with
    /// it." <c>Operands</c> is the whole set, adherent side first — computed, so it can neither be
    /// omitted nor disagree with the two members it is derived from.</summary>
    [Fact]
    public void Operands_states_every_operands_own_layer_and_call_count_adherent_side_first()
    {
        var figure = PreferAOverB(adherentCalls: 3, divergentCalls: 1);

        Assert.Collection(
            figure.Operands,
            operand =>
            {
                Assert.Equal("rg", operand.OperandText);
                Assert.Equal(OperandResolutionLayer.ExactToolName, operand.Layer);
                Assert.Equal(3, operand.CallCount);
            },
            operand =>
            {
                Assert.Equal("Shell", operand.OperandText);
                Assert.Equal(OperandResolutionLayer.DerivedRole, operand.Layer);
                Assert.Equal(1, operand.CallCount);
            });
    }

    [Fact]
    public void The_operand_list_is_never_a_settable_member()
    {
        var property = typeof(AdherenceFigure).GetProperty(nameof(AdherenceFigure.Operands));

        Assert.NotNull(property);
        Assert.Null(property!.SetMethod);
    }

    [Theory]
    [InlineData(nameof(OperandResolution.OperandText))]
    [InlineData(nameof(OperandResolution.Layer))]
    [InlineData(nameof(OperandResolution.CallCount))]
    public void Every_operands_text_layer_and_call_count_are_required_members(string propertyName)
    {
        var property = typeof(OperandResolution).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    /// <summary>PRD §5.5 tolerates zero occurrences, which is why the refusal is a <c>null</c>
    /// percentage on a figure that still states its resolution — not a refusal to build the figure.
    /// <c>null</c> rather than <c>0</c> follows <c>Guardrail</c>'s own rule: a share never appears
    /// without the count that produced it, and 0% of nothing is not 0%.</summary>
    [Fact]
    public void A_rule_with_no_calls_either_way_still_ships_its_resolution_and_states_no_percentage()
    {
        var figure = PreferAOverB(adherentCalls: 0, divergentCalls: 0);

        Assert.Null(figure.Percentage);
        Assert.Equal(0, figure.TotalCalls);
        Assert.Equal(2, figure.Operands.Count);
        Assert.Equal(SampleVersion, figure.RuleVersion);
    }

    /// <summary>An operand nothing matched is <see cref="OperandResolutionLayer.Unresolved"/> with a
    /// zero count — still an operand on the figure, never dropped, because "which layer resolved
    /// this" is the exact question FR-33 says must travel with the figure. A dropped unresolved
    /// operand would silently shrink the denominator.</summary>
    [Fact]
    public void An_unresolved_operand_still_appears_on_the_figure_with_its_layer_stated()
    {
        var figure = new AdherenceFigure
        {
            RuleVersion = SampleVersion,
            Adherent = new OperandResolution
            {
                OperandText = "rg",
                Layer = OperandResolutionLayer.ExactToolName,
                CallCount = 5,
            },
            Divergent =
            [
                new OperandResolution
                {
                    OperandText = "ack",
                    Layer = OperandResolutionLayer.Unresolved,
                    CallCount = 0,
                },
            ],
        };

        Assert.Equal(100d, figure.Percentage);
        Assert.Contains(figure.Operands, operand => operand.Layer == OperandResolutionLayer.Unresolved);
    }

    /// <summary>The bridge to S-23 (issue #37): the figure is built from
    /// <see cref="OperandResolver.ResolveTwoOperands"/>'s own result and the same corpus, so the
    /// layer each operand reports is the layer that actually resolved it — not a label a caller
    /// chose. FR-32's A-wins subtraction is preserved, so a tool both operands would claim is
    /// counted once, on A's side.</summary>
    [Fact]
    public void FromTwoOperands_carries_each_operands_resolved_layer_and_counts_calls_after_A_wins_subtraction()
    {
        // "search" resolves through the exact-tool-name layer; "Search" (the derived role) would
        // otherwise claim the same tool, and FR-32 says A keeps it.
        var invocations = new[]
        {
            new ToolInvocationShape { ToolName = "search", HasPattern = true },
            new ToolInvocationShape { ToolName = "search", HasPattern = true },
            new ToolInvocationShape { ToolName = "grep_tool", HasPattern = true },
        };

        var resolution = OperandResolver.ResolveTwoOperands("search", "Search", invocations);

        var figure = AdherenceFigure.FromTwoOperands(resolution, invocations, SampleVersion);

        Assert.Equal(OperandResolutionLayer.ExactToolName, figure.Adherent.Layer);
        Assert.Equal(2, figure.AdherentCalls);

        var divergent = Assert.Single(figure.Divergent);
        Assert.Equal(OperandResolutionLayer.DerivedRole, divergent.Layer);
        Assert.Equal(1, divergent.CallCount);

        Assert.Equal(3, figure.TotalCalls);
        Assert.Equal(SampleVersion, figure.RuleVersion);
    }
}
