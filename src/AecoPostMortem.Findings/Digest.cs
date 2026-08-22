using AecoPostMortem.Rules;

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

    /// <summary>Every <c>Agent</c> row in the corpus, regardless of repository — the same plain
    /// row count <see cref="SessionRecording.SessionMasthead.SubagentCount"/> already reports at the
    /// single-session scope (<c>agents.Count</c>), just corpus-wide here (mockup parity item #8).
    /// </summary>
    public required int SubagentCount { get; init; }

    /// <summary>True while ingestion of this corpus is still under way. A masthead built from
    /// counters with this set states that analysis is incomplete rather than presenting a partial
    /// count as final (edge case, Scenario 4).</summary>
    public required bool IngestInProgress { get; init; }
}

/// <summary>
/// FR-41's rule-coverage bar (mockup parity item #15). Release 1 shipped exactly one value: rule
/// extraction (FR-26) and rule-set versioning (FR-27) were Release 2, so the bar could not be
/// populated yet. Both have since landed (S-19/S-20/S-22/S-25), and
/// <see cref="Rules.RulesInventoryClassifier"/> (<c>AecoPostMortem.Api</c>) already computes the
/// exact four-way breakdown FR-40 defines (<see cref="Rules.RulesInventoryStatusCounts"/>). Rather
/// than add a bare second enum member — which cannot carry four numbers — this is a closed record
/// hierarchy behind a private constructor, the same "a designed 'not yet' state vs. a real value
/// with data" shape <c>SessionTokenFigures</c>'s own <c>Observed</c>/<c>SessionTotalsNotRecorded</c>
/// split establishes (this file's "`SessionTokenFigures` is not a `Finding`, deliberately" remarks).
/// </summary>
public abstract record RuleCoverageStatus
{
    private RuleCoverageStatus()
    {
    }

    /// <summary>No rule-set-version coverage figure was resolved for this masthead — an empty store,
    /// or no session in the selected repository carrying a rule set at all. The same "not yet" state
    /// Release 1 shipped alone.</summary>
    public static RuleCoverageStatus NotYetAnalyzed { get; } = new NotYetAnalyzedStatus();

    /// <summary>The real four-way breakdown for the rule-set version the caller resolved —
    /// <paramref name="counts"/> is the identical <see cref="Rules.RulesInventoryStatusCounts"/> the
    /// Rules Inventory itself serves for that version (<c>Rules.RulesInventory.StatusCounts</c>), never
    /// a second computation of the same figure.</summary>
    public static RuleCoverageStatus Analyzed(RulesInventoryStatusCounts counts) =>
        new AnalyzedStatus { Counts = counts };

    public sealed record NotYetAnalyzedStatus : RuleCoverageStatus;

    public sealed record AnalyzedStatus : RuleCoverageStatus
    {
        public required RulesInventoryStatusCounts Counts { get; init; }
    }
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
/// <item><see cref="NothingInScope"/> — the analysis scope itself held no sessions
/// (<see cref="CheckRegistry.SessionsInScope"/> is zero), so nothing was looked at. Distinct from
/// <see cref="Analyzed"/> with no findings, which says every check ran over real sessions and found
/// nothing — the clean-versus-never-looked conflation PRD §3.9 names. Takes precedence over
/// <see cref="Analyzed"/> and <see cref="NotYetAnalyzed"/> alike: every check orchestrator sets
/// <see cref="CheckRunStatus.Ran"/> unconditionally, so an empty scope always produces a registry
/// full of Ran entries and would otherwise be indistinguishable from a clean corpus.</item>
/// <item><see cref="Analyzed"/> — ingestion is complete, the scope held real sessions, and at least
/// one check has run. <see cref="ProcessDigest.RankedFindings"/> is the final answer, even when it is
/// empty.</item>
/// </list>
/// </summary>
public enum DigestState
{
    NotYetAnalyzed,
    Incomplete,
    Analyzed,

    /// <summary>Declared last so the three values that existed before it keep their ordinals — this
    /// enum is serialised by name (<c>Api.DigestEnvelope.State</c>), so nothing on the wire depends
    /// on the order, but a stored or logged ordinal elsewhere would.</summary>
    NothingInScope,
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

    /// <summary>FR-42's "checks that found nothing" surface (issue #46): the exact
    /// <see cref="CheckRegistry"/> <see cref="Build"/> already received — used there to compute
    /// <see cref="State"/> and carried through here unchanged, the same already-resolved-plain-input
    /// pattern <see cref="Masthead.Counters"/> and <see cref="RepositoryScope"/> follow. A caller
    /// (<c>AecoPostMortem.Api.SilentCheckEnvelope.From</c>) filters this down to the clean entries;
    /// nothing here re-filters it.</summary>
    public required CheckRegistry CheckRegistry { get; init; }

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
    /// already exist in the store, ranked by <see cref="SessionsAffected"/>.
    /// <paramref name="ruleCoverage"/> (mockup parity item #15) defaults to
    /// <see langword="null"/>, coalesced to <see cref="RuleCoverageStatus.NotYetAnalyzed"/> — the same
    /// "every existing call site that supplies fewer arguments still compiles" precedent
    /// <c>SessionEnvelope.From</c>'s own optional <c>lanes</c> parameter and
    /// <c>RulesInventoryEnvelope.From</c>'s own optional <c>violationCounts</c> parameter both set
    /// (<c>Api/CLAUDE.md</c>).</summary>
    public static ProcessDigest Build(
        MastheadCounters counters,
        CheckRegistry checkRegistry,
        IReadOnlyList<Finding> findings,
        RepositoryScope repositoryScope,
        RuleCoverageStatus? ruleCoverage = null)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(checkRegistry);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(repositoryScope);

        // Ordered by "the more urgent, more specific claim wins" — the same precedence rule this
        // type already applied to Incomplete over NotYetAnalyzed. Incomplete stays first: while
        // ingestion is running, an empty scope may simply not have been filled yet, so nothing about
        // it can be stated as final. NothingInScope then sits above the Ran check deliberately —
        // every orchestrator registers Ran unconditionally, so an empty scope reaches here with a
        // registry full of Ran entries and would otherwise read Analyzed.
        var state = counters.IngestInProgress
            ? DigestState.Incomplete
            : checkRegistry.SessionsInScope == 0
                ? DigestState.NothingInScope
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
                RuleCoverage = ruleCoverage ?? RuleCoverageStatus.NotYetAnalyzed,
                RepositoryScope = repositoryScope,
            },
            State = state,
            RankedFindings = ranked,
            InferredFindings = inferred,
            CheckRegistry = checkRegistry,
        };
    }

    /// <summary>How many distinct sessions a finding touched — the ranking key FR-41 names (§5.1's
    /// primary metric, FR-57's recurrence key made concrete).</summary>
    public static int SessionsAffected(Finding finding) =>
        finding.Recurrence.Occurrences.Select(occurrence => occurrence.SessionId).Distinct().Count();
}
