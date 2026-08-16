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

    /// <summary>
    /// A reference's value — a PackageReference/ProjectReference Include, or a Reference item's
    /// HintPath — together with the file that declared it. That file is the project itself, or a
    /// Directory.Build.props/.targets between the project and the repository root: a props file's
    /// reference applies to every project beneath it, so a message built from this should say which
    /// file it came from rather than naming only the project.
    /// </summary>
    public sealed record ReferenceEntry(string Value, FileInfo DeclaredIn);

    public static DirectoryInfo Root { get; } = FindRoot();

    static readonly Regex SolutionEntry = new(
        """^Project\("\{[^}]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+)",""",
        RegexOptions.Multiline);

    /// <summary>
    /// Every project of any type listed in the solution, repository-relative, with forward slashes.
    /// Used by the placement check: a stray .fsproj or .vbproj outside src/test/web is exactly the
    /// violation "any project in the solution sits outside src, test or web" describes, and it would
    /// be invisible to a check narrowed to .csproj.
    /// </summary>
    public static IReadOnlyList<string> AllProjectPaths { get; } = ReadSolutionProjectPaths(csprojOnly: false);

    /// <summary>
    /// The .csproj subset of <see cref="AllProjectPaths"/> — what the reference-parsing guards
    /// operate on, since they load each entry as an MSBuild XML file. Keeping this separate from
    /// <see cref="AllProjectPaths"/> means widening the placement check to other project types
    /// cannot break a guard that assumes XML it can parse as a C# project file.
    /// </summary>
    public static IReadOnlyList<string> SolutionProjectPaths { get; } = ReadSolutionProjectPaths(csprojOnly: true);

    public static FileInfo ProjectFile(string relativePath) =>
        new(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public static string RelativePath(FileInfo file) =>
        Path.GetRelativePath(Root.FullName, file.FullName).Replace('\\', '/');

    /// <summary>
    /// Every item of the given name's Include value, found in the project file itself and in every
    /// Directory.Build.props/.targets from the project's directory up to the repository root — a
    /// PackageReference set only in a props file reaches every project beneath it exactly as if it
    /// were written in the .csproj. Elements are matched by local name rather than
    /// <c>Descendants(itemName)</c>, so a project file that opts into the legacy MSBuild XML
    /// namespace cannot make the search match nothing and pass vacuously.
    /// </summary>
    public static IEnumerable<ReferenceEntry> References(FileInfo project, string itemName) =>
        from file in ProjectAndImportedFiles(project)
        from element in ItemsNamed(file, itemName)
        let include = element.Attribute("Include")?.Value
        where !string.IsNullOrWhiteSpace(include)
        select new ReferenceEntry(include!, file);

    /// <summary>
    /// The HintPath of every &lt;Reference&gt; item, found the same way as <see cref="References"/>.
    /// A raw assembly Reference with a HintPath is how a project can point outside the repository
    /// without ever using ProjectReference, which is why the escape check consults this too.
    /// </summary>
    public static IEnumerable<ReferenceEntry> ReferenceHintPaths(FileInfo project) =>
        from file in ProjectAndImportedFiles(project)
        from element in ItemsNamed(file, "Reference")
        let hintPath = HintPathOf(element)
        where !string.IsNullOrWhiteSpace(hintPath)
        select new ReferenceEntry(hintPath!, file);

    /// <summary>Formats an occurrence for a failure message, naming the declaring props file when the
    /// reference did not come from the project's own .csproj.</summary>
    public static string Describe(string projectPath, ReferenceEntry entry)
    {
        var declaredInProject = string.Equals(
            entry.DeclaredIn.FullName,
            ProjectFile(projectPath).FullName,
            StringComparison.OrdinalIgnoreCase);

        return declaredInProject
            ? $"{projectPath} -> {entry.Value}"
            : $"{projectPath} -> {entry.Value} (via {RelativePath(entry.DeclaredIn)})";
    }

    static string? HintPathOf(XElement reference) =>
        reference.Attribute("HintPath")?.Value
        ?? reference.Elements().FirstOrDefault(child => child.Name.LocalName == "HintPath")?.Value;

    static IEnumerable<XElement> ItemsNamed(FileInfo file, string itemName) =>
        XDocument.Load(file.FullName).Descendants().Where(element => element.Name.LocalName == itemName);

    /// <summary>The project file itself, then every Directory.Build.props and Directory.Build.targets
    /// found walking from the project's directory up to (and including) the repository root — the
    /// same set MSBuild would fold into that project's evaluation.</summary>
    static IEnumerable<FileInfo> ProjectAndImportedFiles(FileInfo project)
    {
        yield return project;

        var root = Root.FullName.TrimEnd(Path.DirectorySeparatorChar);
        var directory = project.Directory;

        while (directory is not null)
        {
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                var candidate = new FileInfo(Path.Combine(directory.FullName, name));
                if (candidate.Exists)
                {
                    yield return candidate;
                }
            }

            if (string.Equals(
                    directory.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            directory = directory.Parent;
        }
    }

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

    static IReadOnlyList<string> ReadSolutionProjectPaths(bool csprojOnly)
    {
        var text = File.ReadAllText(Path.Combine(Root.FullName, SolutionFileName));

        var paths = SolutionEntry.Matches(text)
            .Select(match => match.Groups[1].Value.Replace('\\', '/'));

        // Solution-folder pseudo-entries (e.g. "src", "test") share the same grammar as project
        // entries but carry a bare name with no extension — Path.HasExtension excludes them from the
        // "any project type" set the same way ".csproj"-only filtering already excluded them.
        return (csprojOnly
                ? paths.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                : paths.Where(Path.HasExtension))
            .ToArray();
    }
}
