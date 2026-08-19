using System.Reflection;
using System.Runtime.CompilerServices;

namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-17's check: two counts, each paired with the population it was drawn from, and the two
/// populations are never derivable from one another (issue #27). The edge case is the measured
/// corpus figure — 34 of 35 sessions overall, 32 of the 33 that made a tool call — where two
/// sessions failed the hook despite making no tool call at all.
/// </summary>
public sealed class HookFailureCheckTests
{
    [Fact]
    public void Both_denominators_are_computed_from_the_measured_edge_case()
    {
        var sessions = BuildMeasuredCorpus();

        var counts = HookFailureCheck.Evaluate(sessions);

        Assert.Equal(34, counts.OverAllSessions.Count);
        Assert.Equal(35, counts.OverAllSessions.Population);
        Assert.Equal(32, counts.OverSessionsWithToolCall.Count);
        Assert.Equal(33, counts.OverSessionsWithToolCall.Population);
    }

    /// <summary>The edge case named in issue #27: two sessions failed the hook while making no
    /// tool call at all, which is exactly why the all-sessions figure (34) exceeds the
    /// tool-call-sessions figure (32) by more than the population gap alone would explain.</summary>
    [Fact]
    public void Sessions_that_failed_the_hook_with_no_tool_call_count_only_toward_the_all_sessions_figure()
    {
        var sessions = new[]
        {
            new SessionHookOutcome { SessionId = "s1", HookFailed = true, MadeToolCall = false },
            new SessionHookOutcome { SessionId = "s2", HookFailed = false, MadeToolCall = true },
        };

        var counts = HookFailureCheck.Evaluate(sessions);

        Assert.Equal(1, counts.OverAllSessions.Count);
        Assert.Equal(2, counts.OverAllSessions.Population);
        Assert.Equal(0, counts.OverSessionsWithToolCall.Count);
        Assert.Equal(1, counts.OverSessionsWithToolCall.Population);
    }

    [Fact]
    public void No_failures_is_a_real_zero_not_an_absent_result()
    {
        var sessions = new[]
        {
            new SessionHookOutcome { SessionId = "s1", HookFailed = false, MadeToolCall = true },
            new SessionHookOutcome { SessionId = "s2", HookFailed = false, MadeToolCall = false },
        };

        var counts = HookFailureCheck.Evaluate(sessions);

        Assert.Equal(0, counts.OverAllSessions.Count);
        Assert.Equal(2, counts.OverAllSessions.Population);
        Assert.Equal(0, counts.OverSessionsWithToolCall.Count);
        Assert.Equal(1, counts.OverSessionsWithToolCall.Population);
    }

    [Fact]
    public void An_empty_corpus_yields_zero_populations()
    {
        var counts = HookFailureCheck.Evaluate([]);

        Assert.Equal(0, counts.OverAllSessions.Population);
        Assert.Equal(0, counts.OverSessionsWithToolCall.Population);
    }

    /// <summary>Mirrors <c>Finding.Provenance</c> being <c>required</c>
    /// (<c>AecoPostMortem.Findings/Finding.cs</c>): the two denominators are structurally paired,
    /// not two loosely-related nullable ints — an object initializer that omits either is a
    /// compile error (CS9035), not a runtime check.</summary>
    [Theory]
    [InlineData(typeof(HookFailureCounts), nameof(HookFailureCounts.OverAllSessions))]
    [InlineData(typeof(HookFailureCounts), nameof(HookFailureCounts.OverSessionsWithToolCall))]
    [InlineData(typeof(SessionCount), nameof(SessionCount.Count))]
    [InlineData(typeof(SessionCount), nameof(SessionCount.Population))]
    public void The_denominator_fields_are_required_members(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    static SessionHookOutcome[] BuildMeasuredCorpus()
    {
        // 33 sessions made a tool call; 32 of those also failed the hook, 1 did not.
        var withToolCall = Enumerable.Range(1, 33)
            .Select(i => new SessionHookOutcome
            {
                SessionId = $"tool-call-{i}",
                HookFailed = i <= 32,
                MadeToolCall = true,
            });

        // 2 more sessions made no tool call at all, and both still failed the hook — the
        // contradiction FR-17 exists to explain rather than hide.
        var withoutToolCall = new[]
        {
            new SessionHookOutcome { SessionId = "no-tool-call-1", HookFailed = true, MadeToolCall = false },
            new SessionHookOutcome { SessionId = "no-tool-call-2", HookFailed = true, MadeToolCall = false },
        };

        return [.. withToolCall, .. withoutToolCall];
    }
}
