namespace AecoPostMortem.Api;

/// <summary>
/// GET /api/settings's response contract: the operator's real, currently-resolved configuration —
/// nothing here is guessed or derived beyond what <see cref="AecoPostMortem.Data.LocalStore"/> and
/// <see cref="AecoPostMortem.Ingestion.ExclusionListSource"/> already resolve for <c>ingest</c>
/// itself, so this surface can never disagree with what a real <c>ingest</c> run would actually do.
/// <see cref="StoreExists"/> is <see langword="false"/> before the first ingest — a real, honest
/// state the client renders distinctly (`web/CLAUDE.md`'s "an honest empty state, never a guessed
/// number" discipline), not a bare <c>0</c> that would read as a measured, empty-but-real store.
/// </summary>
public sealed record SettingsEnvelope(
    string StorePath,
    bool StoreExists,
    long StoreSizeBytes,
    string CopilotSourceRoot,
    bool CopilotSourceFound,
    IReadOnlyList<string> ExcludedRoots);
