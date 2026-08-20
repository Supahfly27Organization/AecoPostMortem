namespace AecoPostMortem.Rules;

/// <summary>Plain input to <see cref="BannedToolCheck"/>: one rule statement's own text and the one
/// tool it names as banned. Unlike <see cref="RuleToolMention"/>, this carries no
/// <see cref="ToolRole"/> — a prohibition does not target a role the way a recommendation does (see
/// <see cref="BannedToolCheck"/>'s own remarks for why <see cref="ToolVocabularyMismatchCheck"/> does
/// not fit this shape).</summary>
public sealed record BannedToolMention
{
    public required string RuleText { get; init; }

    public required string NamedTool { get; init; }
}

/// <summary>One banned-tool mention that resolved to at least one real tool, and how many times that
/// tool (or tools, if resolution named more than one — the server-field and role layers can) was
/// actually called. <see cref="CallCount"/> is always at least 1: every resolution layer
/// <see cref="OperandResolver.Resolve"/> can return is derived from calls that were actually
/// observed, so a resolved mention is structurally a violation — there is no "resolved but never
/// called" state to represent. A mention that resolves to nothing produces no
/// <see cref="BannedToolUsage"/> at all — an unresolved name is indistinguishable from a banned tool
/// the corpus never called, and neither is a violation worth reporting.</summary>
public sealed record BannedToolUsage
{
    public required string RuleText { get; init; }

    public required string NamedTool { get; init; }

    public required IReadOnlyList<string> ResolvedTools { get; init; }

    public required int CallCount { get; init; }
}

/// <summary>
/// Piece 3's adherence check for <see cref="RuleShapeKind.ToolIsBanned"/>: a prohibition names one
/// tool, and the only fact worth reporting is whether that tool was actually called — never a role
/// comparison. <see cref="ToolVocabularyMismatchCheck"/> was built for a recommendation ("prefer /
/// always use tool X for role Y"), where "X is not the dominant tool for Y" is a real mismatch; for a
/// prohibition, "the banned tool is not the dominant tool of the role it happens to classify into"
/// would fire on nearly every real ban and say nothing, and "the banned tool was never called" would
/// misreport a ban being honored as a finding. Every <see cref="BannedToolUsage"/> this returns is
/// therefore already a violation (see its own remarks on why <see cref="BannedToolUsage.CallCount"/>
/// can never be zero) — unlike <see cref="FailedToolCallsCheck"/>'s "report every candidate, let the
/// caller filter" pattern, there is no clean case for this check to report at all.
/// </summary>
public static class BannedToolCheck
{
    public static IReadOnlyList<BannedToolUsage> Run(
        IEnumerable<BannedToolMention> mentions,
        IEnumerable<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(mentions);
        ArgumentNullException.ThrowIfNull(invocations);

        var calls = invocations as IReadOnlyCollection<ToolInvocationShape> ?? invocations.ToList();

        var results = new List<BannedToolUsage>();

        foreach (var mention in mentions)
        {
            var resolved = OperandResolver.Resolve(mention.NamedTool, calls);
            if (resolved.Layer == OperandResolutionLayer.Unresolved)
            {
                continue;
            }

            var callCount = calls.Count(call => resolved.Tools.Contains(call.ToolName));

            results.Add(new BannedToolUsage
            {
                RuleText = mention.RuleText,
                NamedTool = mention.NamedTool,
                ResolvedTools = resolved.Tools.ToArray(),
                CallCount = callCount,
            });
        }

        return results;
    }
}
