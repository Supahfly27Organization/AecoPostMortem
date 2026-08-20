using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Piece 3's fifth and final slice: orchestrates <c>AecoPostMortem.Rules.AlwaysPassParamCheck</c> into
/// <c>FindingClass.RuleAdherenceToolChoice</c> findings — a subagent-dispatch call that omitted a
/// parameter the rule requires on every such call.
/// </summary>
public sealed class AlwaysPassParamFindingTests
{
    static RuleShapeMatch AlwaysPassParamMatch(string ruleText, string paramName) => new()
    {
        Statement = new RuleStatement { SourceFile = "CLAUDE.md", Text = ruleText },
        Kind = RuleShapeKind.AlwaysPassParam,
        OperandAText = paramName,
    };

    static ParamCarryingCall Call(
        string sessionId, string toolCallId, bool spawnsAgent, bool argumentsRecorded, params string[] argumentKeys) =>
        new()
        {
            SessionId = sessionId,
            ToolCallId = toolCallId,
            SpawnsAgent = spawnsAgent,
            ArgumentsRecorded = argumentsRecorded,
            ArgumentKeys = argumentKeys.ToHashSet(StringComparer.Ordinal),
        };

    [Fact]
    public void A_spawn_call_missing_the_named_parameter_produces_a_rule_adherence_finding()
    {
        var matches = new[]
        {
            AlwaysPassParamMatch("Always pass an explicit `model` param when dispatching a subagent.", "model"),
        };
        var calls = new[] { Call("session-1", "tc1", spawnsAgent: true, argumentsRecorded: true, "prompt") };

        var result = AlwaysPassParamFinding.Run(matches, calls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.RuleAdherenceToolChoice, finding.Class);
        Assert.Equal(Provenance.Derived, finding.Provenance);
        Assert.Contains(finding.Evidence, item => item.Field == "param_name" && item.Value == "model");
        Assert.Contains(finding.Evidence, item => item.Field == "violation_count" && item.Value == "1");
        Assert.Equal("The `model` parameter was omitted on 1 call that should have carried it.", finding.Headline);
        Assert.Single(finding.Recurrence.Occurrences);
        Assert.Equal("session-1", finding.Recurrence.Occurrences[0].SessionId);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
    }

    [Fact]
    public void A_spawn_call_carrying_the_named_parameter_produces_no_finding()
    {
        var matches = new[]
        {
            AlwaysPassParamMatch("Always pass an explicit `model` param when dispatching a subagent.", "model"),
        };
        var calls = new[] { Call("session-1", "tc1", spawnsAgent: true, argumentsRecorded: true, "model") };

        var result = AlwaysPassParamFinding.Run(matches, calls);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    [Fact]
    public void A_non_spawn_call_missing_the_parameter_is_never_counted()
    {
        var matches = new[]
        {
            AlwaysPassParamMatch("Always pass an explicit `model` param when dispatching a subagent.", "model"),
        };
        var calls = new[] { Call("session-1", "tc1", spawnsAgent: false, argumentsRecorded: true) };

        var result = AlwaysPassParamFinding.Run(matches, calls);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_non_always_pass_param_shape_match_is_ignored()
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
        var calls = new[] { Call("session-1", "tc1", spawnsAgent: true, argumentsRecorded: true) };

        var result = AlwaysPassParamFinding.Run(matches, calls);

        Assert.Empty(result.Findings);
    }

    /// <summary>Code review caught this: a call with no recorded arguments must not read as a
    /// violation — see <see cref="ParamCarryingCall.ArgumentsRecorded"/>'s own remarks.</summary>
    [Fact]
    public void A_spawn_call_with_no_recorded_arguments_produces_no_finding()
    {
        var matches = new[]
        {
            AlwaysPassParamMatch("Always pass an explicit `model` param when dispatching a subagent.", "model"),
        };
        var calls = new[] { Call("session-1", "tc1", spawnsAgent: true, argumentsRecorded: false) };

        var result = AlwaysPassParamFinding.Run(matches, calls);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Population_counts_every_distinct_session_the_calls_span()
    {
        var matches = new[]
        {
            AlwaysPassParamMatch("Always pass an explicit `model` param when dispatching a subagent.", "model"),
        };
        var calls = new[]
        {
            Call("session-1", "tc1", spawnsAgent: true, argumentsRecorded: true, "model"),
            Call("session-2", "tc2", spawnsAgent: false, argumentsRecorded: true),
        };

        var result = AlwaysPassParamFinding.Run(matches, calls);

        Assert.Equal(2, result.RegistryEntry.Population);
    }
}
