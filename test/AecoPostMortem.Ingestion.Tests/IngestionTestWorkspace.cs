using System.Globalization;
using System.Text;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// A throwaway <c>session-state</c> root plus a throwaway store, shared by the discovery,
/// event-reading and ingest tests. Distinct from <c>SourceIsNeverWrittenToTests</c>'s own private
/// <c>Workspace</c>, which exists only to prove the source is never written to.
/// </summary>
public sealed class IngestionTestWorkspace : IDisposable
{
    public IngestionTestWorkspace()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "AecoPostMortem.Tests",
            Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

        SessionStateRoot = Path.Combine(Root, "session-state");
        Directory.CreateDirectory(SessionStateRoot);

        Store = new LocalStore(Path.Combine(Root, "store", StoreLocation.FileName));
    }

    public string Root { get; }

    public string SessionStateRoot { get; }

    public LocalStore Store { get; }

    public string CreateSessionDirectory(string sessionId)
    {
        var directory = Path.Combine(SessionStateRoot, sessionId);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Writes <c>events.jsonl</c> as exactly the given bytes — no line-ending translation,
    /// so a test can state precisely whether the file ends in a newline.</summary>
    public string WriteEventsFile(string sessionId, byte[] content)
    {
        var directory = CreateSessionDirectory(sessionId);
        var file = Path.Combine(directory, SessionDiscovery.EventsFileName);
        File.WriteAllBytes(file, content);
        return file;
    }

    /// <summary>Joins the lines with <c>\n</c> only, optionally with a trailing terminator — the
    /// shape a test states when it cares about newline-termination, not encoding.</summary>
    public string WriteEventsFile(string sessionId, bool trailingNewline, params string[] lines)
    {
        var text = string.Join('\n', lines) + (trailingNewline ? "\n" : string.Empty);
        return WriteEventsFile(sessionId, Encoding.UTF8.GetBytes(text));
    }

    public void WriteSiblingFiles(
        string sessionId,
        bool sessionDb = false,
        bool rewindSnapshotsIndex = false,
        bool workspaceYaml = false)
    {
        var directory = CreateSessionDirectory(sessionId);

        if (sessionDb)
        {
            File.WriteAllText(Path.Combine(directory, SessionDiscovery.SessionDatabaseFileName), string.Empty);
        }

        if (rewindSnapshotsIndex)
        {
            var rewindFolder = Path.Combine(directory, SessionDiscovery.RewindSnapshotsFolderName);
            Directory.CreateDirectory(rewindFolder);
            File.WriteAllText(
                Path.Combine(rewindFolder, SessionDiscovery.RewindSnapshotsIndexFileName),
                "{}");
        }

        if (workspaceYaml)
        {
            File.WriteAllText(Path.Combine(directory, SessionDiscovery.WorkspaceFileName), string.Empty);
        }
    }

    public void Dispose()
    {
        Store.Purge();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created, or already gone.
        }
    }
}
