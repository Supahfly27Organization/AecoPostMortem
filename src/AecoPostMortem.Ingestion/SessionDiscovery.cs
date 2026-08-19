namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-1: find every session directory under the Copilot session-state root and classify its
/// files, without reading any of them. A directory with no <c>events.jsonl</c> is still reported —
/// classified and skipped, not omitted (measured 12 of 47 directories in the frozen corpus) — and a
/// missing root is reported rather than thrown, since the operator may not have run Copilot yet.
/// </summary>
public static class SessionDiscovery
{
    public const string EventsFileName = "events.jsonl";
    public const string SessionDatabaseFileName = "session.db";
    public const string WorkspaceFileName = "workspace.yaml";
    public const string RewindSnapshotsFolderName = "rewind-snapshots";
    public const string RewindSnapshotsIndexFileName = "index.json";

    public static SessionDiscoveryResult Discover(string sessionStateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionStateRoot);

        if (!Directory.Exists(sessionStateRoot))
        {
            return new SessionDiscoveryResult(sessionStateRoot, RootFound: false, Sessions: []);
        }

        var sessions = Directory.EnumerateDirectories(sessionStateRoot)
            .Order(StringComparer.Ordinal)
            .Select(Classify)
            .ToArray();

        return new SessionDiscoveryResult(sessionStateRoot, RootFound: true, sessions);
    }

    static ClassifiedSession Classify(string directory)
    {
        var eventsFile = Path.Combine(directory, EventsFileName);
        var sessionDatabaseFile = Path.Combine(directory, SessionDatabaseFileName);
        var workspaceFile = Path.Combine(directory, WorkspaceFileName);
        var rewindSnapshotsIndexFile = Path.Combine(directory, RewindSnapshotsFolderName, RewindSnapshotsIndexFileName);

        return new ClassifiedSession(
            Path.GetFileName(directory),
            directory,
            File.Exists(eventsFile) ? eventsFile : null,
            File.Exists(sessionDatabaseFile) ? sessionDatabaseFile : null,
            File.Exists(rewindSnapshotsIndexFile) ? rewindSnapshotsIndexFile : null,
            File.Exists(workspaceFile) ? workspaceFile : null);
    }
}

/// <summary>The outcome of a discovery run. <see cref="RootFound"/> is <c>false</c>, never an
/// exception, when the Copilot directory does not exist on this machine.</summary>
public sealed record SessionDiscoveryResult(
    string Root,
    bool RootFound,
    IReadOnlyList<ClassifiedSession> Sessions);

/// <summary>One session directory's files, classified — not read. <see cref="SessionDatabaseFile"/>
/// is classified only: FR-1 says v1 does not ingest it, so the coverage report can say it was seen
/// and skipped rather than staying silent about it.</summary>
public sealed record ClassifiedSession(
    string SessionId,
    string Directory,
    string? EventsFile,
    string? SessionDatabaseFile,
    string? RewindSnapshotsIndexFile,
    string? WorkspaceFile)
{
    public bool HasEvents => EventsFile is not null;
}
