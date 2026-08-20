namespace AecoPostMortem.Api;

/// <summary>The three states the app can be in before it has anything to show (S-48).</summary>
public enum AppStateKind
{
    /// <summary>No Copilot session-state directory exists on this machine at all.</summary>
    NoSourceFound,

    /// <summary>The source exists, but the local store carries no ingested events yet.</summary>
    EmptyStore,

    /// <summary>The source exists and the store carries at least one ingested event.</summary>
    Ready,
}

/// <summary>
/// What the operator sees when opening the app (S-48). The two empty states are different
/// diagnoses with different fixes and must not collapse into one message: a missing Copilot
/// directory has no fix this product can name — the operator has to go run Copilot first — while
/// an empty store's fix is the <c>ingest</c> command, named explicitly rather than left implied.
/// </summary>
public sealed record AppStateReport(AppStateKind Kind, string Message, string? FixCommand)
{
    /// <summary>The exact command line the empty-store diagnosis points at.</summary>
    public const string IngestCommand = "aecopostmortem ingest";

    /// <summary>
    /// A missing Copilot directory is diagnosed first: on a machine that has never run Copilot,
    /// both conditions are true at once (there is no source, so nothing could have been ingested
    /// either), and naming the root cause is more useful than naming its downstream symptom.
    /// </summary>
    public static AppStateReport Diagnose(bool copilotSourceFound, bool storeHasBeenIngested)
    {
        if (!copilotSourceFound)
        {
            return new AppStateReport(
                AppStateKind.NoSourceFound,
                "No source was found: no Copilot session-state directory exists on this machine. "
                + "Nothing can be ingested until GitHub Copilot CLI has been run here at least once.",
                FixCommand: null);
        }

        if (!storeHasBeenIngested)
        {
            return new AppStateReport(
                AppStateKind.EmptyStore,
                "Nothing has been ingested yet.",
                IngestCommand);
        }

        return new AppStateReport(AppStateKind.Ready, "Ready.", FixCommand: null);
    }
}
