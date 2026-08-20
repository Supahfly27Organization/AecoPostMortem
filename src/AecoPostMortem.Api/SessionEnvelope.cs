using System.Text.Json.Serialization;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-24 in the response contract: the wire shape for <see cref="SessionTokenFigures"/>. Closed to
/// exactly two shapes through the private constructor, the same reasoning
/// <see cref="SuggestionEnvelope"/> gives for its own absent state — "context size at end" must
/// serialise as an explicit discriminated value even when a session's shutdown event never
/// recorded one, never a missing or nullable field a client could read as zero.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Observed), "observed")]
[JsonDerivedType(typeof(NotRecorded), "notRecorded")]
public abstract record SessionTokenFiguresEnvelope
{
    private SessionTokenFiguresEnvelope()
    {
    }

    /// <summary>The one value representing "this session's shutdown event carried no token
    /// metrics".</summary>
    public static SessionTokenFiguresEnvelope NotRecordedValue { get; } = new NotRecorded();

    public static SessionTokenFiguresEnvelope From(SessionTokenFigures figures)
    {
        ArgumentNullException.ThrowIfNull(figures);

        return figures switch
        {
            SessionTokenFigures.Observed observed => new Observed
            {
                InputTokens = observed.InputTokens,
                OutputTokens = observed.OutputTokens,
                CacheReadTokens = observed.CacheReadTokens,
                CacheWriteTokens = observed.CacheWriteTokens,
                ReasoningTokens = observed.ReasoningTokens,
                ModelCount = observed.ModelCount,
            },
            SessionTokenFigures.SessionTotalsNotRecorded => NotRecordedValue,
            _ => throw new ArgumentOutOfRangeException(
                nameof(figures), figures, "Unknown SessionTokenFigures shape."),
        };
    }

    public sealed record Observed : SessionTokenFiguresEnvelope
    {
        public required long InputTokens { get; init; }

        public required long OutputTokens { get; init; }

        public long? CacheReadTokens { get; init; }

        public long? CacheWriteTokens { get; init; }

        public long? ReasoningTokens { get; init; }

        public int? ModelCount { get; init; }
    }

    public sealed record NotRecorded : SessionTokenFiguresEnvelope;
}

/// <summary>The wire shape for <see cref="SessionMasthead"/>. <see cref="ElapsedMs"/> carries
/// milliseconds rather than the domain's <see cref="TimeSpan"/> directly — a plain number needs no
/// format-string agreement with the client the way a serialised <c>TimeSpan</c> would.</summary>
public sealed record SessionMastheadEnvelope
{
    public required string SessionId { get; init; }

    public string? Repository { get; init; }

    public string? Branch { get; init; }

    public required string CopilotVersion { get; init; }

    public long? ElapsedMs { get; init; }

    public required int TurnCount { get; init; }

    public required int ToolCallCount { get; init; }

    public required int SubagentCount { get; init; }

    public required int SkillCount { get; init; }

    public int? ModelCount { get; init; }

    public required SessionTokenFiguresEnvelope ContextSize { get; init; }

    public static SessionMastheadEnvelope From(SessionMasthead masthead)
    {
        ArgumentNullException.ThrowIfNull(masthead);

        return new SessionMastheadEnvelope
        {
            SessionId = masthead.SessionId,
            Repository = masthead.Repository,
            Branch = masthead.Branch,
            CopilotVersion = masthead.CopilotVersion,
            ElapsedMs = masthead.Elapsed is { } elapsed ? (long)elapsed.TotalMilliseconds : null,
            TurnCount = masthead.TurnCount,
            ToolCallCount = masthead.ToolCallCount,
            SubagentCount = masthead.SubagentCount,
            SkillCount = masthead.SkillCount,
            ModelCount = masthead.ModelCount,
            ContextSize = SessionTokenFiguresEnvelope.From(masthead.ContextSize),
        };
    }
}

/// <summary>The wire shape for one <see cref="SessionTapeStep"/>. <see cref="OwnerKind"/> is
/// reused verbatim from <c>Data.Execution</c> rather than re-declared here — the global
/// <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c> <see cref="ApiHost.Build"/> registers
/// already gives it and <see cref="Kind"/> the same camelCase wire form <see cref="AppStateKind"/>
/// gets, with no per-property override needed.</summary>
public sealed record SessionTapeStepEnvelope
{
    public required SessionTapeStepKind Kind { get; init; }

    public required string StepId { get; init; }

    public required string Label { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required long OffsetMs { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }

    public static SessionTapeStepEnvelope From(SessionTapeStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new SessionTapeStepEnvelope
        {
            Kind = step.Kind,
            StepId = step.StepId,
            Label = step.Label,
            Timestamp = step.Timestamp,
            OffsetMs = (long)step.Offset.TotalMilliseconds,
            OwnerKind = step.OwnerKind,
            AgentId = step.AgentId,
        };
    }
}

/// <summary>The wire shape for <see cref="SessionRecordingStatus"/> (FR-21 part 3 of 3, S-53,
/// issue #17). A closed three-shape union behind a private constructor, the same mechanism
/// <see cref="SessionTokenFiguresEnvelope"/> and <see cref="SuggestionEnvelope"/> already use — so a
/// client reads which of the three states applies from the <c>"kind"</c> discriminator rather than
/// inferring it from which optional fields happen to be present.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Complete), "complete")]
[JsonDerivedType(typeof(IngestIncomplete), "ingestIncomplete")]
[JsonDerivedType(typeof(ReconstructionFailed), "reconstructionFailed")]
public abstract record SessionRecordingStatusEnvelope
{
    private SessionRecordingStatusEnvelope()
    {
    }

    public static SessionRecordingStatusEnvelope CompleteValue { get; } = new Complete();

    public static SessionRecordingStatusEnvelope IngestIncompleteValue { get; } = new IngestIncomplete();

    public static SessionRecordingStatusEnvelope From(SessionRecordingStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return status switch
        {
            SessionRecordingStatus.Complete => CompleteValue,
            SessionRecordingStatus.IngestIncomplete => IngestIncompleteValue,
            SessionRecordingStatus.ReconstructionFailed failed => new ReconstructionFailed { Skipped = failed.Skipped },
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown SessionRecordingStatus shape."),
        };
    }

    public sealed record Complete : SessionRecordingStatusEnvelope;

    public sealed record IngestIncomplete : SessionRecordingStatusEnvelope;

    public sealed record ReconstructionFailed : SessionRecordingStatusEnvelope
    {
        public required IReadOnlyList<string> Skipped { get; init; }
    }
}

/// <summary>The wire shape for one <see cref="SessionFindingChip"/> (FR-21 part 2 of 3, S-52, issue
/// #16): the finding itself, already mapped to its <see cref="FindingEnvelope"/> shape, plus how
/// many sessions across the corpus it affects — the chip row's own "with its count" (the story's own
/// Gherkin wording).</summary>
public sealed record SessionFindingChipEnvelope
{
    public required FindingEnvelope Finding { get; init; }

    public required int SessionsAffected { get; init; }

    /// <summary><paramref name="mapFinding"/> is supplied by the caller for the same reason
    /// <see cref="DigestEnvelope.From"/> takes one: only the caller knows whether a given
    /// <see cref="Finding"/> needs <see cref="FindingEnvelope.FromAdherence"/>'s resolution and rule
    /// version instead of the bare <see cref="FindingEnvelope.From"/> shape (FR-33).</summary>
    public static SessionFindingChipEnvelope From(SessionFindingChip chip, Func<Finding, FindingEnvelope> mapFinding)
    {
        ArgumentNullException.ThrowIfNull(chip);
        ArgumentNullException.ThrowIfNull(mapFinding);

        return new SessionFindingChipEnvelope
        {
            Finding = mapFinding(chip.Finding),
            SessionsAffected = chip.SessionsAffected,
        };
    }
}

/// <summary>
/// FR-21's served masthead and tape (S-08, issue #15), FR-21 part 2 of 3's finding chip row (S-52,
/// issue #16), and FR-21 part 3 of 3's finality state (S-53, issue #17): the wire shape a client
/// reads <see cref="SessionRecording"/> and <see cref="SessionFindings"/> through, the same layering
/// <see cref="DigestEnvelope"/> already establishes for <see cref="ProcessDigest"/> (S-36).
/// <see cref="Status"/> carries FR-21 part 3 of 3's finality state alongside the masthead, steps and
/// chips — a client checks it before rendering the tape as the session's final picture.
/// </summary>
public sealed record SessionEnvelope
{
    public required SessionMastheadEnvelope Masthead { get; init; }

    public required IReadOnlyList<SessionTapeStepEnvelope> Steps { get; init; }

    public required SessionRecordingStatusEnvelope Status { get; init; }

    /// <summary>FR-21 part 2 of 3, Scenario 3: "a chip row states each finding affecting this
    /// session with its count." An empty list is itself the designed "no findings affect this
    /// session" state, not a missing field — the client renders it explicitly rather than as a blank
    /// area (see `web/CLAUDE.md`).</summary>
    public required IReadOnlyList<SessionFindingChipEnvelope> Findings { get; init; }

    public static SessionEnvelope From(
        SessionRecording recording, SessionFindings findings, Func<Finding, FindingEnvelope> mapFinding)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(mapFinding);

        return new SessionEnvelope
        {
            Masthead = SessionMastheadEnvelope.From(recording.Masthead),
            Steps = recording.Tape.Steps.Select(SessionTapeStepEnvelope.From).ToList(),
            Status = SessionRecordingStatusEnvelope.From(recording.Status),
            Findings = findings.Chips.Select(chip => SessionFindingChipEnvelope.From(chip, mapFinding)).ToList(),
        };
    }
}
