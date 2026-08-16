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

    [Fact]
    public void No_project_references_an_AecoLedger_assembly()
    {
        var offenders = (
            from path in Repository.SolutionProjectPaths
            let project = Repository.ProjectFile(path)
            from reference in Repository.References(project, "PackageReference")
                .Concat(Repository.References(project, "ProjectReference"))
            where reference.Contains("AecoLedger", StringComparison.OrdinalIgnoreCase)
            select $"{path} -> {reference}").ToArray();

        Assert.True(
            offenders.Length == 0,
            "No project may reference an AecoLedger assembly (PRD §3.1); found: "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void No_project_reference_resolves_outside_the_repository()
    {
        var root = Repository.Root.FullName.TrimEnd(Path.DirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;

        var escapes = (
            from path in Repository.SolutionProjectPaths
            let project = Repository.ProjectFile(path)
            from include in Repository.References(project, "ProjectReference")
            let resolved = Path.GetFullPath(Path.Combine(
                project.DirectoryName!,
                include.Replace('\\', Path.DirectorySeparatorChar)
                       .Replace('/', Path.DirectorySeparatorChar)))
            where !resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            select $"{path} -> {include}").ToArray();

        Assert.True(
            escapes.Length == 0,
            "Every project reference must resolve inside this repository (PRD §3.1); found: "
            + string.Join("; ", escapes));
    }

    [Fact]
    public void Every_project_in_the_solution_lives_under_src_test_or_web()
    {
        // bench/bench.csproj is deliberately NOT in the solution. It is the harness from the
        // SQLite-versus-Postgres latency research, it sits at the repository root, and adding it
        // to the solution is exactly the violation this test exists to catch. Do not "fix" a
        // failure here by relaxing the rule.
        string[] allowed = ["src/", "test/", "web/"];

        var stray = Repository.SolutionProjectPaths
            .Where(path => !allowed.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            stray.Length == 0,
            "Every project in the solution must live under src, test or web (PRD §3.1); found: "
            + string.Join(", ", stray));
    }

    [Fact]
    public void The_rules_project_references_no_persistence_assembly()
    {
        var rules = Repository.ProjectFile("src/AecoPostMortem.Rules/AecoPostMortem.Rules.csproj");

        var packages = Repository.References(rules, "PackageReference").ToArray();
        var projects = Repository.References(rules, "ProjectReference").ToArray();

        Assert.True(
            packages.Length == 0 && projects.Length == 0,
            "AecoPostMortem.Rules must reference nothing at all — no package and no project "
            + "(PRD §3.1, FR-34): it takes plain inputs and returns results, and a project with "
            + "no dependencies has a very small surface in which a tool name could hide. Found: "
            + string.Join(", ", packages.Concat(projects)));
    }

    [Fact]
    public void The_frontend_lives_under_web_and_not_at_the_repository_root()
    {
        Assert.True(
            File.Exists(Path.Combine(Repository.Root.FullName, "web", "package.json")),
            "web/package.json is missing: the React project must build from web (PRD §3.1).");

        Assert.False(
            File.Exists(Path.Combine(Repository.Root.FullName, "package.json")),
            "A package.json at the repository root would let a frontend command run from there "
            + "(Repo Rule 3, PRD §3.1).");
    }
}
