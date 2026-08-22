namespace AecoPostMortem.Api;

/// <summary>
/// POST /api/purge's response contract: which files were actually deleted and how many bytes that
/// reclaimed — <c>Data.LocalStore.PurgeOutcome</c> carried onto the wire verbatim, the same
/// passthrough <see cref="IngestResultEnvelope"/> gives <c>Ingestion.CoverageReport</c>.
///
/// <see cref="DeletedAnything"/> is served explicitly rather than left for a client to infer from an
/// empty <see cref="DeletedFiles"/> list: purging a store that was never created is a real, honest
/// outcome (the CLI says "Nothing to purge; there is no store at …"), and a client rendering "the
/// store was deleted" for it would claim a deletion that never happened. There is no
/// <c>DurationSeconds</c> here, unlike the other two write routes — deleting a file is not work an
/// operator waits on, and a measured figure for it would be noise rather than information.
/// </summary>
public sealed record PurgeResultEnvelope(
    bool DeletedAnything,
    IReadOnlyList<string> DeletedFiles,
    long BytesReclaimed);
