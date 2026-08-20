using System.Text.Json.Serialization;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-40's four statuses on the wire (S-22, issue #35). A closed polymorphic hierarchy behind a
/// private constructor, the same mechanism <see cref="SuggestionEnvelope"/> uses: the discriminator
/// tells a client which status applies without inspecting which optional fields happen to be
/// present, and only <see cref="NotCheckableStatus"/> has a <c>Reason</c> at all — serving a
/// "Not checkable" status without its reason is a compile error here, not a review comment.
/// <see cref="Label"/> rides alongside the discriminator for the reason
/// <c>Findings.ProvenanceLabel</c> gives for its own: the fixed wording survives being quoted out of
/// this surface's styling, the discriminator does not.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(WatchedStatus), "watched")]
[JsonDerivedType(typeof(CheckableNotYetBuiltStatus), "checkableNotYetBuilt")]
[JsonDerivedType(typeof(NotCheckableStatus), "notCheckable")]
[JsonDerivedType(typeof(NotARuleStatus), "notARule")]
public abstract record RuleStatementStatusEnvelope
{
    private RuleStatementStatusEnvelope()
    {
    }

    public abstract string Label { get; }

    /// <summary>Maps the domain's own closed four-shape status onto this contract's four. The
    /// <c>switch</c> has no default arm on purpose: adding a fifth domain shape breaks this
    /// expression rather than silently serialising it as something else.</summary>
    public static RuleStatementStatusEnvelope Of(RuleStatementStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return status switch
        {
            RuleStatementStatus.WatchedStatus => new WatchedStatus(),
            RuleStatementStatus.CheckableNotYetBuiltStatus => new CheckableNotYetBuiltStatus(),
            RuleStatementStatus.NotCheckableStatus notCheckable =>
                new NotCheckableStatus { Reason = notCheckable.Reason },
            RuleStatementStatus.NotARuleStatus => new NotARuleStatus(),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown rule status."),
        };
    }

    public sealed record WatchedStatus : RuleStatementStatusEnvelope
    {
        public override string Label => RuleStatementStatus.Labels.Watched;
    }

    public sealed record CheckableNotYetBuiltStatus : RuleStatementStatusEnvelope
    {
        public override string Label => RuleStatementStatus.Labels.CheckableNotYetBuilt;
    }

    public sealed record NotCheckableStatus : RuleStatementStatusEnvelope
    {
        public required string Reason { get; init; }

        // The constant, not RuleStatementStatus.NotCheckable(Reason).Label: this getter runs during
        // serialisation, and building a domain status to ask it for a fixed string would allocate
        // per row and throw out of a property getter mid-serialise for an envelope deserialised with
        // an empty reason.
        public override string Label => RuleStatementStatus.Labels.NotCheckable;
    }

    public sealed record NotARuleStatus : RuleStatementStatusEnvelope
    {
        public override string Label => RuleStatementStatus.Labels.NotARule;
    }
}

/// <summary>
/// FR-40's retirement state on the wire — two explicit shapes rather than a nullable date, so
/// "still in force" and "we forgot to set the date" cannot serialise identically. The same reasoning
/// <see cref="SuggestionEnvelope"/> gives for making "no suggestion" a value.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(StillInForce), "inForce")]
[JsonDerivedType(typeof(RetiredRule), "retired")]
public abstract record RuleRetirementEnvelope
{
    private RuleRetirementEnvelope()
    {
    }

    public static RuleRetirementEnvelope Of(RuleRetirement retirement)
    {
        ArgumentNullException.ThrowIfNull(retirement);

        return retirement switch
        {
            RuleRetirement.StillInForce => new StillInForce(),
            RuleRetirement.RetiredRule retired => new RetiredRule { RetiredAt = retired.RetiredAt },
            _ => throw new ArgumentOutOfRangeException(
                nameof(retirement), retirement, "Unknown retirement state."),
        };
    }

    public sealed record StillInForce : RuleRetirementEnvelope;

    public sealed record RetiredRule : RuleRetirementEnvelope
    {
        public required string RetiredAt { get; init; }
    }
}

/// <summary>FR-27's version identity and window on the wire — served for the selected version and for
/// each one a client may switch to, never as a container of a second version's statements.</summary>
public sealed record RuleSetVersionEnvelope
{
    public required string? Repository { get; init; }

    public required string Hash { get; init; }

    public required string FirstSessionId { get; init; }

    public required string LastSessionId { get; init; }

    public required int SessionCount { get; init; }

    public static RuleSetVersionEnvelope From(RuleSetVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new RuleSetVersionEnvelope
        {
            Repository = version.Repository,
            Hash = version.Hash,
            FirstSessionId = version.FirstSessionId,
            LastSessionId = version.LastSessionId,
            SessionCount = version.SessionCount,
        };
    }
}

/// <summary>One inventory row on the wire: FR-40's origin, reach, window, status and retirement.</summary>
public sealed record RulesInventoryRowEnvelope
{
    public required string SourceFile { get; init; }

    public required string Text { get; init; }

    public required RuleStatementStatusEnvelope Status { get; init; }

    public required IReadOnlyList<string> SessionIds { get; init; }

    public required string InForceFrom { get; init; }

    public required string InForceUntil { get; init; }

    public required RuleRetirementEnvelope Retirement { get; init; }

    /// <summary>Scenario 5's frozen adherence date, copied from
    /// <see cref="RulesInventoryRow.AdherenceFrozenAt"/> — itself computed from
    /// <see cref="RulesInventoryRow.Retirement"/>, so the two fields served here cannot
    /// disagree.</summary>
    public required string? AdherenceFrozenAt { get; init; }

    public static RulesInventoryRowEnvelope From(RulesInventoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new RulesInventoryRowEnvelope
        {
            SourceFile = row.SourceFile,
            Text = row.Statement.Text,
            Status = RuleStatementStatusEnvelope.Of(row.Status),
            SessionIds = row.SessionIds,
            InForceFrom = row.InForceFrom,
            InForceUntil = row.InForceUntil,
            Retirement = RuleRetirementEnvelope.Of(row.Retirement),
            AdherenceFrozenAt = row.AdherenceFrozenAt,
        };
    }
}

/// <summary>FR-40's status breakdown on the wire — the measured 4 / 9 / 9 / 21 — served rather than
/// left for a client to recount, so every surface quoting a coverage figure quotes the same one.
/// </summary>
public sealed record RulesInventoryStatusCountsEnvelope
{
    public required int Watched { get; init; }

    public required int CheckableNotYetBuilt { get; init; }

    public required int NotCheckable { get; init; }

    public required int NotARule { get; init; }

    public required int Total { get; init; }

    public static RulesInventoryStatusCountsEnvelope From(RulesInventoryStatusCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return new RulesInventoryStatusCountsEnvelope
        {
            Watched = counts.Watched,
            CheckableNotYetBuilt = counts.CheckableNotYetBuilt,
            NotCheckable = counts.NotCheckable,
            NotARule = counts.NotARule,
            Total = counts.Total,
        };
    }
}

/// <summary>
/// FR-40's served inventory (S-22, issue #35): one rule-set version's statements, each with exactly
/// one status, its origin, its reach and its in-force window — plus the versions a client may switch
/// to. <see cref="AvailableVersions"/> carries identities and windows only, never a second version's
/// rows: there is no shape in this contract that could express a union across versions, which is
/// what PRD Part 4 rules out (a measured 34 of 43 statements are absent from the most recent
/// session, so a union would render statements as in force that were removed weeks earlier).
/// </summary>
public sealed record RulesInventoryEnvelope
{
    public required RuleSetVersionEnvelope SelectedVersion { get; init; }

    public required IReadOnlyList<RuleSetVersionEnvelope> AvailableVersions { get; init; }

    /// <summary>Serialised as its own name (<c>"NoInstructionBlocks"</c>) rather than an ordinal, the
    /// same choice <see cref="DigestEnvelope.State"/> makes for a state whose entire point is to be
    /// stated in words.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required RulesInventoryState State { get; init; }

    public required IReadOnlyList<RulesInventoryRowEnvelope> Rows { get; init; }

    public required RulesInventoryStatusCountsEnvelope StatusCounts { get; init; }

    public static RulesInventoryEnvelope From(RulesInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var available = inventory.AvailableVersions;

        // Build only produces an inventory for a version the corpus actually carried, so the selected
        // identity is always one of the computed versions — this look-up joins the identity the
        // inventory is scoped by to that version's own window and sample size, which is what makes
        // Scenario 6's "names which" a full statement rather than a bare hash.
        var selected = available.Single(version => version.Id == inventory.SelectedVersion);

        return new RulesInventoryEnvelope
        {
            SelectedVersion = RuleSetVersionEnvelope.From(selected),
            AvailableVersions = available.Select(RuleSetVersionEnvelope.From).ToList(),
            State = inventory.State,
            Rows = inventory.Rows.Select(RulesInventoryRowEnvelope.From).ToList(),
            StatusCounts = RulesInventoryStatusCountsEnvelope.From(inventory.StatusCounts),
        };
    }
}
