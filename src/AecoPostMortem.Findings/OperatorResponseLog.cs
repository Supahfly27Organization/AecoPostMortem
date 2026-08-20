namespace AecoPostMortem.Findings;

/// <summary>
/// One entry in FR-45's append-only response history: what the operator did with one finding, and
/// when. A finding's identity is <c>(Class, RecurrenceKey)</c> per FR-57 — the same identity
/// <see cref="Finding"/> itself carries no bare id for (<c>Finding.cs</c>'s own doc comment), so a
/// record here targets exactly that pair rather than inventing a second finding id.
/// <see cref="Provenance"/> is captured alongside the response rather than looked up later, because
/// Scenario 1 of issue #49 says the outcome is stored "against the finding and its provenance
/// level" — the guardrail's second figure (PRD §5.4) reads the provenance the finding carried when
/// the operator actually acted on it.
/// </summary>
public sealed record OperatorResponseRecord
{
    public required FindingClass Class { get; init; }

    public required string RecurrenceKey { get; init; }

    public required Provenance Provenance { get; init; }

    public required OperatorResponse Response { get; init; }

    /// <summary>When this response was recorded. Supplied by the caller, never read from a clock
    /// here — the same determinism §3.8 requires everywhere else in this project (see
    /// <c>SuggestionRenderer</c>'s own "reads no clock" guarantee).</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// FR-45's append-only history of operator responses. The edge case named in issue #49 — "changing a
/// verdict later must be possible and must not lose the earlier one" — is why <see cref="Record"/>
/// only ever adds to <see cref="Entries"/>; nothing on this type can remove or overwrite an entry.
/// <see cref="CurrentResponses"/> is the read side: the latest entry per finding identity, which is
/// what a rendered <see cref="Finding.OperatorResponse"/> should reflect (<see cref="Apply"/>) and
/// what <see cref="Guardrail.Compute"/> is drawn from.
/// </summary>
public sealed record OperatorResponseLog
{
    public required IReadOnlyList<OperatorResponseRecord> Entries { get; init; }

    public static readonly OperatorResponseLog Empty = new() { Entries = [] };

    /// <summary>Appends one response record and returns a new log — <see cref="Entries"/> only ever
    /// grows, so an earlier verdict for the same finding is still there after a later one is
    /// recorded.</summary>
    public OperatorResponseLog Record(OperatorResponseRecord entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new OperatorResponseLog { Entries = [.. Entries, entry] };
    }

    /// <summary>The latest recorded response per finding identity. Ties in <see cref="OperatorResponseRecord.RecordedAt"/>
    /// resolve to whichever entry was recorded later (<c>OrderBy</c> is stable, so the later append
    /// wins), so two records sharing one instant still resolve deterministically.</summary>
    public IReadOnlyList<OperatorResponseRecord> CurrentResponses() =>
        Entries
            .GroupBy(entry => (entry.Class, entry.RecurrenceKey))
            .Select(group => group.OrderBy(entry => entry.RecordedAt).Last())
            .ToList();

    /// <summary>Populates <see cref="Finding.OperatorResponse"/> from this log's current response for
    /// <paramref name="finding"/>'s identity — the field's own default, <see cref="OperatorResponse.Ignored"/>,
    /// stands when no response has been recorded (<c>Finding.cs</c>'s "no separate pending state").</summary>
    public Finding Apply(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var current = CurrentResponses()
            .FirstOrDefault(entry => entry.Class == finding.Class && entry.RecurrenceKey == finding.Recurrence.Key);

        return current is null ? finding : finding with { OperatorResponse = current.Response };
    }
}
