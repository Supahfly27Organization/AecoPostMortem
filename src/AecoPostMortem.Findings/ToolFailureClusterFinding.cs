using System.Globalization;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// One tool a rule mandates, paired with the rule that mandates it. Session 3 of this project's own
/// notes (<c>FailedToolCallsFinding</c>'s remarks) already name the caller that would resolve this
/// pairing from a real corpus as later work: S-23's four-layer operand resolution (issue #37) is not
/// merged as this story lands, so — matching the established <see cref="ToolCallOutcome"/> pattern —
/// this is taken as an already-resolved plain input rather than reintroduced as a substring match,
/// which is the exact failure FR-31 layer 2 (and this story's own edge case) exists to prevent.
/// </summary>
public sealed record MandatedTool
{
    public required string ToolIdentity { get; init; }

    public required RuleStatement Rule { get; init; }
}

/// <summary>
/// FR-46 (S-40, issue #51): does not recompute failure rates from scratch — it reuses S-14's
/// <see cref="FailedToolCallsCheck"/> (the same rate, grouped the same way) and turns each tool's
/// rate into a <see cref="FindingClass.MissingCapability"/> cluster: Phase D's "highest-value
/// findings with the weakest provenance," so every cluster carries <see cref="Provenance.Inferred"/>
/// rather than the <see cref="Provenance.Derived"/> <see cref="FailedToolCallsFinding"/> uses for the
/// same numbers under <see cref="FindingClass.Waste"/> — the fact of the failure is derived, but
/// "this is a capability gap, not an operator error" is this project's own hypothesis, not a
/// measurement.
/// </summary>
public static class ToolFailureClusterFinding
{
    /// <summary>The abstract check identifier recorded on <see cref="CheckRegistryEntry.CheckId"/>
    /// (issue #23, Scenario 4/5) — distinct from <see cref="FailedToolCallsFinding.CheckId"/> even
    /// though both read the same <see cref="FailedToolCallsCheck"/> result, because they are two
    /// different checks over one shared computation (Waste vs. Missing Capability).</summary>
    public const string CheckId = "tool-failure-clusters";

    /// <summary>FR-46: "match tool names exactly, and state the convention on the table." A
    /// substring match is the exact failure this story's own edge case names — the earlier 49/15
    /// figure that pulled in a different MCP server's tool — so the convention is stated, literally,
    /// on every cluster's evidence rather than left for a reader to assume.</summary>
    public const string ExactMatchConvention = "exact";

    public static ToolFailureClusterResult Run(
        IReadOnlyList<ToolCallOutcome> outcomes,
        IReadOnlyList<MandatedTool> mandatedTools)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(mandatedTools);

        var rates = FailedToolCallsCheck.Run(outcomes);

        var findings = rates
            .Where(rate => rate.FailureRate.Failures > 0)
            .Select(rate => ToFinding(rate, outcomes, mandatedTools))
            .ToArray();

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = outcomes.Count,
            FindingCount = findings.Length,
        };

        return new ToolFailureClusterResult
        {
            Findings = findings,
            RegistryEntry = registryEntry,
        };
    }

    static Finding ToFinding(
        ToolFailureRate rate,
        IReadOnlyList<ToolCallOutcome> outcomes,
        IReadOnlyList<MandatedTool> mandatedTools)
    {
        var failedSessions = outcomes
            .Where(outcome => outcome.ToolIdentity == rate.ToolIdentity && !outcome.Succeeded)
            .Select(outcome => outcome.SessionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sessionId => sessionId, StringComparer.Ordinal)
            .ToArray();

        var evidence = new List<EvidenceItem>
        {
            new() { Field = "toolIdentity", Value = rate.ToolIdentity },
            new()
            {
                Field = "failures",
                Value = rate.FailureRate.Failures.ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Field = "calls",
                Value = rate.FailureRate.Calls.ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Field = "percentage",
                Value = FormatPercentage(rate.FailureRate.Percentage),
            },
            new()
            {
                Field = "sessionCount",
                Value = rate.SessionCount.ToString(CultureInfo.InvariantCulture),
            },
            new() { Field = "matchConvention", Value = ExactMatchConvention },
        };

        // Scenario 2 (issue #51): match by the same exact tool identity the cluster itself grouped
        // by — never a substring — so this cross-reference cannot commit the failure FR-31 layer 2
        // exists to prevent. FirstOrDefault: a caller supplying two MandatedTool entries for the
        // same ToolIdentity is a caller-side ambiguity this check does not attempt to resolve — the
        // first one wins rather than the finding carrying two conflicting rule links.
        var mandate = mandatedTools.FirstOrDefault(
            candidate => string.Equals(candidate.ToolIdentity, rate.ToolIdentity, StringComparison.Ordinal));

        if (mandate is not null)
        {
            // The pair a RuleAdherenceToolChoice finding is identified by (FR-57: class +
            // recurrence key, "the rule statement" per FindingClassRegistry) — quoted so a caller
            // can look that finding up, never a bare pointer this evidence shape has no room for.
            evidence.Add(new EvidenceItem { Field = "mandatingRuleSourceFile", Value = mandate.Rule.SourceFile });
            evidence.Add(new EvidenceItem { Field = "mandatingRuleText", Value = mandate.Rule.Text });
            evidence.Add(new EvidenceItem { Field = "mandatingRuleLinkKind", Value = "hypothesis" });
        }

        return new Finding
        {
            Class = FindingClass.MissingCapability,
            Provenance = Provenance.Inferred,
            Evidence = evidence,
            Recurrence = new Recurrence
            {
                Key = rate.ToolIdentity,
                Occurrences = failedSessions
                    .Select(sessionId => new RecurrenceOccurrence { SessionId = sessionId })
                    .ToArray(),
            },
        };
    }

    static string FormatPercentage(double percentage) =>
        percentage.ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>One run's output: the clusters this check produced, and the registry entry that records
/// the run happened whether or not it found anything (issue #23, Scenario 4).</summary>
public sealed record ToolFailureClusterResult
{
    public required IReadOnlyList<Finding> Findings { get; init; }

    public required CheckRegistryEntry RegistryEntry { get; init; }
}
