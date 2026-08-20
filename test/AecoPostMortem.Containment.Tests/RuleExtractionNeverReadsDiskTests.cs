namespace AecoPostMortem.Containment.Tests;

/// <summary>
/// FR-26's third scenario (issue #32): rule-statement extraction reads only the ingested store,
/// never a markdown file on disk. <c>AecoPostMortem.Rules/CLAUDE.md</c> already proves this project
/// references no persistence assembly and no other project; that does not by itself rule out direct
/// file access, since <c>System.IO</c> needs no <c>PackageReference</c> at all — the same reasoning
/// <see cref="DeterminismInvariantTests"/> gives for scanning source rather than trusting the
/// reference list alone. This is a textual scan of <c>AecoPostMortem.Rules</c>'s own source, which is
/// where <c>&lt;custom_instruction&gt;</c> parsing (<c>RuleStatementExtractor</c>) lives: it takes
/// prompt text already resolved from the store and has no path parameter to read a file from in the
/// first place, and this test is what keeps that true structurally rather than by convention.
/// </summary>
public sealed class RuleExtractionNeverReadsDiskTests
{
    const string RulesSourcePath = "src/AecoPostMortem.Rules";

    static readonly string[] ForbiddenPatterns =
    [
        "System.IO.File",
        "System.IO.Directory",
        "File.Read",
        "File.Open",
        "Directory.Enumerate",
        "Directory.GetFiles",
        "new FileStream",
        "new StreamReader",
    ];

    [Fact]
    public void No_source_under_AecoPostMortem_Rules_reads_a_file_from_disk()
    {
        var offenders = (
            from file in Repository.CSharpFiles(RulesSourcePath)
            from line in File.ReadLines(file.FullName).Select((text, index) => (Text: text, Number: index + 1))
            from forbidden in ForbiddenPatterns
            where line.Text.Contains(forbidden, StringComparison.Ordinal)
            select $"{Repository.RelativePath(file)}:{line.Number} {forbidden}: {line.Text.Trim()}").ToArray();

        Assert.True(
            offenders.Length == 0,
            "Rule-statement extraction must read only the ingested store, never a markdown file on "
            + "disk (issue #32, Scenario 3); found: " + string.Join("; ", offenders));
    }

    /// <summary>Proves the scan above is a real check rather than one that passes only because
    /// nothing under <c>AecoPostMortem.Rules</c> happens to match today.</summary>
    [Fact]
    public void The_scanner_is_not_vacuous()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "AecoPostMortem.Tests", "file-access-scan-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "Offender.cs"),
                "var text = System.IO.File.ReadAllText(path);\n");

            var offenders =
                from file in Directory.EnumerateFiles(tempDirectory, "*.cs")
                from line in File.ReadLines(file)
                from forbidden in ForbiddenPatterns
                where line.Contains(forbidden, StringComparison.Ordinal)
                select forbidden;

            Assert.Contains("System.IO.File", offenders);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
