namespace AecoPostMortem.Rules;

/// <summary>Plain input to <see cref="UseAAfterBCheck"/>: one rule statement's own text and its two
/// operand texts — <see cref="LaterToolText"/> names the step <see cref="RuleShapeKind.UseAAfterB"/>
/// requires to come second, <see cref="EarlierToolText"/> the prerequisite it must follow, whichever
/// way the statement itself phrased the ordering ("use A after B" or "use B before A").</summary>
public sealed record UseAAfterBMention
{
    public required string SourceText { get; init; }

    public required string LaterToolText { get; init; }

    public required string EarlierToolText { get; init; }
}

/// <summary>One real tool call, reduced to what <see cref="UseAAfterBCheck"/> needs to place it in its
/// own session's chronological order. <see cref="StartedAt"/> is an opaque, ordinally-sortable
/// timestamp string — never parsed as a <see cref="DateTime"/> (PRD §3.8) — the same ordering
/// discipline <c>AbortedTurnCheck</c> already applies to <c>TurnRecord.StartedAt</c>.</summary>
public sealed record TimedToolCall
{
    public required string SessionId { get; init; }

    public required string ToolCallId { get; init; }

    public required string ToolName { get; init; }

    public required string StartedAt { get; init; }
}

/// <summary>One rule statement whose ordering was violated at least once, with how many calls to the
/// later tool had no earlier prerequisite call in their own session, and which sessions carried one.
/// A mention whose either operand never resolves against the corpus produces no result — the same
/// "no clean case reported" shape <see cref="BannedToolCheck"/> already follows.</summary>
public sealed record UseAAfterBViolation
{
    public required string SourceText { get; init; }

    public required string LaterToolText { get; init; }

    public required string EarlierToolText { get; init; }

    public required int ViolationCount { get; init; }

    public required IReadOnlyList<string> SessionIds { get; init; }
}

/// <summary>
/// Piece 3's adherence check for <see cref="RuleShapeKind.UseAAfterB"/>: an ordering names two tools,
/// and the only adherence-worthy question is whether a real call to the later tool ever happened in a
/// session with no earlier call to the prerequisite tool anywhere before it in that same session — the
/// prerequisite does not need to be the immediately preceding call, only to have occurred at some
/// point earlier in the session.
/// </summary>
public static class UseAAfterBCheck
{
    public static IReadOnlyList<UseAAfterBViolation> Run(
        IEnumerable<UseAAfterBMention> mentions,
        IEnumerable<TimedToolCall> calls,
        IEnumerable<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(mentions);
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(invocations);

        var invocationList = invocations as IReadOnlyCollection<ToolInvocationShape> ?? invocations.ToList();

        var orderedBySession = calls
            .GroupBy(call => call.SessionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(call => call.StartedAt, StringComparer.Ordinal)
                    .ThenBy(call => call.ToolCallId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var results = new List<UseAAfterBViolation>();

        foreach (var mention in mentions)
        {
            var resolution = OperandResolver.ResolveTwoOperands(mention.LaterToolText, mention.EarlierToolText, invocationList);
            if (resolution.OperandA.Layer == OperandResolutionLayer.Unresolved
                || resolution.OperandB.Layer == OperandResolutionLayer.Unresolved)
            {
                continue;
            }

            var laterTools = resolution.OperandA.Tools;
            var earlierTools = resolution.OperandB.Tools;

            var violatingSessionIds = new List<string>();
            var violationCount = 0;

            foreach (var (sessionId, sessionCalls) in orderedBySession)
            {
                var seenEarlier = false;
                var sessionHasViolation = false;

                foreach (var call in sessionCalls)
                {
                    if (earlierTools.Contains(call.ToolName))
                    {
                        seenEarlier = true;
                    }
                    else if (laterTools.Contains(call.ToolName) && !seenEarlier)
                    {
                        violationCount++;
                        sessionHasViolation = true;
                    }
                }

                if (sessionHasViolation)
                {
                    violatingSessionIds.Add(sessionId);
                }
            }

            if (violationCount == 0)
            {
                continue;
            }

            results.Add(new UseAAfterBViolation
            {
                SourceText = mention.SourceText,
                LaterToolText = mention.LaterToolText,
                EarlierToolText = mention.EarlierToolText,
                ViolationCount = violationCount,
                SessionIds = violatingSessionIds.Order(StringComparer.Ordinal).ToArray(),
            });
        }

        return results;
    }
}
