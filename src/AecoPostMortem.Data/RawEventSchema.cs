namespace AecoPostMortem.Data;

/// <summary>
/// The physical names of the RAW table. The model maps to them and the batched append writes them
/// literally, so they are stated once here rather than in two places that can drift apart.
/// </summary>
public static class RawEventSchema
{
    public const string Table = "raw_event";

    public const string Id = "id";
    public const string SessionId = "session_id";
    public const string Sequence = "seq";
    public const string EventType = "event_type";
    public const string Timestamp = "ts";
    public const string ProviderVersion = "provider_version";
    public const string SourceFile = "source_file";
    public const string ByteOffset = "byte_offset";
    public const string ContentHash = "content_hash";
    public const string Payload = "payload";

    /// <summary>FR-2's identity triple, unique — which is what makes a re-ingest a no-op (FR-5).</summary>
    public const string IdentityIndex = "ux_raw_identity";

    /// <summary>The Flight Recorder's tape: a session's events in order.</summary>
    public const string SessionSequenceIndex = "ix_raw_session_seq";

    /// <summary>The event census, which counts by type.</summary>
    public const string EventTypeIndex = "ix_raw_type";

    /// <summary>Every column the append path writes, in the order it writes them. The surrogate key
    /// is not among them: SQLite assigns it.</summary>
    public static IReadOnlyList<string> WrittenColumns { get; } =
    [
        SessionId,
        Sequence,
        EventType,
        Timestamp,
        ProviderVersion,
        SourceFile,
        ByteOffset,
        ContentHash,
        Payload,
    ];
}
