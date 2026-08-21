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

    /// <summary>Mockup parity item #14: the session's own wall-clock start/end, so a client can
    /// render a real start→end range alongside <see cref="ElapsedMs"/>'s duration.
    /// <see cref="DateTimeOffset"/> is left as-is, the same precedent
    /// <see cref="SessionTapeStepEnvelope.Timestamp"/>'s own remark states — it serialises
    /// losslessly and needs no format agreement of its own, unlike <see cref="ElapsedMs"/>.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary><see langword="null"/> under the identical condition <see cref="ElapsedMs"/> is —
    /// this session never recorded <c>session.shutdown</c>.</summary>
    public DateTimeOffset? EndedAt { get; init; }

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
            StartedAt = masthead.StartedAt,
            EndedAt = masthead.EndedAt,
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

    /// <summary>FR-25 (S-12, issue #21): a <see cref="Findings.SessionTapeStepKind.Skill"/> step's
    /// plugin, carried alongside <see cref="Label"/> (the skill's own name) rather than folded into
    /// it — <see langword="null"/> for every other step kind and for a skill with no plugin
    /// recorded.</summary>
    public string? PluginName { get; init; }

    /// <summary>Paired with <see cref="PluginName"/> — never populated without it.</summary>
    public string? PluginVersion { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required long OffsetMs { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }

    /// <summary>Mockup parity item #17: the specific finding(s) this exact step is unambiguously
    /// *about* — e.g. a failed tool-call row for a finding whose evidence names this exact tool
    /// identity, or a failed hook row for a finding whose evidence is this exact hook's own
    /// success/error fields (<see cref="SessionTapeStepFindingLookup"/>). Empty for the overwhelming
    /// majority of steps: today this covers only the finding shapes named there, deliberately not
    /// every finding class — this is a narrower, step-level fact than
    /// <see cref="SessionEnvelope.Findings"/> (the chip row, "every finding affecting this session").
    /// Defaults to an empty list — the same "empty list is the designed state, never an omission"
    /// discipline <see cref="SessionEnvelope.Findings"/> and <see cref="SessionEnvelope.Lanes"/>
    /// already establish — so every pre-existing call to <see cref="From(SessionTapeStep)"/> still
    /// compiles and still serves an empty list.</summary>
    public required IReadOnlyList<FindingEnvelope> Findings { get; init; }

    public static SessionTapeStepEnvelope From(SessionTapeStep step, IReadOnlyList<FindingEnvelope>? findings = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new SessionTapeStepEnvelope
        {
            Kind = step.Kind,
            StepId = step.StepId,
            Label = step.Label,
            PluginName = step.PluginName,
            PluginVersion = step.PluginVersion,
            Timestamp = step.Timestamp,
            OffsetMs = (long)step.Offset.TotalMilliseconds,
            OwnerKind = step.OwnerKind,
            AgentId = step.AgentId,
            Findings = findings ?? [],
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

/// <summary>FR-22 (S-09, issue #18): the wire shape for one subagent's own lane — its identity, how
/// it finished, and the report it actually produced (<see cref="SubagentOutputEnvelope"/>), resolved
/// once per agent so a client can render each subagent's own lane distinctly from the main thread
/// (Scenario 5) without a second per-lane request. <see cref="Outcome"/> is <see cref="AgentOutcome"/>
/// itself, not re-declared here — the same reuse <see cref="SessionTapeStepEnvelope.OwnerKind"/>
/// already establishes for its own enum.</summary>
public sealed record SessionAgentLaneEnvelope
{
    public required string AgentId { get; init; }

    /// <summary><see langword="null"/> means spawned from the main thread, matching
    /// <see cref="Agent.ParentAgentId"/>'s own nullability.</summary>
    public string? ParentAgentId { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required AgentOutcome Outcome { get; init; }

    /// <summary>From <c>subagent.failed.data.error</c> — populated only when <see cref="Outcome"/> is
    /// <see cref="AgentOutcome.Failed"/>, matching <see cref="Agent.Error"/>'s own nullability.</summary>
    public string? Error { get; init; }

    /// <summary>The report this subagent actually produced, resolved by
    /// <see cref="SubagentOutputLookup.Find"/> from the session's own <see cref="RawEvent"/>s — not
    /// resolved here, since that lookup needs RAW events this type has no access to.</summary>
    public required SubagentOutputEnvelope Output { get; init; }

    public static SessionAgentLaneEnvelope From(Agent agent, SubagentOutputEnvelope output)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(output);

        return new SessionAgentLaneEnvelope
        {
            AgentId = agent.AgentId,
            ParentAgentId = agent.ParentAgentId,
            Name = agent.Name,
            DisplayName = agent.DisplayName,
            Outcome = agent.Outcome,
            Error = agent.Error,
            Output = output,
        };
    }
}

/// <summary>
/// FR-21's served masthead and tape (S-08, issue #15), FR-21 part 2 of 3's finding chip row (S-52,
/// issue #16), and FR-21 part 3 of 3's finality state (S-53, issue #17): the wire shape a client
/// reads <see cref="SessionRecording"/> and <see cref="SessionFindings"/> through, the same layering
/// <see cref="DigestEnvelope"/> already establishes for <see cref="ProcessDigest"/> (S-36).
/// <see cref="Status"/> carries FR-21 part 3 of 3's finality state alongside the masthead, steps and
/// chips — a client checks it before rendering the tape as the session's final picture. FR-22 (S-09,
/// issue #18) added <see cref="Lanes"/>, one entry per subagent, each carrying the report it
/// actually produced rather than the parent's truncated <c>read_agent</c> stub.
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

    /// <summary>FR-22 (S-09, issue #18): one entry per subagent this session spawned. An empty list
    /// is the designed "no subagents" state, the same discipline <see cref="Findings"/> already
    /// establishes — never a missing field.</summary>
    public required IReadOnlyList<SessionAgentLaneEnvelope> Lanes { get; init; }

    /// <summary><paramref name="lanes"/> defaults to <see langword="null"/> (served as an empty
    /// list) rather than being required, the same additive-parameter shape
    /// <see cref="SessionRecording.Build"/> already uses for its own <c>spawnResolution</c>
    /// parameter — every existing call site that supplies no lanes still compiles and still serves
    /// an empty list. <paramref name="stepFindings"/> (mockup parity item #17) follows the identical
    /// shape: <see langword="null"/> when the caller built no <see cref="SessionTapeStepFindingLookup"/>
    /// map (or does not want this behaviour at all — e.g. every pre-existing test in this project),
    /// in which case every step serves an empty <see cref="SessionTapeStepEnvelope.Findings"/> list.
    /// </summary>
    public static SessionEnvelope From(
        SessionRecording recording,
        SessionFindings findings,
        Func<Finding, FindingEnvelope> mapFinding,
        IReadOnlyList<SessionAgentLaneEnvelope>? lanes = null,
        IReadOnlyDictionary<(SessionTapeStepKind Kind, string StepId), IReadOnlyList<Finding>>? stepFindings = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(mapFinding);

        return new SessionEnvelope
        {
            Masthead = SessionMastheadEnvelope.From(recording.Masthead),
            Steps = recording.Tape.Steps
                .Select(step => SessionTapeStepEnvelope.From(
                    step,
                    stepFindings is not null && stepFindings.TryGetValue((step.Kind, step.StepId), out var matches)
                        ? matches.Select(mapFinding).ToList()
                        : null))
                .ToList(),
            Status = SessionRecordingStatusEnvelope.From(recording.Status),
            Findings = findings.Chips.Select(chip => SessionFindingChipEnvelope.From(chip, mapFinding)).ToList(),
            Lanes = lanes ?? [],
        };
    }
}
