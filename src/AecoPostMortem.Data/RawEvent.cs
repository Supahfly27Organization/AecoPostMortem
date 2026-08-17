namespace AecoPostMortem.Data;

/// <summary>
/// One Copilot event line, preserved exactly as it was read (FR-2). <see cref="Payload"/> is the
/// whole line, not a projection of it, so a field no parser recognises today survives to be read by
/// a parser that does.
/// </summary>
/// <remarks>
/// The identity is <c>(SourceFile, ByteOffset, ContentHash)</c> — FR-2's triple, enforced by a
/// unique index rather than by the writer, which is what makes re-ingesting the same log a no-op
/// (FR-5) instead of a duplicate.
///
/// <see cref="SessionId"/>, <see cref="Sequence"/>, <see cref="EventType"/> and
/// <see cref="Timestamp"/> are lifted out of the envelope and stored beside the payload. They are
/// not a second copy of the truth — the payload stays authoritative and byte-exact — they exist
/// because the read path the latency measurement issues is
/// <c>raw_event(session_id, seq)</c> and <c>raw_event(event_type)</c>, and an index cannot be built
/// over a value that only exists inside a JSON string
/// (<c>docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md</c>).
/// </remarks>
public sealed record RawEvent(
    string SessionId,
    long Sequence,
    string EventType,
    string Timestamp,
    string ProviderVersion,
    string SourceFile,
    long ByteOffset,
    string ContentHash,
    string Payload)
{
    /// <summary>The store's own surrogate key. Assigned by SQLite on insert; it carries no meaning
    /// and nothing outside the store may key on it.</summary>
    public long Id { get; init; }
}
