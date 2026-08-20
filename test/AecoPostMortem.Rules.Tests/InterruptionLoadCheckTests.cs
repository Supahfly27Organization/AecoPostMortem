using System.Reflection;
using System.Runtime.CompilerServices;

namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-20's check (issue #30): permission prompts and questions put to the operator, reduced to two
/// distinct counts that are never summed into one "interruptions" figure — they mean different
/// things, a prompt can be denied and a question can only go answered or not. The edge case is the
/// measured corpus figure — 1,033 permission prompts requested against 1,031 with a recorded
/// outcome — where two prompts never resolved at all.
/// </summary>
public sealed class InterruptionLoadCheckTests
{
    [Fact]
    public void Permission_prompts_and_questions_are_counted_separately_not_summed()
    {
        var permissionPrompts = new[]
        {
            new PermissionPromptOutcome { SessionId = "s1", ResultKind = "approved" },
            new PermissionPromptOutcome { SessionId = "s1", ResultKind = "denied" },
        };
        var questions = new[]
        {
            new QuestionOutcome { SessionId = "s1" },
        };

        var load = InterruptionLoadCheck.Evaluate(permissionPrompts, questions);

        Assert.Equal(2, load.PermissionPromptCount);
        Assert.Equal(1, load.QuestionCount);
        // Nowhere on the result does 2 + 1 = 3 appear — the two figures live on separate,
        // independently-required fields (mirrors HookFailureCounts pairing its two denominators).
    }

    [Fact]
    public void The_measured_edge_case_leaves_two_prompts_with_no_recorded_outcome()
    {
        var permissionPrompts = new List<PermissionPromptOutcome>();
        for (var i = 0; i < 1031; i++)
        {
            permissionPrompts.Add(new PermissionPromptOutcome { SessionId = $"s{i}", ResultKind = "approved" });
        }

        permissionPrompts.Add(new PermissionPromptOutcome { SessionId = "s-unresolved-1", ResultKind = null });
        permissionPrompts.Add(new PermissionPromptOutcome { SessionId = "s-unresolved-2", ResultKind = null });

        var load = InterruptionLoadCheck.Evaluate(permissionPrompts, []);

        Assert.Equal(1033, load.PermissionPromptCount);
        Assert.Equal(1031, load.PermissionPromptsWithOutcome);
        Assert.Equal(2, load.PermissionPromptsWithoutOutcome);
    }

    [Fact]
    public void An_empty_corpus_yields_zero_counts()
    {
        var load = InterruptionLoadCheck.Evaluate([], []);

        Assert.Equal(0, load.PermissionPromptCount);
        Assert.Equal(0, load.PermissionPromptsWithOutcome);
        Assert.Equal(0, load.QuestionCount);
    }

    /// <summary>Mirrors <c>HookFailureCounts</c>' own reasoning: the two figures are structurally
    /// paired members of one result, not two loosely-related values a caller could construct without
    /// one of them.</summary>
    [Theory]
    [InlineData(typeof(InterruptionLoad), nameof(InterruptionLoad.PermissionPromptCount))]
    [InlineData(typeof(InterruptionLoad), nameof(InterruptionLoad.PermissionPromptsWithOutcome))]
    [InlineData(typeof(InterruptionLoad), nameof(InterruptionLoad.QuestionCount))]
    public void The_counts_are_required_members(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }
}
