using System.Text.Json;
using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-7's "operator-configured, not compiled in" list of excluded repository roots. Resolved from
/// a JSON file the same way <see cref="AecoPostMortem.Data.StoreLocation"/> resolves the store's
/// own path — sitting alongside it, under the same per-user application-data folder — so an
/// operator edits one file and the very next ingest run honours it, with no rebuild in between
/// (Scenario 2).
/// </summary>
public static class ExclusionListSource
{
    public const string FileName = "exclusions.json";

    /// <summary>The marker <see cref="Load"/> walks upward looking for, to name this product's own
    /// checkout as the default excluded root — the same bounded upward walk
    /// <c>AecoPostMortem.Cli.ServeWebRoot.Resolve</c> already uses to find <c>web/dist</c>.</summary>
    public const string SolutionMarkerFileName = "AecoPostMortem.sln";

    public static string DefaultPath => Path.Combine(StoreLocation.DefaultFolder, FileName);

    /// <summary>
    /// Reads the configured list fresh every call — there is no cache, which is what makes
    /// Scenario 2 true structurally. When <paramref name="path"/> does not exist, the default is
    /// this product's own repository root (FR-7: "defaulting to this product's own repository
    /// root"), discovered by walking upward from <paramref name="searchStartDirectory"/> for
    /// <see cref="SolutionMarkerFileName"/>; on a machine with no checkout to find (an installed
    /// build), the default is an empty list rather than a guess. An explicit configured file always
    /// wins, even an empty one — the operator asking for no exclusions is honoured, not silently
    /// replaced by the default.
    /// </summary>
    public static IReadOnlyList<string> Load(string? path = null, string? searchStartDirectory = null)
    {
        var file = path ?? DefaultPath;

        if (File.Exists(file))
        {
            return ReadConfiguredRoots(file);
        }

        var repositoryRoot = FindRepositoryRoot(searchStartDirectory ?? AppContext.BaseDirectory);
        return repositoryRoot is null ? [] : [repositoryRoot];
    }

    /// <summary>Malformed JSON reads as no exclusions rather than throwing — an ingest run should
    /// not fail outright over a config file the operator is still editing, and reporting nothing
    /// excluded (rather than falling back to the default) keeps the operator's own edit-in-progress
    /// from silently reappearing as an unrelated default.</summary>
    static IReadOnlyList<string> ReadConfiguredRoots(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var roots = JsonSerializer.Deserialize<string[]>(stream) ?? [];
            return roots.Where(root => !string.IsNullOrWhiteSpace(root)).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionMarkerFileName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
