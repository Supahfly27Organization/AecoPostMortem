using System.Globalization;
using System.Security.Cryptography;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// §3.8's read-only rule: the product never writes to <c>~/.copilot/</c>. Checked by hashing the
/// source directory before and after landing its lines in RAW, which is the only way to state it
/// that a later change cannot quietly break.
/// </summary>
public sealed class SourceIsNeverWrittenToTests
{
    [Fact]
    public void A_source_file_is_opened_for_reading_and_nothing_else()
    {
        using var workspace = new Workspace();
        var file = workspace.WriteSession("session-1", """{"type":"session.start"}""");

        using var stream = SourceFiles.OpenRead(file);

        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
    }

    /// <summary>
    /// <c>events.jsonl</c> is written live by a session that may still be running. A reader that
    /// took an exclusive share would make the source fail to write — read-only has to mean the
    /// product neither writes to the source nor stops it being written to.
    /// </summary>
    [Fact]
    public void Reading_a_source_file_does_not_lock_out_the_session_still_writing_it()
    {
        using var workspace = new Workspace();
        var file = workspace.WriteSession("session-1", """{"type":"session.start"}""");

        using var reading = SourceFiles.OpenRead(file);
        using var writing = new FileStream(
            file,
            new FileStreamOptions
            {
                Mode = FileMode.Append,
                Access = FileAccess.Write,
                Share = FileShare.ReadWrite | FileShare.Delete,
            });

        Assert.True(writing.CanWrite);
    }

    [Fact]
    public void Every_file_under_the_session_directory_is_byte_identical_after_an_append_to_RAW()
    {
        using var workspace = new Workspace();
        workspace.WriteSession(
            "session-1",
            """{"type":"session.start","data":{"copilotVersion":"0.0.339"}}""",
            """{"type":"assistant.turn_start"}""");
        workspace.WriteSession("session-2", """{"type":"session.start"}""");

        var before = workspace.SnapshotSource();

        using (var context = workspace.Store.Open())
        {
            RawEventBatch.Append(context, workspace.ReadEveryLineThroughSourceFiles());
        }

        Assert.Equal(before, workspace.SnapshotSource());
    }

    /// <summary>A source directory and a store, both thrown away afterwards. The store sits outside
    /// the source tree, so a write that landed in the wrong place shows up in the snapshot.</summary>
    sealed class Workspace : IDisposable
    {
        readonly string root;

        public Workspace()
        {
            root = Path.Combine(
                Path.GetTempPath(),
                "AecoPostMortem.Tests",
                Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

            Source = Path.Combine(root, "session-state");
            Directory.CreateDirectory(Source);

            Store = new LocalStore(Path.Combine(root, "store", StoreLocation.FileName));
        }

        public string Source { get; }

        public LocalStore Store { get; }

        public string WriteSession(string sessionId, params string[] lines)
        {
            var folder = Path.Combine(Source, sessionId);
            Directory.CreateDirectory(folder);

            var file = Path.Combine(folder, "events.jsonl");
            File.WriteAllLines(file, lines);
            return file;
        }

        /// <summary>Every file's path, length, content hash and last-write time. A rewrite that
        /// happened to preserve the bytes would still move the timestamp.</summary>
        public IReadOnlyList<string> SnapshotSource() =>
            Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(file => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Path.GetRelativePath(Source, file)} {new FileInfo(file).Length} "
                    + $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))} "
                    + $"{File.GetLastWriteTimeUtc(file):O}"))
                .ToArray();

        public IEnumerable<RawEvent> ReadEveryLineThroughSourceFiles()
        {
            foreach (var file in Directory.EnumerateFiles(Source, "events.jsonl", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                using var stream = SourceFiles.OpenRead(file);
                using var reader = new StreamReader(stream);

                long sequence = 0;
                long offset = 0;
                while (reader.ReadLine() is { } line)
                {
                    var bytes = RawPayload.ToUtf8(line);
                    yield return new RawEvent(
                        Path.GetFileName(Path.GetDirectoryName(file))!,
                        sequence++,
                        "unparsed",
                        "2026-08-09T20:14:36.758Z",
                        "0.0.339",
                        file,
                        offset,
                        RawPayload.ContentHash(bytes),
                        line);

                    offset += bytes.Length;
                }
            }
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
