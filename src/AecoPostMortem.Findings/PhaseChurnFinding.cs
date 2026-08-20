using System.Globalization;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-19's orchestration (issue #29): turns <see cref="PhaseChurnCheck"/>'s per-session results into
/// <see cref="FindingClass.Waste"/> findings plus a check-registry entry. This is the orchestration
/// <c>AecoPostMortem.Rules</c> deliberately cannot do itself — <c>Rules</c> reports every session
/// that declared at least one intent, churned or not; deciding that only a session which actually
/// returned to an earlier phase is worth surfacing as a finding belongs here, the same split
/// <c>FailedToolCallsFinding</c> documents for its own "only tools with recorded failures" filter.
/// </summary>
public static class PhaseChurnFinding
{
    /// <summary>The abstract check identifier recorded on <see cref="CheckRegistryEntry.CheckId"/>
    /// (issue #23, Scenario 4/5).</summary>
    public const string CheckId = "phase-churn";

    public static Result Run(IReadOnlyList<DeclaredIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        var results = PhaseChurnCheck.Run(intents);
        var findings = results
            .Where(result => result.Returns > 0)
            .Select(ToFinding)
            .ToArray();

        var population = intents
            .Select(intent => intent.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = population,
            FindingCount = findings.Length,
        };

        return new Result { Findings = findings, RegistryEntry = registryEntry };
    }

    /// <summary>
    /// FR-57's recurrence key for this class is the session id: unlike the other Waste checks,
    /// phase churn has no shared sub-object — no path, hook or tool identity — for two sessions to
    /// recur around, because it is a whole-session aggregate over that session's own declared
    /// intents. Each churning session is therefore its own finding, with exactly one occurrence:
    /// itself.
    /// </summary>
    static Finding ToFinding(PhaseChurnResult result)
    {
        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Field = "returns",
                Value = result.Returns.ToString(CultureInfo.InvariantCulture),
            },
            new()
            {
                Field = "totalIntents",
                Value = result.TotalIntents.ToString(CultureInfo.InvariantCulture),
            },
        };
        evidence.AddRange(result.Vocabulary.Select(phase => new EvidenceItem
        {
            Field = "vocabulary",
            Value = phase,
        }));

        return new Finding
        {
            Class = FindingClass.Waste,
            // Derived, not Observed: FR-19 says a legitimate iteration is indistinguishable from
            // drift on this data, so the finding says so rather than claiming certainty it does
            // not have.
            Provenance = Provenance.Derived,
            Headline = BuildHeadline(result),
            Evidence = evidence,
            Recurrence = new Recurrence
            {
                Key = result.SessionId,
                Occurrences = [new RecurrenceOccurrence { SessionId = result.SessionId }],
            },
        };
    }

    /// <summary>Mockup parity item #5: grounded in the same returns/totalIntents pair
    /// <see cref="Evidence"/> already carries for this session.</summary>
    static string BuildHeadline(PhaseChurnResult result) => string.Format(
        CultureInfo.InvariantCulture,
        "Session {0} churned through phases {1} {2} across {3} declared {4}.",
        result.SessionId,
        result.Returns,
        HeadlineText.Pluralize(result.Returns, "time"),
        result.TotalIntents,
        HeadlineText.Pluralize(result.TotalIntents, "intent"));

    /// <summary>One run's output: the findings this check produced, and the registry entry that
    /// records the run happened whether or not it found anything (issue #23, Scenario 4).</summary>
    public sealed record Result
    {
        public required IReadOnlyList<Finding> Findings { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }
}
