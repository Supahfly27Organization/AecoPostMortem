namespace AecoPostMortem.Rules;

/// <summary>
/// FR-40's four statuses (S-22, issue #35), closed to exactly these four shapes: the private
/// constructor means only the nested records — the only types that can see it — can derive from this
/// one, the same mechanism <c>AecoPostMortem.Api.SuggestionEnvelope</c> uses. "Exactly one status"
/// (Scenario 1) is therefore a property of the type, not of a caller remembering to set one field
/// and clear three others.
/// </summary>
public abstract record RuleStatementStatus
{
    private RuleStatementStatus()
    {
    }

    /// <summary>The fixed FR-40 wording for this status. <see cref="NotCheckableStatus"/> states only
    /// "Not checkable" here — its reason is a separate field, so a surface can render the two
    /// distinguishably rather than concatenating them into one unparseable string.</summary>
    public abstract string Label { get; }

    /// <summary>A statement a built check shape actually watches — a measured 4 of 43.</summary>
    public static RuleStatementStatus Watched { get; } = new WatchedStatus();

    /// <summary>A statement a check shape could express, but none has been built for — a measured 9
    /// of 43.</summary>
    public static RuleStatementStatus CheckableNotYetBuilt { get; } = new CheckableNotYetBuiltStatus();

    /// <summary>A statement no check shape can express, with the reason stated — a measured 9 of 43.
    /// FR-40 names the reason as part of the status, which is why there is no parameterless way to
    /// reach this one.</summary>
    public static RuleStatementStatus NotCheckable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new NotCheckableStatus { Reason = reason };
    }

    /// <summary>An extracted list item that is not a rule at all — a heading, an index entry, a note.
    /// The largest bucket at a measured 21 of 43, and deliberately not a failure: FR-40's own point is
    /// that the extraction unit is a list item, so most list items were never going to be rules.
    /// </summary>
    public static RuleStatementStatus NotARule { get; } = new NotARuleStatus();

    /// <summary>FR-40's fixed wording, as constants so the wire contract
    /// (<c>Api.RuleStatementStatusEnvelope</c>) can state the same strings without constructing a
    /// status to ask one for its label.</summary>
    public static class Labels
    {
        public const string Watched = "Watched";
        public const string CheckableNotYetBuilt = "Checkable — not yet built";
        public const string NotCheckable = "Not checkable";
        public const string NotARule = "Not a rule";
    }

    public sealed record WatchedStatus : RuleStatementStatus
    {
        public override string Label => Labels.Watched;
    }

    public sealed record CheckableNotYetBuiltStatus : RuleStatementStatus
    {
        public override string Label => Labels.CheckableNotYetBuilt;
    }

    public sealed record NotCheckableStatus : RuleStatementStatus
    {
        string reason = null!;

        /// <summary>Validated in the accessor rather than only in
        /// <see cref="RuleStatementStatus.NotCheckable"/>: <c>required</c> proves the reason was
        /// assigned, not that it says anything, and an object initialiser or a <c>with</c> expression
        /// reaches this property without going through the factory. FR-40's status is "Not checkable
        /// **with a stated reason**", so a blank one is refused on every path.</summary>
        public required string Reason
        {
            get => reason;
            init
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                reason = value;
            }
        }

        public override string Label => Labels.NotCheckable;
    }

    public sealed record NotARuleStatus : RuleStatementStatus
    {
        public override string Label => Labels.NotARule;
    }
}

/// <summary>
/// Whether a statement is still carried by its repository's most recent rule-set version, or was
/// removed at a stated point (FR-40: "a retired rule stays visible with its adherence frozen at
/// retirement"). Two shapes rather than a nullable date, for the same reason
/// <c>AecoPostMortem.Api.SuggestionEnvelope</c> makes "no suggestion" a value: a null date and a
/// forgotten date look identical, and one of them is a defect.
/// </summary>
public abstract record RuleRetirement
{
    private RuleRetirement()
    {
    }

    /// <summary>The statement is still in the repository's most recent rule-set version.</summary>
    public static RuleRetirement InForce { get; } = new StillInForce();

    /// <summary>The statement is gone from the most recent version, removed as of
    /// <paramref name="retiredAt"/> — the timestamp of the first session in that repository, after
    /// the statement's last carrying session, that no longer carried it.</summary>
    public static RuleRetirement Retired(string retiredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retiredAt);

        return new RetiredRule { RetiredAt = retiredAt };
    }

    public sealed record StillInForce : RuleRetirement;

    public sealed record RetiredRule : RuleRetirement
    {
        string retiredAt = null!;

        /// <summary>Validated in the accessor for the same reason
        /// <see cref="RuleStatementStatus.NotCheckableStatus.Reason"/> is: a retired rule whose
        /// removal date is blank is a rule whose adherence is frozen at nothing.</summary>
        public required string RetiredAt
        {
            get => retiredAt;
            init
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                retiredAt = value;
            }
        }
    }
}

/// <summary>
/// One row of FR-40's inventory: the statement itself, its one status, every session in the selected
/// rule-set version that carried it, that version's own in-force window for it, and whether it has
/// since been retired.
/// </summary>
public sealed record RulesInventoryRow
{
    public required RuleStatement Statement { get; init; }

    public required RuleStatementStatus Status { get; init; }

    /// <summary>Scenario 2's "the sessions carrying it" — every session in the selected version whose
    /// own prompt carried this statement, never a count alone.</summary>
    public required IReadOnlyList<string> SessionIds { get; init; }

    /// <summary>Scenario 3's first in-force date: the earliest carrying session's own
    /// <see cref="SessionRuleSet.StartedAt"/>. Carried as the session's own recorded text, not
    /// reparsed into a date type — every temporal ordering in this project derives from event
    /// timestamps compared ordinally (PRD §3.8).</summary>
    public required string InForceFrom { get; init; }

    /// <summary>Scenario 3's last in-force date: the latest carrying session's own
    /// <see cref="SessionRuleSet.StartedAt"/>.</summary>
    public required string InForceUntil { get; init; }

    public required RuleRetirement Retirement { get; init; }

    /// <summary>Scenario 2's "the source file it came from", read off the statement itself rather than
    /// stored a second time here where it could drift.</summary>
    public string SourceFile => Statement.SourceFile;

    /// <summary>Scenario 5's "adherence frozen at the date it was removed", computed from
    /// <see cref="Retirement"/> rather than stored: an in-force statement has no frozen date at all,
    /// and there is no constructor path that could give it one.</summary>
    public string? AdherenceFrozenAt =>
        Retirement is RuleRetirement.RetiredRule retired ? retired.RetiredAt : null;
}

/// <summary>
/// FR-40's status breakdown — the measured 4 / 9 / 9 / 21 on the reference corpus, and the figure
/// PRD §2 says every coverage number derives from. Computed from the rows, never stored beside them
/// (<see cref="RulesInventory.StatusCounts"/> is a getter-only property), so a count cannot disagree
/// with the rows it claims to summarise.
/// </summary>
public sealed record RulesInventoryStatusCounts
{
    public required int Watched { get; init; }

    public required int CheckableNotYetBuilt { get; init; }

    public required int NotCheckable { get; init; }

    public required int NotARule { get; init; }

    public int Total => Watched + CheckableNotYetBuilt + NotCheckable + NotARule;

    public static RulesInventoryStatusCounts Over(IEnumerable<RulesInventoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var materialised = rows as IReadOnlyList<RulesInventoryRow> ?? rows.ToArray();

        return new RulesInventoryStatusCounts
        {
            Watched = materialised.Count(row => row.Status is RuleStatementStatus.WatchedStatus),
            CheckableNotYetBuilt =
                materialised.Count(row => row.Status is RuleStatementStatus.CheckableNotYetBuiltStatus),
            NotCheckable = materialised.Count(row => row.Status is RuleStatementStatus.NotCheckableStatus),
            NotARule = materialised.Count(row => row.Status is RuleStatementStatus.NotARuleStatus),
        };
    }
}

/// <summary>
/// The three states this surface can be in. Scenario 4 asks only that "no rules found" be stated
/// rather than an empty table rendered, but FR-26's own fourth scenario already established that a
/// session carrying no <c>custom_instruction</c> block at all is a different fact from one whose
/// blocks yielded no list item (<see cref="SessionInstructionBlocks.HasInstructionBlocks"/>) — the
/// first is "this repository has no written rules", the second is "it has them, and the extraction
/// unit found none in them," which are different problems with different fixes.
/// </summary>
public enum RulesInventoryState
{
    /// <summary>No session in the selected version carried a <c>custom_instruction</c> block.</summary>
    NoInstructionBlocks,

    /// <summary>Blocks were carried, but none of them yielded a list item.</summary>
    BlocksCarriedNoStatements,

    /// <summary>The selected version carries at least one statement.</summary>
    Listed,
}

/// <summary>
/// FR-40's inventory, scoped to exactly one rule-set version (Scenario 6) and naming which. There is
/// no shape here that could hold more than one version's statements at once: a union across versions
/// is what the digest mockup showed and what PRD Part 4 explicitly rules out, since a measured 34 of
/// 43 statements are absent from the most recent session and a union would render all 43 as though
/// they were all still in force.
/// </summary>
public sealed record RulesInventory
{
    public required RuleSetVersionId SelectedVersion { get; init; }

    /// <summary>Every version of <see cref="RuleSetVersionId.Repository"/>, so a surface can offer the
    /// others without rendering them — the same seam <c>Findings.RepositoryScope</c> gives the digest
    /// for repositories, not a second list of statements.</summary>
    public required IReadOnlyList<RuleSetVersion> AvailableVersions { get; init; }

    public required RulesInventoryState State { get; init; }

    public required IReadOnlyList<RulesInventoryRow> Rows { get; init; }

    public RulesInventoryStatusCounts StatusCounts => RulesInventoryStatusCounts.Over(Rows);

    /// <summary>
    /// Builds the inventory for <paramref name="selectedVersion"/> out of the whole corpus of
    /// <paramref name="sessions"/>. Only sessions in that version's own repository are read at all,
    /// and only the ones carrying its block-set hash contribute rows; the rest of the corpus is used
    /// solely to establish which version is the most recent (for retirement) and which versions exist
    /// (for <see cref="AvailableVersions"/>).
    /// </summary>
    /// <param name="classify">Supplies each statement's one status. Kept a caller-supplied function
    /// for the same reason <c>Api.DigestEnvelope.From</c> takes its finding mapper: deciding whether a
    /// statement matches a built check shape is S-25's catalogue work (FR-34), which this project does
    /// not yet carry — and when it does, only the function passed in here changes.</param>
    /// <exception cref="UnknownRuleSetVersionException">No session in the corpus carried
    /// <paramref name="selectedVersion"/>. A version that was never in force has no inventory, which
    /// is a different fact from a version whose sessions carried no rules.</exception>
    public static RulesInventory Build(
        IEnumerable<SessionRuleSet> sessions,
        RuleSetVersionId selectedVersion,
        Func<RuleStatement, RuleStatementStatus> classify)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(selectedVersion);
        ArgumentNullException.ThrowIfNull(classify);

        // Chronological, with the session id breaking ties — the identical ordering
        // RuleSetVersioning.Compute uses, so "the most recent session" means the same thing in both.
        var inRepository = sessions
            .Where(session => string.Equals(
                session.Repository, selectedVersion.Repository, StringComparison.Ordinal))
            .OrderBy(session => session.StartedAt, StringComparer.Ordinal)
            .ThenBy(session => session.SessionId, StringComparer.Ordinal)
            .Select(session => (Session: session, Hash: RuleSetVersionHasher.ComputeHash(session.Blocks)))
            .ToArray();

        var carrying = inRepository
            .Where(entry => string.Equals(entry.Hash, selectedVersion.Hash, StringComparison.Ordinal))
            .ToArray();

        if (carrying.Length == 0)
        {
            throw new UnknownRuleSetVersionException(selectedVersion);
        }

        // Which statements each session carried, in the same chronological order as inRepository.
        // Retirement is a property of a statement, not of the version it is being viewed through, so
        // it has to be answered against every session's own block set — see RetirementOf.
        var statementsPerSession = inRepository
            .Select(entry => entry.Session.Blocks
                .SelectMany(block => block.Statements)
                .Select(statement => (statement.SourceFile, statement.Text))
                .ToHashSet())
            .ToArray();

        var occurrences = RuleStatementDeduplication.Deduplicate(carrying.Select(entry =>
            new SessionInstructionBlocks
            {
                SessionId = entry.Session.SessionId,
                Blocks = entry.Session.Blocks,
            }));

        // TryAdd rather than ToDictionary: RuleStatementDeduplication already tolerates one session
        // contributing twice, and this is the only step that would otherwise throw an opaque
        // duplicate-key error on the same corpus. `carrying` is chronological, so the first entry
        // wins and a repeated session id keeps its earliest start.
        var startedAt = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in carrying)
        {
            startedAt.TryAdd(entry.Session.SessionId, entry.Session.StartedAt);
        }

        var rows = occurrences
            .Select(occurrence => ToRow(occurrence, startedAt, inRepository, statementsPerSession, classify))
            .ToArray();

        return new RulesInventory
        {
            SelectedVersion = selectedVersion,
            AvailableVersions = ChronologicalVersions(inRepository),
            State = StateOf(rows, carrying),
            Rows = rows,
        };
    }

    /// <summary>
    /// The version <paramref name="repository"/>'s chronologically last session carried, or
    /// <c>null</c> when the corpus has no session for that repository at all. This is the version a
    /// surface opens on: FR-40's retirement rule is stated against "the most recent rule-set version",
    /// so the default view is the one in which nothing is retired.
    /// </summary>
    public static RuleSetVersionId? MostRecentVersion(
        IEnumerable<SessionRuleSet> sessions, string? repository)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var latest = sessions
            .Where(session => string.Equals(session.Repository, repository, StringComparison.Ordinal))
            .OrderBy(session => session.StartedAt, StringComparer.Ordinal)
            .ThenBy(session => session.SessionId, StringComparer.Ordinal)
            .LastOrDefault();

        return latest is null
            ? null
            : new RuleSetVersionId
            {
                Repository = latest.Repository,
                Hash = RuleSetVersionHasher.ComputeHash(latest.Blocks),
            };
    }

    /// <summary>
    /// <see cref="RuleSetVersioning.Compute"/>'s own order is chronological by
    /// <see cref="RuleSetVersion.FirstSessionStartedAt"/>, so a version picker built from it already
    /// puts the version whose own window reaches latest in time last — this is a thin, named
    /// pass-through rather than a re-derivation, kept so a reader here does not have to look up
    /// <see cref="RuleSetVersioning.Compute"/> to know its result is already in the order this
    /// surface needs. This is not quite a guarantee that the last entry is always
    /// <see cref="MostRecentVersion"/>'s own answer: a hash that reappears after an intervening edit
    /// (this file's own remarks on <see cref="RuleSetVersioning.Compute"/>'s single-window-per-hash
    /// choice) keeps its *first* appearance's position here, even though it is also the version the
    /// repository's most recent session carries. Not observed in the measured corpus, and not a defect
    /// this surface introduces — the same characteristic the pre-fix ordering also had.
    /// </summary>
    static IReadOnlyList<RuleSetVersion> ChronologicalVersions(
        (SessionRuleSet Session, string Hash)[] inRepository) =>
        RuleSetVersioning.Compute(inRepository.Select(entry => entry.Session));

    static RulesInventoryRow ToRow(
        RuleStatementOccurrence occurrence,
        IReadOnlyDictionary<string, string> startedAt,
        (SessionRuleSet Session, string Hash)[] inRepository,
        HashSet<(string SourceFile, string Text)>[] statementsPerSession,
        Func<RuleStatement, RuleStatementStatus> classify)
    {
        var status = classify(occurrence.Statement)
            ?? throw new InvalidOperationException(
                "Every extracted statement carries exactly one status (FR-40); the classifier "
                + $"returned none for \"{occurrence.Statement.Text}\".");

        var window = occurrence.SessionIds
            .Select(sessionId => startedAt[sessionId])
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new RulesInventoryRow
        {
            Statement = occurrence.Statement,
            Status = status,
            SessionIds = occurrence.SessionIds,
            InForceFrom = window[0],
            InForceUntil = window[^1],
            Retirement = RetirementOf(occurrence.Statement, inRepository, statementsPerSession),
        };
    }

    /// <summary>
    /// Removal is positional in the repository's own chronological order, never a date computed
    /// independently of the sessions: the statement was last carried by some session, and the very
    /// next session that ran is the first evidence it was gone.
    /// <para>
    /// The search is over each session's own block set, not over the row's carrying-session list.
    /// Those are not the same thing: every session sharing a hash carries an identical block set, so
    /// the carrying-session list is the same for every row in the selected version, and searching it
    /// would date every statement's removal to the end of that version — including a statement some
    /// later version went on carrying for weeks.
    /// </para>
    /// </summary>
    static RuleRetirement RetirementOf(
        RuleStatement statement,
        (SessionRuleSet Session, string Hash)[] inRepository,
        HashSet<(string SourceFile, string Text)>[] statementsPerSession)
    {
        var key = (statement.SourceFile, statement.Text);

        if (statementsPerSession[^1].Contains(key))
        {
            return RuleRetirement.InForce;
        }

        // Never -1: this statement came out of the selected version, whose sessions are all in
        // inRepository. Never the final index either, since the final session's set does not contain
        // the key — which is what makes the next index always a real session.
        var lastCarried = Array.FindLastIndex(statementsPerSession, carried => carried.Contains(key));

        return RuleRetirement.Retired(inRepository[lastCarried + 1].Session.StartedAt);
    }

    static RulesInventoryState StateOf(
        IReadOnlyList<RulesInventoryRow> rows, (SessionRuleSet Session, string Hash)[] carrying)
    {
        if (rows.Count > 0)
        {
            return RulesInventoryState.Listed;
        }

        return carrying.Any(entry => entry.Session.Blocks.Count > 0)
            ? RulesInventoryState.BlocksCarriedNoStatements
            : RulesInventoryState.NoInstructionBlocks;
    }
}

/// <summary>
/// Thrown by <see cref="RulesInventory.Build"/> when no session in the corpus carried the requested
/// version. Its own type rather than a bare <see cref="ArgumentException"/> for the same reason
/// <see cref="MixedRuleSetVersionException"/> is: the caller needs to tell "you asked for a version
/// that never existed" apart from "this version's sessions carried no rules", which is a designed,
/// renderable state (<see cref="RulesInventoryState.NoInstructionBlocks"/>) and not an error at all.
/// </summary>
public sealed class UnknownRuleSetVersionException(RuleSetVersionId version)
    : InvalidOperationException("No session in this corpus carried the requested rule-set version.")
{
    public RuleSetVersionId Version { get; } = version;
}
