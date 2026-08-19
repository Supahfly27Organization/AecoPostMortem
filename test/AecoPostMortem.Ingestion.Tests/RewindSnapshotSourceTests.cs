using System.Globalization;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-13: <c>rewind-snapshots/index.json</c> is a single JSON object, like <c>.meta.json</c>, so
/// the one-file-one-event rule applies — but it is rewritten in place as the session grows, unlike
/// an append-only stream. RAW's "keeps both versions at the same identity" rule has to handle that
/// deliberately, not by accident.
/// </summary>
public sealed class RewindSnapshotSourceTests
{
    /// <summary>Reading the file produces exactly one RAW-shaped event, at byte offset zero — the
    /// whole file is the event, there is no line to seek within it.</summary>
    [Fact]
    public void The_whole_file_becomes_one_event_at_offset_zero()
    {
        using var workspace = new Workspace();
        var path = workspace.WriteIndex("""{"version":1,"snapshots":[]}""");

        var raw = RewindSnapshotSource.ReadAsEvent("session-1", sequence: 0, timestamp: Stamp, path);

        Assert.Equal(0, raw.ByteOffset);
        Assert.Equal("""{"version":1,"snapshots":[]}""", raw.Payload);
        Assert.Equal(path, raw.SourceFile);
    }

    /// <summary>Acceptance criterion 3: a file rewritten in place since last ingest keeps both
    /// versions, retained at the same source identity (same source file, same byte offset — the
    /// content hash is what differs and what makes both inserts land).</summary>
    [Fact]
    public void A_rewrite_in_place_keeps_both_versions_at_the_same_source_identity()
    {
        using var workspace = new Workspace();
        var path = workspace.WriteIndex("""{"version":1,"snapshots":[]}""");

        using var context = workspace.Store.Open();

        var first = RewindSnapshotSource.ReadAsEvent("session-1", sequence: 0, timestamp: Stamp, path);
        RawEventBatch.Append(context, [first]);

        // Rewritten in place: same path, grown content, as the session continues.
        workspace.WriteIndex(
            """{"version":1,"snapshots":[{"eventId":"evt-1"}]}""",
            path);
        var second = RewindSnapshotSource.ReadAsEvent("session-1", sequence: 0, timestamp: Stamp, path);
        RawEventBatch.Append(context, [second]);

        var stored = context.RawEvents.Where(row => row.SourceFile == path).OrderBy(row => row.Id).ToArray();

        Assert.Equal(2, stored.Length);
        Assert.All(stored, row => Assert.Equal(0, row.ByteOffset));
        Assert.NotEqual(stored[0].ContentHash, stored[1].ContentHash);
        Assert.Equal("""{"version":1,"snapshots":[]}""", stored[0].Payload);
        Assert.Equal("""{"version":1,"snapshots":[{"eventId":"evt-1"}]}""", stored[1].Payload);
    }

    /// <summary>Re-reading an unchanged file is the ordinary FR-5 idempotency case: the second read
    /// hashes identically to the first and the append adds nothing.</summary>
    [Fact]
    public void Reading_an_unchanged_file_again_adds_nothing()
    {
        using var workspace = new Workspace();
        var path = workspace.WriteIndex("""{"version":1,"snapshots":[]}""");

        using var context = workspace.Store.Open();

        RawEventBatch.Append(context, [RewindSnapshotSource.ReadAsEvent("session-1", 0, Stamp, path)]);
        var second = RawEventBatch.Append(context, [RewindSnapshotSource.ReadAsEvent("session-1", 0, Stamp, path)]);

        Assert.Equal(0, second);
        Assert.Single(context.RawEvents);
    }

    /// <summary>It goes through <see cref="SourceFiles.OpenRead"/>, the one door onto
    /// <c>~/.copilot/</c>, the same as every other source read.</summary>
    [Fact]
    public void Reading_the_index_does_not_lock_out_a_concurrent_writer()
    {
        using var workspace = new Workspace();
        var path = workspace.WriteIndex("""{"version":1,"snapshots":[]}""");

        RewindSnapshotSource.ReadAsEvent("session-1", 0, Stamp, path);

        using var writing = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Write,
                Share = FileShare.ReadWrite | FileShare.Delete,
            });

        Assert.True(writing.CanWrite);
    }

    const string Stamp = "2026-08-09T20:14:36.758Z";

    sealed class Workspace : IDisposable
    {
        readonly string root;

        public Workspace()
        {
            root = Path.Combine(
                Path.GetTempPath(),
                "AecoPostMortem.Tests",
                Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(root);

            Store = new LocalStore(Path.Combine(root, "store", StoreLocation.FileName));
        }

        public LocalStore Store { get; }

        public string WriteIndex(string json, string? path = null)
        {
            path ??= Path.Combine(root, "index.json");
            File.WriteAllText(path, json);
            return path;
        }

        public void Dispose()
        {
            Store.Purge();

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Never created, or already gone.
            }
        }
    }
}
