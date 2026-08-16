using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AecoPostMortem.Containment.Tests;

/// <summary>
/// Reads the solution and its project files from disk. Deliberately not reflection: reflection sees
/// only assemblies this test project already references, and a project that has drifted outside the
/// containment rules is precisely the project this one will not reference.
/// </summary>
public static class Repository
{
    public const string SolutionFileName = "AecoPostMortem.sln";

    public static DirectoryInfo Root { get; } = FindRoot();

    static readonly Regex SolutionEntry = new(
        """^Project\("\{[^}]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+)",""",
        RegexOptions.Multiline);

    /// <summary>Every .csproj listed in the solution, repository-relative, with forward slashes.</summary>
    public static IReadOnlyList<string> SolutionProjectPaths { get; } = ReadSolutionProjectPaths();

    public static FileInfo ProjectFile(string relativePath) =>
        new(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The Include values of every item of the given name in a project file.</summary>
    public static IEnumerable<string> References(FileInfo project, string itemName) =>
        XDocument.Load(project.FullName)
            .Descendants(itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!);

    static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
    }

    static IReadOnlyList<string> ReadSolutionProjectPaths()
    {
        var text = File.ReadAllText(Path.Combine(Root.FullName, SolutionFileName));

        return SolutionEntry.Matches(text)
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
