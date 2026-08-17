using System.Globalization;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// A store in a throwaway directory. The tests never touch
/// <see cref="StoreLocation.Default"/> — that is the operator's real store, and a test suite that
/// wrote to it would be the one thing FR-11 exists to prevent.
/// </summary>
public sealed class TemporaryStore : IDisposable
{
    public TemporaryStore()
    {
        Folder = Path.Combine(
            Path.GetTempPath(),
            "AecoPostMortem.Tests",
            Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

        Store = new LocalStore(Path.Combine(Folder, StoreLocation.FileName));
    }

    public string Folder { get; }

    public LocalStore Store { get; }

    public void Dispose()
    {
        Store.Purge();

        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created, or already gone — either way there is nothing to clean up.
        }
    }
}

/// <summary>Builds RAW rows for tests, so a test states only the part it is about.</summary>
public static class Events
{
    public const string ProviderVersion = "0.0.339";

    public static RawEvent From(
        string payload,
        string sessionId = "session-1",
        long sequence = 0,
        string eventType = "session.start",
        string sourceFile = @"~/.copilot/session-state/session-1/events.jsonl",
        long byteOffset = 0) =>
        new(
            sessionId,
            sequence,
            eventType,
            "2026-08-09T20:14:36.758Z",
            ProviderVersion,
            sourceFile,
            byteOffset,
            RawPayload.ContentHashOfText(payload),
            payload);
}
