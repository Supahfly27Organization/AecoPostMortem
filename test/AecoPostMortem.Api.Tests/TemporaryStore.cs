using System.Globalization;
using AecoPostMortem.Data;

namespace AecoPostMortem.Api.Tests;

/// <summary>A store in a throwaway directory: the operator's real store
/// (<see cref="StoreLocation.Default"/>) must never be touched by a test.</summary>
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
            // Never created, or already gone.
        }
    }
}
