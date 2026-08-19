namespace AecoPostMortem.Containment.Tests;

/// <summary>
/// The determinism contract's "no clock, no chance, no model call" scenario (issue #24, S-46, PRD
/// §3.8): a rebuild that produces a different answer on a re-run is a bug, and the three ways a
/// check's own code could cause that are reading the wall clock, sampling randomly, or reaching out
/// to a model. This is a textual scan rather than a reference check, because the surface that would
/// let a check do any of the three is entirely inside the base class library — <c>DateTime.Now</c>,
/// <c>Random</c> and <c>HttpClient</c> need no <c>PackageReference</c> at all, so
/// <see cref="SolutionContainmentTests.The_rules_project_references_no_persistence_assembly"/>'s
/// "references nothing" guarantee does not by itself rule any of them out.
/// </summary>
/// <remarks>
/// <see cref="AnalysisCodePaths"/> is <c>AecoPostMortem.Rules</c> (the check-shape catalogue FR-34
/// binds) and <c>AecoPostMortem.Findings</c> (the orchestrator that runs a check and writes its
/// result — see that project's own CLAUDE.md). Both are still close to empty: no check exists yet to
/// scan (its CLAUDE.md: "Empty. The check-shape catalogue is the first thing that lands here."), so
/// this test is the enforcement mechanism itself, built ahead of what it enforces, exactly as the
/// story that added it says to do. <see cref="The_scanner_is_not_vacuous"/> is what keeps that
/// honest: it proves the pattern list and the file walk actually catch a violation, rather than the
/// main test passing today only because there is nothing yet for it to see.
/// </remarks>
public sealed class DeterminismInvariantTests
{
    static readonly string[] AnalysisCodePaths = ["src/AecoPostMortem.Rules", "src/AecoPostMortem.Findings"];

    /// <summary>
    /// Substrings rather than a single regex, so a failure message can name which of the three
    /// forbidden things (clock, chance, model) a match belongs to. Deliberately conservative: this
    /// is a list of what to reject, which — per <c>AecoPostMortem.Rules/CLAUDE.md</c>'s own
    /// reasoning for preferring an allowlist of zero — can never be exhaustive. It catches the direct
    /// and obvious spellings; a check that launders a clock read through a helper method is this
    /// test's known blind spot, same as any textual scan.
    /// </summary>
    static readonly (string Pattern, string Forbidden)[] ForbiddenPatterns =
    [
        ("DateTime.Now", "reads the wall clock"),
        ("DateTime.UtcNow", "reads the wall clock"),
        ("DateTimeOffset.Now", "reads the wall clock"),
        ("DateTimeOffset.UtcNow", "reads the wall clock"),
        ("Environment.TickCount", "reads the wall clock"),
        ("Stopwatch.StartNew", "reads the wall clock"),
        ("new Random(", "samples randomly"),
        ("Random.Shared", "samples randomly"),
        ("RandomNumberGenerator", "samples randomly"),
        ("Guid.NewGuid", "samples randomly"),
        ("HttpClient", "calls a model"),
        ("IChatClient", "calls a model"),
        ("ChatCompletion", "calls a model"),
    ];

    [Fact]
    public void No_source_in_the_analysis_code_path_reads_the_clock_samples_randomly_or_calls_a_model()
    {
        var offenders = (
            from directory in AnalysisCodePaths
            from file in Repository.CSharpFiles(directory)
            from line in File.ReadLines(file.FullName).Select((text, index) => (Text: text, Number: index + 1))
            from forbidden in ForbiddenPatterns
            where line.Text.Contains(forbidden.Pattern, StringComparison.Ordinal)
            select $"{Repository.RelativePath(file)}:{line.Number} {forbidden.Pattern} "
                   + $"({forbidden.Forbidden}): {line.Text.Trim()}").ToArray();

        Assert.True(
            offenders.Length == 0,
            "No check may read the current time, sample randomly, or call a model (PRD §3.8, "
            + "issue #24); found: " + string.Join("; ", offenders));
    }

    /// <summary>Proves the scan above is a real check rather than one that passes only because
    /// <see cref="AnalysisCodePaths"/> currently has nothing in it for the patterns to match.</summary>
    [Fact]
    public void The_scanner_is_not_vacuous()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "AecoPostMortem.Tests", "determinism-scan-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "Offender.cs"),
                "var stamp = DateTime.Now;\nvar roll = new Random();\n");

            var offenders =
                from file in Directory.EnumerateFiles(tempDirectory, "*.cs")
                from line in File.ReadLines(file)
                from forbidden in ForbiddenPatterns
                where line.Contains(forbidden.Pattern, StringComparison.Ordinal)
                select forbidden.Pattern;

            Assert.Contains("DateTime.Now", offenders);
            Assert.Contains("new Random(", offenders);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
