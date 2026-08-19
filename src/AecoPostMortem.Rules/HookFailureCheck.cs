namespace AecoPostMortem.Rules;

/// <summary>
/// One session's hook outcome — plain inputs, no tool name and no session content, just what
/// FR-17's check is allowed to see: whether the session had at least one failed hook.start/
/// hook.end pair, and whether it made at least one tool call.
/// </summary>
public sealed record SessionHookOutcome
{
    public required string SessionId { get; init; }

    public required bool HookFailed { get; init; }

    public required bool MadeToolCall { get; init; }
}

/// <summary>
/// A count paired with the population it was drawn from. Neither is meaningful without the
/// other — "34" alone says nothing until "of 35" sits beside it.
/// </summary>
public sealed record SessionCount
{
    public required int Count { get; init; }

    public required int Population { get; init; }
}

/// <summary>
/// FR-17's two denominators, both <c>required</c> so a caller cannot construct a hook-failure
/// result that states one figure without the other (issue #27, Scenario 1) — mirrors
/// <c>AecoPostMortem.Findings.Finding.Provenance</c> being <c>required</c> rather than validated
/// at run time. A measured 34 of 35 sessions overall and 32 of the 33 that made a tool call are
/// both correct at once: two sessions failed the hook despite making no tool call, which is
/// exactly why either figure printed alone would read as a contradiction.
/// </summary>
public sealed record HookFailureCounts
{
    public required SessionCount OverAllSessions { get; init; }

    public required SessionCount OverSessionsWithToolCall { get; init; }
}

/// <summary>
/// Pure check logic: reduces a corpus of <see cref="SessionHookOutcome"/> to the paired counts
/// FR-17 requires. Takes plain inputs and returns a result — no tool name, no MCP server, no
/// repository (the non-negotiable invariant this project's own containment test enforces).
/// </summary>
public static class HookFailureCheck
{
    public static HookFailureCounts Evaluate(IReadOnlyList<SessionHookOutcome> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var sessionsWithToolCall = sessions.Where(session => session.MadeToolCall).ToList();

        return new HookFailureCounts
        {
            OverAllSessions = new SessionCount
            {
                Count = sessions.Count(session => session.HookFailed),
                Population = sessions.Count,
            },
            OverSessionsWithToolCall = new SessionCount
            {
                Count = sessionsWithToolCall.Count(session => session.HookFailed),
                Population = sessionsWithToolCall.Count,
            },
        };
    }
}
