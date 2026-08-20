using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-43 (S-38, issue #47): orchestrates <see cref="Rules.ContradictionCheck"/> — the pairwise,
/// self-match-excluding detection itself stays in <c>Rules</c> (the check-shape catalogue this
/// project's own CLAUDE.md documents); this project's job is the two things <c>Rules</c> cannot do
/// on its own: scope comparison to one rule-set version at a time (FR-43's second scenario — a
/// statement is never compared against a statement from a different version, even inside the same
/// call), and register the run in <see cref="CheckRegistry"/> so a clean pass states its
/// denominator on the "checks that found nothing" surface (FR-42, S-37) exactly the way
/// <c>SilentCheckEnvelopeTests</c> (issue #46) already expects for <see cref="CheckId"/> ahead of
/// this story landing.
/// </summary>
/// <remarks>
/// This check produces no <see cref="Finding"/>: like <c>Ingestion.MalformedLineCheck</c> and
/// <c>Ingestion.SpawnResolutionCheck</c> — the other two special-purpose checks
/// <see cref="CheckRegistryEntry"/>'s own remarks name alongside this one — a contradiction is not
/// one of <see cref="FindingClassRegistry"/>'s four closed finding classes (PRD §3.3 lists rule
/// adherence, waste and missing capability; a rule-set's own internal conflict is none of those).
/// <see cref="Result.Provenance"/> instead carries FR-43's "never Observed" requirement directly, as
/// a <c>required</c> member on this project's own result type — the same "fails construction by
/// being required, not by validating" reasoning this project's <c>Finding.Provenance</c> already
/// uses — because this check can never confirm two statements actually conflict in meaning, only
/// that their surface keyword polarity does.
/// </remarks>
public static class ContradictionCheck
{
    public const string CheckId = "contradiction-check";

    public static Result Run(IReadOnlyList<SessionRuleSet> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var candidates = new List<ContradictionCandidate>();

        // Group by rule-set version identity (repository + order-insensitive block-set hash) —
        // the same identity RuleSetVersioning/RuleSetVersionScope already use — so a statement from
        // one version is never handed to Rules.ContradictionCheck alongside a statement from
        // another, even when both arrive in the same call (FR-43 Scenario 2).
        var byVersion = sessions.GroupBy(session => new RuleSetVersionId
        {
            Repository = session.Repository,
            Hash = RuleSetVersionHasher.ComputeHash(session.Blocks),
        });

        foreach (var versionGroup in byVersion)
        {
            var sessionBlocks = versionGroup
                .Select(session => new SessionInstructionBlocks
                {
                    SessionId = session.SessionId,
                    Blocks = session.Blocks,
                })
                .ToArray();

            // The version's own in-force statements: deduplicated across every session that
            // carried this exact block set, the same identity (SourceFile, Text) every other
            // caller of this dedup step already relies on.
            var statements = RuleStatementDeduplication.Deduplicate(sessionBlocks)
                .Select(occurrence => occurrence.Statement)
                .ToArray();

            candidates.AddRange(Rules.ContradictionCheck.Run(statements));
        }

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = sessions.Count,
            FindingCount = candidates.Count,
        };

        return new Result
        {
            Candidates = candidates,
            Provenance = Provenance.Inferred,
            RegistryEntry = registryEntry,
        };
    }

    /// <summary>One run's output: every contradiction candidate found across every rule-set version
    /// in the input, the provenance every one of them carries (always <see cref="Provenance.Inferred"/>,
    /// never validated at run time — the same structural guarantee <see cref="Finding.Provenance"/>
    /// gives), and the registry entry that records the run happened whether or not it found
    /// anything.</summary>
    public sealed record Result
    {
        public required IReadOnlyList<ContradictionCandidate> Candidates { get; init; }

        public required Provenance Provenance { get; init; }

        public required CheckRegistryEntry RegistryEntry { get; init; }
    }
}
