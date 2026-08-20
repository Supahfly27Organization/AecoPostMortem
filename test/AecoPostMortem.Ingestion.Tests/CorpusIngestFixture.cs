using System.Diagnostics;
using System.Globalization;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// S-45's shared drive: one full ingest of the live reference corpus into a throwaway store,
/// followed by one incremental re-ingest of the same, unchanged sessions. `IClassFixture` runs this
/// once per test class rather than once per <c>[Fact]</c>, so <see cref="CorpusVerificationTests"/>'s
/// four scenarios (census, byte-identical replay, full-ingest timing, incremental-re-ingest timing)
/// all read off the one pair of runs rather than re-ingesting 176.7 MiB four times over.
/// </summary>
/// <remarks>
/// <see cref="Available"/> is <c>false</c> — never an exception — when the live corpus
/// (<see cref="ReferenceCorpus"/>) is not on the machine running the suite; every scenario that
/// depends on this fixture skips rather than fails in that case, the same shape
/// <c>ApplyPatchCorpusRoundTripTests</c> already uses. Full-corpus ingestion is driven directly
/// through <see cref="SessionDiscovery"/> and <see cref="SessionIngestor"/> — the composable
/// building blocks E1 landed (issue #5, issue #7) — because the <c>ingest</c> CLI command itself
/// does not exist yet (<c>AecoPostMortem.Cli/CLAUDE.md</c>): this is the same directory walk that
/// command will eventually wrap.
/// </remarks>
public sealed class CorpusIngestFixture : IDisposable
{
    readonly string? _tempRoot;

    public CorpusIngestFixture()
    {
        Source = ReferenceCorpus.Source();
        Available = ReferenceCorpus.IsAvailable(Source);

        if (!Available)
        {
            return;
        }

        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "AecoPostMortem.Tests",
            Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

        Store = new LocalStore(Path.Combine(_tempRoot, "store", StoreLocation.FileName));
        Context = Store.Open();

        var discovery = SessionDiscovery.Discover(Source!);
        Sessions = discovery.Sessions
            .Where(session => session.HasEvents)
            .OrderBy(session => session.SessionId, StringComparer.Ordinal)
            .ToArray();

        // long, not int: SessionIngestor.Ingest's own EventsInserted is an int, but PRD §3.7's
        // design target is 1,000,000 events — accumulating into a wider type here costs nothing
        // and means this fixture does not need to change if a future corpus approaches that target.
        var fullIngestStopwatch = Stopwatch.StartNew();
        long insertedFirstPass = 0;
        foreach (var session in Sessions)
        {
            var result = SessionIngestor.Ingest(Context, session.SessionId, session.EventsFile!);
            insertedFirstPass += result.EventsInserted;
        }
        fullIngestStopwatch.Stop();
        FullIngestElapsed = fullIngestStopwatch.Elapsed;
        EventsInsertedFirstPass = insertedFirstPass;

        var incrementalStopwatch = Stopwatch.StartNew();
        long insertedSecondPass = 0;
        foreach (var session in Sessions)
        {
            var result = SessionIngestor.Ingest(Context, session.SessionId, session.EventsFile!);
            insertedSecondPass += result.EventsInserted;
        }
        incrementalStopwatch.Stop();
        IncrementalReingestElapsed = incrementalStopwatch.Elapsed;
        EventsInsertedSecondPass = insertedSecondPass;
    }

    public string? Source { get; }

    public bool Available { get; }

    public LocalStore? Store { get; }

    public PostMortemContext? Context { get; }

    public IReadOnlyList<ClassifiedSession> Sessions { get; } = [];

    public TimeSpan FullIngestElapsed { get; }

    public long EventsInsertedFirstPass { get; }

    public TimeSpan IncrementalReingestElapsed { get; }

    public long EventsInsertedSecondPass { get; }

    public void Dispose()
    {
        Context?.Dispose();
        Store?.Purge();

        if (_tempRoot is null)
        {
            return;
        }

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created, or already gone.
        }
    }
}
