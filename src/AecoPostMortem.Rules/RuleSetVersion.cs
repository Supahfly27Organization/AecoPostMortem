namespace AecoPostMortem.Rules;

/// <summary>
/// One session's own rule set: which repository it ran in, when it started (for the chronological
/// order FR-27's window is built from) and the instruction blocks its own system prompt(s) carried.
/// Plain input — the same discipline every check in this project follows (Repo Rule 6): the caller
/// resolves a session's repository and blocks and hands the shape in.
/// </summary>
public sealed record SessionRuleSet
{
    public required string SessionId { get; init; }

    public required string? Repository { get; init; }

    public required string StartedAt { get; init; }

    public required IReadOnlyList<InstructionBlock> Blocks { get; init; }
}

/// <summary>
/// A rule-set version's identity (FR-27): the repository it was in force for, and the content hash
/// of the block set that identifies it — order-insensitive (PRD Part 8 Q4: whether block ordering is
/// stable across sessions was not measured). Two sessions with this same identity carried an
/// identical block set; two sessions with a different one did not, even if every other session
/// field matches. <see cref="RuleSetVersionHasher"/> produces <see cref="Hash"/>.
/// </summary>
public sealed record RuleSetVersionId
{
    public required string? Repository { get; init; }

    public required string Hash { get; init; }
}

/// <summary>
/// One rule-set version (FR-27): every session in <see cref="RuleSetVersionId.Repository"/> that
/// carried the identical block set <see cref="RuleSetVersionId.Hash"/> identifies. The window is
/// stated as the first and last session that carried it, in that repository's own chronological
/// order — never a date range computed independently of the sessions themselves.
/// <see cref="SessionCount"/> renders alongside every figure: a measured 6 versions over 32 days
/// across 25 sessions in one repository means a version's own sample is often small.
/// </summary>
public sealed record RuleSetVersion
{
    public required RuleSetVersionId Id { get; init; }

    public required string FirstSessionId { get; init; }

    public required string LastSessionId { get; init; }

    public required int SessionCount { get; init; }

    public string? Repository => Id.Repository;

    public string Hash => Id.Hash;
}
