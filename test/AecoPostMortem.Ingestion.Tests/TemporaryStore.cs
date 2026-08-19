using System.Globalization;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// A store in a throwaway directory, mirroring <c>AecoPostMortem.Data.Tests.TemporaryStore</c> for
/// this project — the tests never touch <see cref="StoreLocation.Default"/>, the operator's real
/// store.
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
