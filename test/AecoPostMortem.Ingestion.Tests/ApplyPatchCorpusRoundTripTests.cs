using System.Text.Json;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-4's corpus round-trip gate — S-03's third acceptance scenario: every <c>apply_patch</c> call
/// in the reference corpus is parsed and re-serialised, and a single failure fails the build. PRD
/// §3.9 lists this first among common failure modes: a parser that assumes <c>arguments</c> is
/// always an object silently drops every patch, and finding class 3 loses its entire input,
/// silently and without error.
/// </summary>
/// <remarks>
/// The corpus round-trip is a gate on real data, not a unit test with hand-picked strings
/// (<c>ToolArgumentsTests</c> covers those). The bytes themselves are not checked in
/// (<c>fixtures/README.md</c>) — only their hashes — so this test reads the live source directory
/// the manifest was frozen from (<c>fixtures/corpus-manifest.json</c>'s own <c>source</c> field,
/// overridable by <c>AECOPOSTMORTEM_CORPUS_SOURCE</c>) and skips, rather than fails, when that
/// directory is not present on the machine running the suite. <c>scripts/check-apply-patch-roundtrip.py</c>
/// is the CI entry point that runs this test in isolation and forwards its exit code.
/// </remarks>
public sealed class ApplyPatchCorpusRoundTripTests
{
    [Fact]
    public void Every_apply_patch_call_in_the_corpus_parses_as_a_string_and_round_trips()
    {
        var source = CorpusSource();
        if (source is null || !Directory.Exists(source))
        {
            Assert.Skip(
                $"No corpus at {source ?? "(unresolved)"} on this machine; the gate only runs "
                + "where the corpus does.");
            return;
        }

        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var eventsFile in Directory
                     .EnumerateFiles(source, "events.jsonl", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            using var stream = SourceFiles.OpenRead(eventsFile);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                JsonDocument envelope;
                try
                {
                    envelope = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue; // malformed lines are FR-6's problem, not FR-4's
                }

                using (envelope)
                {
                    var root = envelope.RootElement;
                    if (!root.TryGetProperty("type", out var typeElement)
                        || typeElement.GetString() != "tool.execution_start")
                    {
                        continue;
                    }

                    if (!root.TryGetProperty("data", out var data)
                        || !data.TryGetProperty("toolName", out var toolNameElement)
                        || toolNameElement.GetString() != "apply_patch"
                        || !data.TryGetProperty("arguments", out var argumentsElement))
                    {
                        continue;
                    }

                    checkedCount++;
                    Check(eventsFile, argumentsElement.GetRawText(), failures);
                }
            }
        }

        Assert.True(
            checkedCount > 0,
            $"No apply_patch calls found under {source} — the gate would be vacuous.");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedCount} apply_patch call(s) failed to round-trip:\n"
            + string.Join('\n', failures.Take(10)));
    }

    static void Check(string eventsFile, string raw, List<string> failures)
    {
        try
        {
            var parsed = ToolArguments.Parse(raw);
            if (parsed.Kind != ToolArgumentKind.String)
            {
                failures.Add($"{eventsFile}: apply_patch arguments parsed as {parsed.Kind}, not String");
                return;
            }

            var reparsed = ToolArguments.Parse(parsed.ToJson());
            if (reparsed.Kind != ToolArgumentKind.String || reparsed.AsText != parsed.AsText)
            {
                failures.Add($"{eventsFile}: apply_patch arguments did not round-trip");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{eventsFile}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    static string? CorpusSource()
    {
        var overridden = Environment.GetEnvironmentVariable("AECOPOSTMORTEM_CORPUS_SOURCE");
        if (!string.IsNullOrEmpty(overridden))
        {
            return overridden;
        }

        var manifestPath = FindManifestPath();
        if (manifestPath is null)
        {
            return null;
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.TryGetProperty("source", out var sourceElement)
            ? sourceElement.GetString()
            : null;
    }

    /// <summary>Walks up from the test binary's own directory to find the repo's
    /// <c>fixtures/corpus-manifest.json</c>, so the test does not depend on the working directory
    /// the runner happens to use.</summary>
    static string? FindManifestPath()
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
