using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Resolves the live reference corpus directory the frozen fixture manifest
/// (<c>fixtures/corpus-manifest.json</c>) was built from. The corpus bytes are deliberately not
/// checked in (<c>fixtures/README.md</c>) — only their hashes and post-exclusion census are — so
/// every gate that needs the live directory reads the manifest's own <c>source</c> field
/// (overridable by <c>AECOPOSTMORTEM_CORPUS_SOURCE</c>) rather than a path hardcoded per test.
/// Reading the source out of the frozen manifest, rather than defaulting to the real machine's
/// Copilot directory directly, is what keeps every corpus-shaped gate pinned to the fixture FR-55
/// froze rather than to whatever happens to be on the machine running the suite today (S-45's first
/// acceptance scenario).
/// </summary>
public static class ReferenceCorpus
{
    /// <summary>The live directory to read, or <c>null</c> if it cannot be resolved. Does not check
    /// whether it exists — see <see cref="IsAvailable"/>.</summary>
    public static string? Source()
    {
        var overridden = Environment.GetEnvironmentVariable("AECOPOSTMORTEM_CORPUS_SOURCE");
        if (!string.IsNullOrEmpty(overridden))
        {
            return overridden;
        }

        var manifestPath = ManifestPath();
        if (manifestPath is null)
        {
            return null;
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.TryGetProperty("source", out var sourceElement)
            ? sourceElement.GetString()
            : null;
    }

    /// <summary><c>true</c> when <paramref name="source"/> is non-null and really is a directory on
    /// this machine — the condition every corpus-shaped gate skips rather than fails on.</summary>
    public static bool IsAvailable([NotNullWhen(true)] string? source) =>
        source is not null && Directory.Exists(source);

    /// <summary>Walks up from the test binary's own directory to find the repo's
    /// <c>fixtures/corpus-manifest.json</c>, so resolution does not depend on the working directory
    /// the runner happens to use.</summary>
    public static string? ManifestPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", "corpus-manifest.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
