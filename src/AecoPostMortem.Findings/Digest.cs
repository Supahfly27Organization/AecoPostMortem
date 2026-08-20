namespace AecoPostMortem.Findings;

/// <summary>
/// FR-41's corpus-scope figures for the masthead: sessions, span, repositories, events and tool
/// calls. Every field here is expected to arrive already resolved — maintained by counters kept at
/// ingest time, not computed here. Counting a million rows measured 126 ms on SQLite and 118 ms on
/// Postgres (S-36's own edge case, `docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-
/// query-latency.md`), so this type exists specifically so nothing downstream of it needs to scan a
/// raw-event table to answer "how big is this corpus" — the same reasoning
/// <c>HookFailureFinding.Build</c> gives for taking plain inputs instead of reading through
/// <c>Data</c> directly.
/// </summary>
public sealed record MastheadCounters
{
    public required int SessionCount { get; init; }

    /// <summary>Null exactly when <see cref="SessionCount"/> is zero — an empty corpus has no span.</summary>
    public required DateTimeOffset? SpanStart { get; init; }

    public required DateTimeOffset? SpanEnd { get; init; }

    public required int RepositoryCount { get; init; }

    public required long EventCount { get; init; }

    public required long ToolCallCount { get; init; }

    /// <summary>True while ingestion of this corpus is still under way. A masthead built from
    /// counters with this set states that analysis is incomplete rather than presenting a partial
    /// count as final (edge case, Scenario 4).</summary>
    public required bool IngestInProgress { get; init; }
}

/// <summary>
/// FR-41's rule-coverage bar. Release 1 ships exactly one value: rule extraction (FR-26) and
/// rule-set versioning (FR-27) are Release 2, so the bar cannot be populated yet — that is a stated
/// requirement here, not an omission (S-36's edge case). A future Release-2 value is added to this
/// enum when FR-26/FR-40 land; nothing about this shape needs to change to admit it.
/// </summary>
public enum RuleCoverageStatus
{
    NotYetAnalyzed,
}

/// <summary>
/// The digest's own designed states, distinguishing the two ways a render can have "nothing to show"
/// from a genuine, final zero:
/// <list type="bullet">
/// <item><see cref="NotYetAnalyzed"/> — no check has ever run against this corpus
/// (<see cref="CheckRegistry"/> carries no <see cref="CheckRunStatus.Ran"/> entry). Distinct from a
/// check having run and found nothing — the same distinction <see cref="CheckRegistryEntry"/> itself
/// already draws (issue #23, Scenario 5), reused here at the digest level.</item>
/// <item><see cref="Incomplete"/> — a session under this corpus is still being ingested
/// (<see cref="MastheadCounters.IngestInProgress"/>). Takes precedence over
/// <see cref="NotYetAnalyzed"/>: a mid-ingest corpus answers a different question than a corpus that
/// was never analysed at all, and the two must not collapse into one when both happen to hold.</item>
/// <item><see cref="Analyzed"/> — ingestion is complete and at least one check has run.
/// <see cref="ProcessDigest.RankedFindings"/> is the final answer, even when it is empty.</item>
/// </list>
/// </summary>
public enum DigestState
{
    NotYetAnalyzed,
    Incomplete,
    Analyzed,
}

/// <summary>
/// FR-41 part 2 (S-54)'s repository scope: PRD Part 8 Q5 decided the digest shows one repository at
/// a time by default, selectable — ranking findings across repositories would mix rule sets that
/// were never in force together (FR-28's reasoning applied to this surface). Like
/// <see cref="MastheadCounters"/>, this is an already-resolved plain input: the caller has already
/// picked which repository's findings were handed to <see cref="ProcessDigest.Build"/>, and this
/// type only states which one that was and which others exist to select — the seam for a later
/// cross-repository view, not that view itself (the measured corpus holds 3 repositories with one
/// dominant at 25 of 35 sessions, so a cross-repository view is a later option, not the default).
/// </summary>
public sealed record RepositoryScope
{
    /// <summary>The repository the caller resolved <see cref="Finding"/>s for. Null when no session
    /// in the store carries a repository at all (<c>Session.Repository</c> is nullable) — an honest
    /// "no repository information" rather than a fabricated default.</summary>
    public required string? SelectedRepository { get; init; }

    /// <summary>Every repository the store holds, for the selector. A single-entry list is the
    /// common shape this story ships; more than one is the seam a later cross-repository story
    /// switches through — this type does not itself re-filter <see cref="ProcessDigest.RankedFindings"/>
    /// when more than one is present.</summary>
    public required IReadOnlyList<string> AvailableRepositories { get; init; }

    /// <summary>Every session id in this scope — the same set every check
    /// <see cref="ProcessDigest.RankedFindings"/> was computed over, so a finding's own
    /// <see cref="Recurrence.Occurrences"/> is always a subset of this list. Ordered chronologically
    /// by the session's own real start time, never by session id text (random UUIDs in the reference
    /// corpus have no relationship to arrival order — the same defect PR #112 fixed for rule-set
    /// version ordering). This is what a per-finding session strip needs and
    /// <see cref="Recurrence"/> alone cannot give it: which sessions were *not* touched, and in what
    /// position, not only which ones were.</summary>
    public required IReadOnlyList<string> SessionIds { get; init; }
}

/// <summary>The masthead: <see cref="MastheadCounters"/> plus the rule-coverage bar's current state
/// and FR-41 part 2's repository scope.</summary>
public sealed record Masthead
{
    public required MastheadCounters Counters { get; init; }

    public required RuleCoverageStatus RuleCoverage { get; init; }

    public required RepositoryScope RepositoryScope { get; init; }
}

/// <summary>
/// FR-41's corpus digest: the masthead, plus every finding across the corpus ranked by how many
/// sessions it touched — the ranking's entire purpose (S-36's edge case) is to make a finding
/// touching one session visually subordinate to one touching thirty, so the order this type
/// publishes is itself the point, not a pass-through of whatever order findings arrived in.
/// <see cref="RankedFindings"/> never carries an <see cref="Provenance.Inferred"/> finding
/// (FR-48, S-42/issue #52): a hypothesis has no session-count claim to rank on, and ranking one
/// beside an Observed or Derived finding is exactly the "guess laundered into a process change"
/// the PRD's own risk table (§3.8) names. Every Inferred finding is in
/// <see cref="InferredFindings"/> instead — its own section, never interleaved by rank.
/// </summary>
public sealed record ProcessDigest
{
    public required Masthead Masthead { get; init; }

    public required DigestState State { get; init; }

    /// <summary>Observed and Derived findings only, ranked by <see cref="SessionsAffected"/>
    /// descending. Never contains a <see cref="Provenance.Inferred"/> finding — see
    /// <see cref="InferredFindings"/>.</summary>
    public required IReadOnlyList<Finding> RankedFindings { get; init; }

    /// <summary>Every <see cref="Provenance.Inferred"/> finding, in the order <see cref="Build"/>
    /// received them — FR-48's own section, deliberately unranked: applying
    /// <see cref="SessionsAffected"/> to a hypothesis would dress it up with the same
    /// measured-looking figure that ranks Observed and Derived findings, which is the exact
    /// conflation FR-48 exists to prevent.</summary>
    public required IReadOnlyList<Finding> InferredFindings { get; init; }

    /// <summary>Builds the digest from plain, already-resolved inputs. FR-41 needs no individual
    /// finding story to be complete (S-36's own dependency note): it renders whatever findings
    /// already exist in the store, ranked by <see cref="SessionsAffected"/>.</summary>
    public static ProcessDigest Build(
        MastheadCounters counters,
        CheckRegistry checkRegistry,
        IReadOnlyList<Finding> findings,
        RepositoryScope repositoryScope)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(checkRegistry);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(repositoryScope);

        var state = counters.IngestInProgress
            ? DigestState.Incomplete
            : checkRegistry.Entries.Any(entry => entry.Status == CheckRunStatus.Ran)
                ? DigestState.Analyzed
                : DigestState.NotYetAnalyzed;

        var ranked = findings
            .Where(finding => finding.Provenance != Provenance.Inferred)
            .OrderByDescending(SessionsAffected)
            .ToList();

        var inferred = findings
            .Where(finding => finding.Provenance == Provenance.Inferred)
            .ToList();

        return new ProcessDigest
        {
            Masthead = new Masthead
            {
                Counters = counters,
                RuleCoverage = RuleCoverageStatus.NotYetAnalyzed,
                RepositoryScope = repositoryScope,
            },
            State = state,
            RankedFindings = ranked,
            InferredFindings = inferred,
        };
    }

    /// <summary>How many distinct sessions a finding touched — the ranking key FR-41 names (§5.1's
    /// primary metric, FR-57's recurrence key made concrete).</summary>
    public static int SessionsAffected(Finding finding) =>
        finding.Recurrence.Occurrences.Select(occurrence => occurrence.SessionId).Distinct().Count();
}
