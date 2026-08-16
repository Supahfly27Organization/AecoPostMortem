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

    [Fact]
    public void Every_source_project_has_a_test_project_in_the_solution()
    {
        var missing = SourceProjects
            .Where(name => !Repository.SolutionProjectPaths.Contains($"test/{name}.Tests/{name}.Tests.csproj"))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Source projects with no test project in {Repository.SolutionFileName}: {string.Join(", ", missing)}");
    }
}
