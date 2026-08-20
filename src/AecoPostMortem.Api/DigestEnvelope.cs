using System.Text.Json.Serialization;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-41 part 2 (S-54)'s served repository scope — the wire shape for
/// <see cref="Findings.RepositoryScope"/>. PRD Part 8 Q5: the digest shows one repository at a time
/// by default; <see cref="AvailableRepositories"/> is the seam a later cross-repository view
/// switches through, not that view itself.
/// </summary>
public sealed record RepositoryScopeEnvelope
{
    public required string? SelectedRepository { get; init; }

    public required IReadOnlyList<string> AvailableRepositories { get; init; }

    public static RepositoryScopeEnvelope From(RepositoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new RepositoryScopeEnvelope
        {
            SelectedRepository = scope.SelectedRepository,
            AvailableRepositories = scope.AvailableRepositories,
        };
    }
}

/// <summary>
/// FR-41's served masthead — the wire shape for <see cref="Findings.Masthead"/>. Enum members
/// serialise as their name (<see cref="JsonStringEnumConverter"/>) so a client reads
/// <c>"NotYetAnalyzed"</c> rather than an opaque ordinal for a state whose entire point is to be
/// stated in words (edge case, Scenario 5 of S-36).
/// </summary>
public sealed record MastheadEnvelope
{
    public required int SessionCount { get; init; }

    public required DateTimeOffset? SpanStart { get; init; }

    public required DateTimeOffset? SpanEnd { get; init; }

    public required int RepositoryCount { get; init; }

    public required long EventCount { get; init; }

    public required long ToolCallCount { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required RuleCoverageStatus RuleCoverage { get; init; }

    public required RepositoryScopeEnvelope RepositoryScope { get; init; }

    public static MastheadEnvelope From(Masthead masthead)
    {
        ArgumentNullException.ThrowIfNull(masthead);

        return new MastheadEnvelope
        {
            SessionCount = masthead.Counters.SessionCount,
            SpanStart = masthead.Counters.SpanStart,
            SpanEnd = masthead.Counters.SpanEnd,
            RepositoryCount = masthead.Counters.RepositoryCount,
            EventCount = masthead.Counters.EventCount,
            ToolCallCount = masthead.Counters.ToolCallCount,
            RuleCoverage = masthead.RuleCoverage,
            RepositoryScope = RepositoryScopeEnvelope.From(masthead.RepositoryScope),
        };
    }
}

/// <summary>
/// FR-41's served digest (S-36, issue #44): the masthead plus every finding, already ranked by
/// sessions affected — <see cref="ProcessDigest.Build"/> does the ranking and decides
/// <see cref="Findings.DigestState"/>; this type is the wire shape a client reads it through, the
/// same layering <see cref="FindingEnvelope"/> and <see cref="SuggestionEnvelope"/> already
/// establish (S-50, issue #13).
/// </summary>
public sealed record DigestEnvelope
{
    public required MastheadEnvelope Masthead { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required DigestState State { get; init; }

    public required IReadOnlyList<FindingEnvelope> RankedFindings { get; init; }

    /// <summary><paramref name="mapFinding"/> is supplied by the caller rather than assumed to be
    /// <see cref="FindingEnvelope.From"/>: an adherence finding must go through
    /// <see cref="FindingEnvelope.FromAdherence"/> with its resolution and rule version instead
    /// (FR-33), and this type has no way to know which shape a given <c>Finding</c> needs — only the
    /// caller, which already has the resolution, does. <see cref="ProcessDigest.RankedFindings"/>'s
    /// order is preserved: the ranking already happened, this only maps each entry to its wire
    /// shape.</summary>
    public static DigestEnvelope From(ProcessDigest digest, Func<Finding, FindingEnvelope> mapFinding)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(mapFinding);

        return new DigestEnvelope
        {
            Masthead = MastheadEnvelope.From(digest.Masthead),
            State = digest.State,
            RankedFindings = digest.RankedFindings.Select(mapFinding).ToList(),
        };
    }
}
