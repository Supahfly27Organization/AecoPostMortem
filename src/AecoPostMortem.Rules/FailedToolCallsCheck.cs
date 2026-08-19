namespace AecoPostMortem.Rules;

/// <summary>
/// One completed tool call, as a plain input to a check — never the entity that produced it. This
/// project references nothing (FR-34's invariant), so it cannot see, and never names,
/// <c>AecoPostMortem.Data.Execution.ToolCall</c>. <see cref="Succeeded"/> is a required
/// <c>bool</c>, not <c>bool?</c>: Copilot's own success field is measured present on 16,076 of
/// 16,076 completions, so a caller resolving this operand must already have turned an absent
/// value into a parser defect before it reaches here — there is no null this check could misread
/// as "unknown".
/// </summary>
public sealed record ToolCallOutcome
{
    public required string SessionId { get; init; }

    /// <summary>Whatever tool identity the operand carries — never a hardcoded name (Repo Rule 6).
    /// The check groups by this value alone, so it never special-cases any specific tool.</summary>
    public required string ToolIdentity { get; init; }

    public required bool Succeeded { get; init; }
}

/// <summary>
/// A rate that cannot exist without the counts that produced it. <see cref="Failures"/> and
/// <see cref="Calls"/> are both <c>required</c>; <see cref="Percentage"/> has no setter at all —
/// it is derived from the two, so there is no constructor path that can supply a bare percentage
/// on its own (issue #26, Scenario 1: "a rate never appears without its counts"), the same
/// reasoning <c>AecoPostMortem.Findings/CLAUDE.md</c> gives for <c>Finding.Provenance</c> being
/// <c>required</c>: structural beats conventional.
/// </summary>
public sealed record FailureRate
{
    public required int Failures { get; init; }

    public required int Calls { get; init; }

    public double Percentage => Calls == 0 ? 0d : 100d * Failures / Calls;
}

/// <summary>
/// One tool's failure rate. <see cref="SessionCount"/> — the number of distinct sessions that
/// called this tool, not the number of calls — is <c>required</c> alongside
/// <see cref="FailureRate"/> so a tool called only a handful of times cannot be ranked as if it
/// were common (issue #26, Scenario 2).
/// </summary>
public sealed record ToolFailureRate
{
    public required string ToolIdentity { get; init; }

    public required FailureRate FailureRate { get; init; }

    public required int SessionCount { get; init; }
}

/// <summary>
/// FR-16 (S-14): failure rate per tool, grouped by whatever tool identity the operand carries.
/// Takes the whole population of completed calls considered and returns one
/// <see cref="ToolFailureRate"/> per distinct identity observed — including tools with zero
/// failures, because deciding which rates are worth surfacing as a finding is
/// <c>AecoPostMortem.Findings</c>'s job, not this one's.
/// </summary>
public static class FailedToolCallsCheck
{
    public static IReadOnlyList<ToolFailureRate> Run(IReadOnlyList<ToolCallOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        return outcomes
            .GroupBy(outcome => outcome.ToolIdentity, StringComparer.Ordinal)
            .Select(group =>
            {
                var calls = group.ToArray();

                return new ToolFailureRate
                {
                    ToolIdentity = group.Key,
                    FailureRate = new FailureRate
                    {
                        Failures = calls.Count(call => !call.Succeeded),
                        Calls = calls.Length,
                    },
                    SessionCount = calls
                        .Select(call => call.SessionId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                };
            })
            .ToArray();
    }
}
