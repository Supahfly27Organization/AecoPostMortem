namespace AecoPostMortem.Rules;

/// <summary>Plain input to <see cref="AlwaysPassParamCheck"/>: one rule statement's own text and the
/// one argument key <see cref="RuleShapeKind.AlwaysPassParam"/> requires every in-scope call to
/// carry.</summary>
public sealed record AlwaysPassParamMention
{
    public required string SourceText { get; init; }

    public required string ParamName { get; init; }
}

/// <summary>One real tool call, reduced to what <see cref="AlwaysPassParamCheck"/> needs: whether it
/// spawned a subagent (a structural fact, not a name read from its own arguments — the same signal
/// <see cref="ToolInvocationShape.SpawnsAgent"/> already carries) and which argument keys its own
/// <c>tool.execution_start.data.arguments</c> named. <see cref="ArgumentKeys"/> is opaque provider
/// text, never interpreted as meaning here.</summary>
public sealed record ParamCarryingCall
{
    public required string SessionId { get; init; }

    public required string ToolCallId { get; init; }

    public required bool SpawnsAgent { get; init; }

    public required IReadOnlySet<string> ArgumentKeys { get; init; }
}

/// <summary>One parameter-obligation mention that at least one real subagent-dispatch call violated by
/// omitting the named key, with how many calls did and which sessions carried them. A mention with no
/// spawn calls in scope at all produces no result — the same "no clean case reported" shape
/// <see cref="BannedToolCheck"/> and <see cref="NeverReadPathCheck"/> already follow.</summary>
public sealed record AlwaysPassParamViolation
{
    public required string SourceText { get; init; }

    public required string ParamName { get; init; }

    public required int ViolationCount { get; init; }

    public required IReadOnlyList<string> SessionIds { get; init; }
}

/// <summary>
/// Piece 3's fifth and final slice: an obligation on an argument — "always pass an explicit A" — names
/// only a parameter, never which calls it applies to (the shape's own qualifying clause, "when
/// dispatching", is stripped as decorative during extraction — <see cref="RuleOperandText"/>). The one
/// structural, Repo-Rule-6-safe population this corpus can name without guessing is
/// <see cref="ParamCarryingCall.SpawnsAgent"/> — subagent-dispatch calls — which also happens to match
/// the one real corpus instance this shape was scoped against (this repository's own rule, "always
/// pass an explicit model param when dispatching"). No <see cref="OperandResolver"/> involved: a
/// parameter name is not a tool-vocabulary lookup, the same reasoning <see cref="NeverReadPathCheck"/>
/// gives for a path operand.
/// </summary>
public static class AlwaysPassParamCheck
{
    public static IReadOnlyList<AlwaysPassParamViolation> Run(
        IEnumerable<AlwaysPassParamMention> mentions,
        IEnumerable<ParamCarryingCall> calls)
    {
        ArgumentNullException.ThrowIfNull(mentions);
        ArgumentNullException.ThrowIfNull(calls);

        var spawnCalls = calls.Where(call => call.SpawnsAgent).ToArray();

        var results = new List<AlwaysPassParamViolation>();

        foreach (var mention in mentions)
        {
            var missing = spawnCalls
                .Where(call => !call.ArgumentKeys.Contains(mention.ParamName))
                .ToArray();

            if (missing.Length == 0)
            {
                continue;
            }

            results.Add(new AlwaysPassParamViolation
            {
                SourceText = mention.SourceText,
                ParamName = mention.ParamName,
                ViolationCount = missing.Length,
                SessionIds = missing.Select(call => call.SessionId)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
            });
        }

        return results;
    }
}
