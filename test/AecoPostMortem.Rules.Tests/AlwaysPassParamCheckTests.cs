namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// Piece 3's fifth and final slice: an obligation on an argument — "always pass an explicit A". The
/// only structural, Repo-Rule-6-safe population <see cref="ParamCarryingCall.SpawnsAgent"/> gives is
/// subagent-dispatch calls, which is also the one real corpus instance this shape was scoped against
/// (this repository's own rule, "always pass an explicit model param when dispatching"). A non-spawn
/// call is never counted, and a mention with no spawn calls at all in scope produces no result — the
/// same "no clean case reported" shape <see cref="BannedToolCheck"/> and <see cref="NeverReadPathCheck"/>
/// already follow.
/// </summary>
public sealed class AlwaysPassParamCheckTests
{
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
    public void A_spawn_call_missing_the_named_parameter_is_reported_as_a_violation()
    {
        var mentions = new[]
        {
            new AlwaysPassParamMention
            {
                SourceText = "Always pass an explicit `model` param when dispatching a subagent.",
                ParamName = "model",
            },
        };
        var calls = new[] { Call("s1", "tc1", spawnsAgent: true, argumentsRecorded: true, "description", "prompt") };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        var violation = Assert.Single(results);
        Assert.Equal("model", violation.ParamName);
        Assert.Equal(1, violation.ViolationCount);
        Assert.Equal(["s1"], violation.SessionIds);
    }

    [Fact]
    public void A_spawn_call_carrying_the_named_parameter_produces_no_violation()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };
        var calls = new[] { Call("s1", "tc1", spawnsAgent: true, argumentsRecorded: true, "model") };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        Assert.Empty(results);
    }

    [Fact]
    public void A_non_spawn_call_missing_the_parameter_is_never_counted()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };
        var calls = new[] { Call("s1", "tc1", spawnsAgent: false, argumentsRecorded: true) };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        Assert.Empty(results);
    }

    [Fact]
    public void No_spawn_calls_at_all_produces_no_violation()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };

        var results = AlwaysPassParamCheck.Run(mentions, []);

        Assert.Empty(results);
    }

    /// <summary>Code review caught this: a call whose own RAW arguments were never recorded at all (no
    /// matching <c>tool.execution_start</c> event, or a non-object-shaped value) is a different fact
    /// from a call whose arguments were recorded and genuinely omitted the key — "we don't know" must
    /// never read as "it violated", the same "Unresolved is its own state, not an empty Tools set"
    /// discipline <c>OperandResolver</c> already documents.</summary>
    [Fact]
    public void A_spawn_call_with_no_recorded_arguments_produces_no_violation()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };
        var calls = new[] { Call("s1", "tc1", spawnsAgent: true, argumentsRecorded: false) };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        Assert.Empty(results);
    }

    [Fact]
    public void A_mix_of_recorded_and_unrecorded_calls_counts_only_the_recorded_violation()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };
        var calls = new[]
        {
            Call("s1", "tc1", spawnsAgent: true, argumentsRecorded: true),
            Call("s2", "tc2", spawnsAgent: true, argumentsRecorded: false),
        };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        var violation = Assert.Single(results);
        Assert.Equal(1, violation.ViolationCount);
        Assert.Equal(["s1"], violation.SessionIds);
    }

    [Fact]
    public void Matching_is_case_sensitive_because_argument_keys_are_provider_defined_json_fields()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };
        var calls = new[] { Call("s1", "tc1", spawnsAgent: true, argumentsRecorded: true, "Model") };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        var violation = Assert.Single(results);
        Assert.Equal(1, violation.ViolationCount);
    }

    [Fact]
    public void Violations_across_two_sessions_report_both_session_ids()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };
        var calls = new[]
        {
            Call("s1", "tc1", spawnsAgent: true, argumentsRecorded: true),
            Call("s2", "tc2", spawnsAgent: true, argumentsRecorded: true),
        };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        var violation = Assert.Single(results);
        Assert.Equal(2, violation.ViolationCount);
        Assert.Equal(["s1", "s2"], violation.SessionIds);
    }

    [Fact]
    public void A_mention_with_no_matching_kind_of_call_produces_no_result_rather_than_a_zero_violation()
    {
        var mentions = new[] { new AlwaysPassParamMention { SourceText = "...", ParamName = "model" } };
        var calls = new[] { Call("s1", "tc1", spawnsAgent: true, argumentsRecorded: true, "model") };

        var results = AlwaysPassParamCheck.Run(mentions, calls);

        Assert.DoesNotContain(results, v => v.ViolationCount == 0);
    }
}
