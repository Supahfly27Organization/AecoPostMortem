namespace AecoPostMortem.Containment.Tests;

public sealed class SolutionContainmentTests
{
    /// <summary>The five modules of PRD §3.1, plus the CLI the stories document adds.</summary>
    internal static readonly string[] SourceProjects =
    [
        "AecoPostMortem.Data",
        "AecoPostMortem.Ingestion",
        "AecoPostMortem.Rules",
        "AecoPostMortem.Findings",
        "AecoPostMortem.Api",
        "AecoPostMortem.Cli",
    ];

    [Fact]
    public void Solution_contains_every_source_project()
    {
        var missing = SourceProjects
            .Where(name => !Repository.SolutionProjectPaths.Contains($"src/{name}/{name}.csproj"))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Not present in {Repository.SolutionFileName}: {string.Join(", ", missing)}");
    }
}
