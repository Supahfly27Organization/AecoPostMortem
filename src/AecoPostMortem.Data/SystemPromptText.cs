namespace AecoPostMortem.Data;

/// <summary>
/// FR-12's content-addressed system-prompt text: verbatim prompt content stored once and referenced
/// by its own hash, so a measured 337 near-duplicate system messages (median 54,335 characters, data
/// map Part 6) do not become 337 near-duplicate rows.
/// </summary>
/// <remarks>
/// <see cref="ContentHash"/> is the natural key — <c>RawPayload.ContentHashOfText</c> of the
/// extracted <c>system.message.data.content</c> field itself, not of the enclosing envelope RAW
/// already stores verbatim (FR-2). Two sessions carrying the same prompt text hash identically and
/// land on the same row; each session still resolves its own full text by re-extracting its own
/// <c>system.message</c> RAW event the same way — see
/// <c>AecoPostMortem.Ingestion.SystemPromptExtractor</c>.
///
/// This table is migrated, not derived: it is written directly at ingest time from source bytes,
/// the same act that writes RAW, not re-derived from what is already in the store the way NORMALIZED
/// and FINDINGS are (Repo Rule 4 governs those two layers; this is RAW-adjacent content, keyed by a
/// hash of the content rather than by its position in the source, the same reasoning that keeps
/// <c>store_metadata</c> migrated — see <c>src/AecoPostMortem.Data/CLAUDE.md</c>).
/// </remarks>
public sealed record SystemPromptText(string ContentHash, string Text);
