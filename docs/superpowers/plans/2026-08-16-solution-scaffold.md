# Solution Scaffold and Command Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the AecoPostMortem solution, its thirteen projects and the four-command CLI surface, with the PRD §3.1 containment rule enforced by a test rather than by convention.

**Architecture:** One solution at the repository root holds six source projects under `src/` and seven test projects under `test/`; the React app lives in `web/` and builds only from there. `AecoPostMortem.Containment.Tests` reads the `.sln` and every `.csproj` **as files on disk** — not via reflection — because a project that has drifted outside the rules is exactly the project a test assembly will not reference. The CLI is driven by a `CommandSpec` table that renders the listing and dispatches the invocation, so the two cannot drift apart.

**Tech Stack:** .NET 10 (SDK 10.0.400), C#, **xUnit v3** (`xunit.v3` 3.2.2) with `xunit.runner.visualstudio` 3.1.4 and Microsoft.NET.Test.Sdk 17.14.1; React + TypeScript + Vite (Node 22.19.0, npm 11.12.1). No third-party CLI parser.

**On xUnit v3:** the SDK's `dotnet new xunit` template still emits v2, and there is no `xunit3` short name installed. Every test project in this plan is therefore hand-authored from the `.csproj` blocks below — do not generate them with `dotnet new`. This exact combination was probe-built on this machine before the plan was written: restore, build and `dotnet test` all succeed on `net10.0` with `TreatWarningsAsErrors`.

**Spec:** `docs/superpowers/specs/2026-08-16-solution-scaffold-design.md`

**Story:** S-47, [issue #10](https://github.com/Supahfly27Organization/AecoPostMortem/issues/10) · **Epic:** E1, [issue #1](https://github.com/Supahfly27Organization/AecoPostMortem/issues/1)

## Global Constraints

- **Target framework `net10.0` everywhere.** Set once in `src/Directory.Build.props` and `test/Directory.Build.props`; no `.csproj` declares its own.
- **xUnit v3, and every test project is an `Exe`.** xUnit v3 self-hosts, so `OutputType=Exe` is mandatory and is set once in `test/Directory.Build.props`. Test projects declare no `Main` — xunit.v3 generates the entry point.
- **Do not create a `Directory.Build.props` or `Directory.Packages.props` at the repository root.** `bench/bench.csproj` sits at the root and is not in the solution; a root-level props file would reach it and change how it builds. This deviates from spec §2, which placed both at the root — the collision with `bench` was found while planning. Shared package versions live in `test/Directory.Build.props` instead, which is where all seven test projects and none of the source projects need them.
- **`bench/bench.csproj` is never added to the solution.** It would violate the "no project outside `src`, `test`, `web`" rule.
- **No project may reference an `AecoLedger` assembly, in either direction** (PRD §3.1).
- **`AecoPostMortem.Rules` references nothing at all** — no package and no project (PRD §3.1's
  "Rules reaching nothing", FR-34).
- **Project reference direction:** `Cli → Api, Findings, Ingestion`; `Api → Findings`; `Findings → Rules, Data`; `Ingestion → Data`; `Rules →` nothing; `Data →` nothing.
- **No frontend command runs from the repository root** (Repo Rule 3). There is no `package.json` at the root.
- **No EF Core model, `DbContext` or migration in this plan** — that is S-01, and Repo Rule 4 governs it.
- **No endpoint, route or real web shell content** — that is S-48.
- **`dotnet test` never shells out to `npm`.**
- **Warnings are errors** in `src/` and `test/`.
- Commit after every task. Use the repository's committer: `git -c user.name=david -c user.email=David@benoualid.org commit`.

---

## File Structure

| File | Responsibility |
|---|---|
| `AecoPostMortem.sln` | The only solution. Membership is what the containment test reads. |
| `src/Directory.Build.props` | TFM, nullable, warnings-as-errors for every source project. |
| `test/Directory.Build.props` | The same, plus the xUnit package set shared by all seven test projects. |
| `src/AecoPostMortem.{Data,Ingestion,Rules,Findings,Api}/*.csproj` | Empty class libraries; the stories that fill them are S-01 onward. |
| `src/AecoPostMortem.Cli/CommandSpec.cs` | The record: name, arguments, output channel, summary, implementing story. |
| `src/AecoPostMortem.Cli/CommandSurface.cs` | The table — the single source of truth for what commands exist. |
| `src/AecoPostMortem.Cli/CommandParser.cs` | Pure `string[] → ParsedInvocation`. No I/O. |
| `src/AecoPostMortem.Cli/CommandListing.cs` | Renders the table to an injected `TextWriter`. |
| `src/AecoPostMortem.Cli/CommandRunner.cs` | Dispatch and exit codes. Injected writers, so tests run in-process. |
| `src/AecoPostMortem.Cli/Program.cs` | `Main` only; wires `Console.Out` / `Console.Error` into `CommandRunner`. |
| `test/AecoPostMortem.Containment.Tests/Repository.cs` | Locates the repo root, parses the `.sln`, reads references out of `.csproj` files. |
| `test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs` | The seven structural assertions. |
| `test/AecoPostMortem.{Data,Ingestion,Rules,Findings,Api}.Tests/ProjectReferenceTests.cs` | One smoke test each: the subject assembly is referenced and targets `net10.0`. |
| `test/AecoPostMortem.Cli.Tests/*.cs` | Real tests for the parser, the listing and the runner. |
| `web/` | The Vite React + TypeScript app. |
| `scripts/build-web.ps1` | Runs `npm ci && npm run build` with `web/` as the working directory. |

`.gitignore` needs no change: it already ignores `[Bb]in/`, `[Oo]bj/`, `node_modules/` and `dist/`.

---

## Task 1: The solution, the build props, and the six source projects

**Files:**
- Create: `AecoPostMortem.sln`
- Create: `src/Directory.Build.props`, `test/Directory.Build.props`
- Create: `src/AecoPostMortem.Data/AecoPostMortem.Data.csproj`, and the same for `Ingestion`, `Rules`, `Findings`, `Api`, `Cli`
- Test: `test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj`, `test/AecoPostMortem.Containment.Tests/Repository.cs`, `test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AecoPostMortem.Containment.Tests.Repository` with `static DirectoryInfo Root`, `static string SolutionFileName`, `static IReadOnlyList<string> SolutionProjectPaths` (repository-relative, forward slashes), `static FileInfo ProjectFile(string relativePath)`, `static IEnumerable<string> References(FileInfo project, string itemName)`. Tasks 2, 3 and 5 add assertions that use these.

- [ ] **Step 1: Create the solution and the containment test project**

There is no solution yet, so the test project has to exist before the test can run. Create only these two things in this step.

```powershell
dotnet new sln -n AecoPostMortem --format sln
New-Item -ItemType Directory -Force test/AecoPostMortem.Containment.Tests | Out-Null
```

`--format sln` is required: SDK 10.0.400's `dotnet new sln` defaults to the newer `.slnx` format, and both the PRD and the story's acceptance criteria name `AecoPostMortem.sln`. The containment test looks for that filename when it walks up to find the repository root.

- [ ] **Step 2: Write `test/Directory.Build.props`**

This is where the xUnit versions live — one file for all seven test projects, so no test `.csproj` carries a `Version` attribute. `OutputType=Exe` is required by xUnit v3, which self-hosts rather than being loaded by a runner.

```xml
<Project>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write `src/Directory.Build.props`**

```xml
<Project>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

</Project>
```

- [ ] **Step 4: Write the containment test project file**

It references no source project on purpose — it reads files, so referencing what it inspects would defeat the point.

`test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

</Project>
```

Then add it to the solution:

```powershell
dotnet sln add test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj
```

- [ ] **Step 5: Write `Repository.cs`**

`test/AecoPostMortem.Containment.Tests/Repository.cs`:

```csharp
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

    // Declared before the properties whose initializers use it: C# runs static field and property
    // initializers in textual order, so a regex declared below SolutionProjectPaths would still be
    // null when SolutionProjectPaths is built.
    static readonly Regex SolutionEntry = new(
        """^Project\("\{[^}]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+)",""",
        RegexOptions.Multiline);

    public static DirectoryInfo Root { get; } = FindRoot();

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
```

- [ ] **Step 6: Write the first failing test**

`test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs`:

```csharp
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
```

- [ ] **Step 7: Run it and confirm it fails for the right reason**

Run: `dotnet test test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj`

Expected: FAIL — `Not present in AecoPostMortem.sln: AecoPostMortem.Data, AecoPostMortem.Ingestion, AecoPostMortem.Rules, AecoPostMortem.Findings, AecoPostMortem.Api, AecoPostMortem.Cli`.

If instead it fails with "AecoPostMortem.sln was not found above ...", the root walk is wrong — fix that before continuing.

- [ ] **Step 8: Create the five class libraries**

Each is this exact file, with the name substituted. Everything else comes from `src/Directory.Build.props`.

`src/AecoPostMortem.Data/AecoPostMortem.Data.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

</Project>
```

`src/AecoPostMortem.Ingestion/AecoPostMortem.Ingestion.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\AecoPostMortem.Data\AecoPostMortem.Data.csproj" />
  </ItemGroup>

</Project>
```

`src/AecoPostMortem.Rules/AecoPostMortem.Rules.csproj` — references nothing, and that is the invariant:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    AecoPostMortem.Rules references no persistence assembly and no other project. PRD §3.1 calls
    this the non-negotiable invariant: a project with no persistence dependency has a very small
    surface in which a tool name could hide. Adding a reference here breaks a containment test.
  -->

</Project>
```

`src/AecoPostMortem.Findings/AecoPostMortem.Findings.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\AecoPostMortem.Rules\AecoPostMortem.Rules.csproj" />
    <ProjectReference Include="..\AecoPostMortem.Data\AecoPostMortem.Data.csproj" />
  </ItemGroup>

</Project>
```

`src/AecoPostMortem.Api/AecoPostMortem.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\AecoPostMortem.Findings\AecoPostMortem.Findings.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 9: Create the CLI project**

`src/AecoPostMortem.Cli/AecoPostMortem.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\AecoPostMortem.Api\AecoPostMortem.Api.csproj" />
    <ProjectReference Include="..\AecoPostMortem.Findings\AecoPostMortem.Findings.csproj" />
    <ProjectReference Include="..\AecoPostMortem.Ingestion\AecoPostMortem.Ingestion.csproj" />
  </ItemGroup>

</Project>
```

An `Exe` with no `Main` does not compile, so add the smallest one that will be replaced in Task 4.

`src/AecoPostMortem.Cli/Program.cs`:

```csharp
namespace AecoPostMortem.Cli;

public static class Program
{
    public static int Main(string[] args) => 0;
}
```

- [ ] **Step 10: Add all six to the solution**

```powershell
dotnet sln add src/AecoPostMortem.Data/AecoPostMortem.Data.csproj `
               src/AecoPostMortem.Ingestion/AecoPostMortem.Ingestion.csproj `
               src/AecoPostMortem.Rules/AecoPostMortem.Rules.csproj `
               src/AecoPostMortem.Findings/AecoPostMortem.Findings.csproj `
               src/AecoPostMortem.Api/AecoPostMortem.Api.csproj `
               src/AecoPostMortem.Cli/AecoPostMortem.Cli.csproj
```

- [ ] **Step 11: Run the test and the build**

Run: `dotnet build AecoPostMortem.sln` then `dotnet test AecoPostMortem.sln`

Expected: build succeeds; `Solution_contains_every_source_project` PASSES.

`Program.cs` with an unused `args` parameter must not produce a warning; if `TreatWarningsAsErrors` fails the build here, read the error rather than disabling the setting.

- [ ] **Step 12: Commit**

```bash
git add AecoPostMortem.sln src/ test/
git commit -m "Create the solution and its six source projects, with membership under test"
```

---

## Task 2: A test project for every source project

**Files:**
- Create: `test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj` and `ProjectReferenceTests.cs`, and the same for `Ingestion`, `Rules`, `Findings`, `Api`, `Cli`
- Modify: `test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs`
- Modify: `AecoPostMortem.sln`

**Interfaces:**
- Consumes: `Repository.SolutionProjectPaths`, `SolutionContainmentTests.SourceProjects` from Task 1.
- Produces: six test projects, each referencing exactly its subject source project.

- [ ] **Step 1: Write the failing test**

Append to `test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs`, inside the class:

```csharp
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
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj --filter FullyQualifiedName~Every_source_project_has_a_test_project`

Filter against the containment project, not the solution: a `--filter` that matches nothing in some other test project makes `dotnet test` report a failure that is about the filter rather than about the code.

Expected: FAIL listing all six source projects.

- [ ] **Step 3: Create the six test projects**

Each `.csproj` is this file with the name substituted — packages and settings come from `test/Directory.Build.props`.

`test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\..\src\AecoPostMortem.Data\AecoPostMortem.Data.csproj" />
  </ItemGroup>

</Project>
```

Repeat for `AecoPostMortem.Ingestion.Tests`, `AecoPostMortem.Rules.Tests`, `AecoPostMortem.Findings.Tests`, `AecoPostMortem.Api.Tests` and `AecoPostMortem.Cli.Tests`, each pointing at its own source project.

- [ ] **Step 4: Write the smoke test in each of the five library test projects**

`test/AecoPostMortem.Data.Tests/ProjectReferenceTests.cs`:

```csharp
using System.Reflection;
using System.Runtime.Versioning;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// A thin test, and deliberately so: S-47 requires every test project to execute while the source
/// projects are still empty. It proves the reference is real and the target framework has not
/// drifted. S-01 replaces it with coverage of the store.
/// </summary>
public sealed class ProjectReferenceTests
{
    const string SubjectAssembly = "AecoPostMortem.Data";

    [Fact]
    public void The_subject_assembly_is_referenced_and_targets_net10()
    {
        var path = Path.Combine(AppContext.BaseDirectory, SubjectAssembly + ".dll");

        Assert.True(
            File.Exists(path),
            $"{SubjectAssembly}.dll is not in the test output; the ProjectReference is missing.");

        var assembly = Assembly.LoadFrom(path);

        Assert.Equal(SubjectAssembly, assembly.GetName().Name);
        Assert.Equal(
            ".NETCoreApp,Version=v10.0",
            assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }
}
```

Repeat verbatim in `AecoPostMortem.Ingestion.Tests`, `AecoPostMortem.Rules.Tests`, `AecoPostMortem.Findings.Tests`, `AecoPostMortem.Api.Tests` **and `AecoPostMortem.Cli.Tests`**, changing only the namespace and `SubjectAssembly`.

`AecoPostMortem.Cli.Tests` gets one too, even though Task 4 fills it with real tests: a test project containing zero tests makes `dotnet test` report "no test is available" and return a non-zero exit code, which would make Step 6 below fail for a reason that has nothing to do with the code.

- [ ] **Step 5: Add the six to the solution**

```powershell
dotnet sln add test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj `
               test/AecoPostMortem.Ingestion.Tests/AecoPostMortem.Ingestion.Tests.csproj `
               test/AecoPostMortem.Rules.Tests/AecoPostMortem.Rules.Tests.csproj `
               test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj `
               test/AecoPostMortem.Api.Tests/AecoPostMortem.Api.Tests.csproj `
               test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test AecoPostMortem.sln`

Expected: PASS. Seven test projects discovered; eight tests run — two containment assertions plus six smoke tests.

- [ ] **Step 7: Commit**

```bash
git add test/ AecoPostMortem.sln
git commit -m "Give every source project a test project, and put that rule under test"
```

---

## Task 3: The containment rules, each proven to actually catch a violation

**Files:**
- Modify: `test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs`

**Interfaces:**
- Consumes: `Repository.References`, `Repository.ProjectFile`, `Repository.Root`, `Repository.SolutionProjectPaths`.
- Produces: nothing other tasks consume.

These are guard tests: they pass the moment they are written, because the solution is already clean. A guard that has never been seen to fire is not known to work, so each step below deliberately introduces the violation, watches the test go red, and reverts.

- [ ] **Step 1: Write the four remaining structural assertions**

Append to `SolutionContainmentTests`:

```csharp
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
```

- [ ] **Step 2: Run them and confirm they pass on a clean tree**

Run: `dotnet test AecoPostMortem.sln`

Expected: PASS.

- [ ] **Step 3: Prove the AecoLedger guard fires**

Temporarily add to `src/AecoPostMortem.Findings/AecoPostMortem.Findings.csproj`, inside the existing `ItemGroup`:

```xml
    <PackageReference Include="AecoLedger.Core" Version="1.0.0" />
```

Run: `dotnet test test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj --filter FullyQualifiedName~No_project_references_an_AecoLedger`

Expected: FAIL — `... found: src/AecoPostMortem.Findings/AecoPostMortem.Findings.csproj -> AecoLedger.Core`.

Then **remove that line** and re-run; expected PASS.

- [ ] **Step 4: Prove the escape guard fires**

Temporarily change the `ProjectReference` in `src/AecoPostMortem.Api/AecoPostMortem.Api.csproj` to:

```xml
    <ProjectReference Include="..\..\..\Elsewhere\Elsewhere.csproj" />
```

Run: `dotnet test test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj --filter FullyQualifiedName~No_project_reference_resolves_outside`

Expected: FAIL naming that reference.

This works because the containment test project references no source project — it reads them as files. `AecoPostMortem.Api` is therefore never built by this command, so the dangling reference cannot break the build before the test gets to report it. That is the same property that makes the test trustworthy in the first place. Running the same filter against the whole solution would fail at build time instead, and prove nothing.

Then **restore the original line** and re-run; expected PASS.

- [ ] **Step 5: Prove the stray-project guard fires**

```powershell
dotnet sln add bench/bench.csproj
dotnet test test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj --filter FullyQualifiedName~Every_project_in_the_solution_lives_under
```

Expected: FAIL — `... found: bench/bench.csproj`.

Then:

```powershell
dotnet sln remove bench/bench.csproj
```

Re-run; expected PASS. Confirm with `git diff AecoPostMortem.sln` that the solution file is back to its committed state.

- [ ] **Step 6: Prove the Rules guard fires — twice**

This assertion is an allowlist of size zero, not a denylist of known-bad packages, so it needs two demonstrations: one for each way a dependency can arrive.

First, temporarily add to `src/AecoPostMortem.Rules/AecoPostMortem.Rules.csproj`:

```xml
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.3" />
  </ItemGroup>
```

`Npgsql` rather than an EF Core package on purpose: a denylist of persistence prefixes would have missed it, and `bench/bench.csproj` in this repository already references it, so it is the realistic case rather than the convenient one.

Run: `dotnet test test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj --filter FullyQualifiedName~The_rules_project_references_no_persistence`

Expected: FAIL naming `Npgsql`. Remove it and re-run; expected PASS.

Then temporarily add a project reference instead:

```xml
  <ItemGroup>
    <ProjectReference Include="..\AecoPostMortem.Findings\AecoPostMortem.Findings.csproj" />
  </ItemGroup>
```

Expected: FAIL naming the `Findings` reference. This case would create a reference cycle and break a solution build, which is precisely why the containment project — referencing nothing it inspects — is the only thing that can observe it. Remove it and re-run; expected PASS.

- [ ] **Step 7: Confirm the tree is clean and the suite is green**

Run: `git status --porcelain` — expected: only `test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs` modified.

Run: `dotnet test AecoPostMortem.sln` — expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs
git commit -m "Enforce containment with tests, each one watched to fail before it was trusted"
```

---

## Task 4: The command surface

**Files:**
- Create: `src/AecoPostMortem.Cli/CommandSpec.cs`, `CommandSurface.cs`, `CommandParser.cs`, `CommandListing.cs`, `CommandRunner.cs`
- Modify: `src/AecoPostMortem.Cli/Program.cs`
- Test: `test/AecoPostMortem.Cli.Tests/CommandSurfaceTests.cs`, `CommandParserTests.cs`, `CommandListingTests.cs`, `CommandRunnerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `public sealed record CommandSpec(string Name, string Arguments, string OutputChannel, string Summary, string ArrivesWith)`
  - `public static class CommandSurface` — `static IReadOnlyList<CommandSpec> Commands`, `static CommandSpec? Find(string name)`
  - `public sealed record ParsedInvocation(CommandSpec? Command, string? UnrecognisedName, IReadOnlyList<string> Arguments)` with `bool ShowsListing`
  - `public static class CommandParser` — `static ParsedInvocation Parse(IReadOnlyList<string> arguments)`
  - `public static class CommandListing` — `static void Write(TextWriter writer)`
  - `public static class CommandRunner` — `const int Success = 0`, `const int UnrecognisedCommand = 2`, `static int Run(IReadOnlyList<string> arguments, TextWriter stdout, TextWriter stderr)`

- [ ] **Step 1: Write the surface test**

`test/AecoPostMortem.Cli.Tests/CommandSurfaceTests.cs`:

```csharp
namespace AecoPostMortem.Cli.Tests;

public sealed class CommandSurfaceTests
{
    [Fact]
    public void The_surface_is_exactly_the_four_commands_FR_58_enumerates()
    {
        Assert.Equal(
            new[] { "ingest", "rebuild", "purge", "serve" },
            CommandSurface.Commands.Select(command => command.Name));
    }

    [Theory]
    [InlineData("ingest")]
    [InlineData("rebuild")]
    [InlineData("purge")]
    [InlineData("serve")]
    public void Every_command_states_its_output_channel_and_what_it_does(string name)
    {
        var command = CommandSurface.Find(name);

        Assert.NotNull(command);
        Assert.False(string.IsNullOrWhiteSpace(command!.OutputChannel));
        Assert.False(string.IsNullOrWhiteSpace(command.Summary));
        Assert.False(string.IsNullOrWhiteSpace(command.ArrivesWith));
    }

    [Fact]
    public void Ingest_takes_an_optional_path_and_serve_an_optional_port()
    {
        Assert.Equal("[path]", CommandSurface.Find("ingest")!.Arguments);
        Assert.Equal("[--port <n>]", CommandSurface.Find("serve")!.Arguments);
    }

    [Fact]
    public void Command_lookup_ignores_case()
    {
        Assert.NotNull(CommandSurface.Find("INGEST"));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj`

Expected: FAIL to compile — `The name 'CommandSurface' does not exist`.

- [ ] **Step 3: Write the table**

`src/AecoPostMortem.Cli/CommandSpec.cs`:

```csharp
namespace AecoPostMortem.Cli;

/// <summary>One command on the surface FR-58 enumerates.</summary>
/// <param name="Name">The word the operator types.</param>
/// <param name="Arguments">Its arguments as the listing shows them; empty when it takes none.</param>
/// <param name="OutputChannel">Where its output goes, and what that output is.</param>
/// <param name="Summary">What it does, in one line.</param>
/// <param name="ArrivesWith">The story that implements it. S-47 ships the surface, not the behaviour.</param>
public sealed record CommandSpec(
    string Name,
    string Arguments,
    string OutputChannel,
    string Summary,
    string ArrivesWith);
```

`src/AecoPostMortem.Cli/CommandSurface.cs`:

```csharp
namespace AecoPostMortem.Cli;

/// <summary>
/// The single source of truth for what commands exist. The listing is rendered from this table and
/// invocations are dispatched from it, so a command cannot exist without being documented.
/// </summary>
public static class CommandSurface
{
    public static IReadOnlyList<CommandSpec> Commands { get; } =
    [
        new(
            "ingest",
            "[path]",
            "stdout — the coverage report",
            "Read the Copilot session state and re-derive from it.",
            "the ingestion stories in E1"),
        new(
            "rebuild",
            "",
            "stdout — the re-derivation summary",
            "Re-derive the normalized and findings layers from RAW.",
            "the ingestion stories in E1"),
        new(
            "purge",
            "",
            "stdout — what was deleted",
            "Delete the local store.",
            "S-01 (local store and its governance)"),
        new(
            "serve",
            "[--port <n>]",
            "stdout — the listening URL",
            "Start the local API and web shell.",
            "S-48 (API host, web shell and the zero-data state)"),
    ];

    public static CommandSpec? Find(string name) =>
        Commands.FirstOrDefault(
            command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run the surface tests**

Run: `dotnet test test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Write the parser tests**

`test/AecoPostMortem.Cli.Tests/CommandParserTests.cs`:

```csharp
namespace AecoPostMortem.Cli.Tests;

public sealed class CommandParserTests
{
    [Fact]
    public void No_arguments_asks_for_the_listing()
    {
        var invocation = CommandParser.Parse([]);

        Assert.True(invocation.ShowsListing);
        Assert.Null(invocation.Command);
        Assert.Null(invocation.UnrecognisedName);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Asking_for_help_asks_for_the_listing(string word)
    {
        Assert.True(CommandParser.Parse([word]).ShowsListing);
    }

    [Fact]
    public void A_known_command_carries_its_remaining_arguments()
    {
        var invocation = CommandParser.Parse(["ingest", "C:/copilot/session-state"]);

        Assert.Equal("ingest", invocation.Command?.Name);
        Assert.Equal(new[] { "C:/copilot/session-state" }, invocation.Arguments);
    }

    [Fact]
    public void An_unknown_command_is_reported_by_name()
    {
        var invocation = CommandParser.Parse(["digest"]);

        Assert.Equal("digest", invocation.UnrecognisedName);
        Assert.Null(invocation.Command);
        Assert.False(invocation.ShowsListing);
    }

    [Fact]
    public void Blank_arguments_are_not_a_command()
    {
        Assert.True(CommandParser.Parse(["", "   "]).ShowsListing);
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj`

Expected: FAIL to compile — `The name 'CommandParser' does not exist`.

- [ ] **Step 7: Write the parser**

`src/AecoPostMortem.Cli/CommandParser.cs`:

```csharp
namespace AecoPostMortem.Cli;

/// <summary>
/// What the operator asked for. Exactly one of the three states holds: a command, an unrecognised
/// word, or a request for the listing.
/// </summary>
public sealed record ParsedInvocation(
    CommandSpec? Command,
    string? UnrecognisedName,
    IReadOnlyList<string> Arguments)
{
    public bool ShowsListing => Command is null && UnrecognisedName is null;
}

/// <summary>Pure: no console, no environment, no file system. That is what makes it testable.</summary>
public static class CommandParser
{
    static readonly string[] HelpWords = ["help", "--help", "-h", "-?", "/?"];

    public static ParsedInvocation Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var words = arguments.Where(word => !string.IsNullOrWhiteSpace(word)).ToArray();

        if (words.Length == 0 || HelpWords.Contains(words[0], StringComparer.OrdinalIgnoreCase))
        {
            return new ParsedInvocation(null, null, []);
        }

        var command = CommandSurface.Find(words[0]);

        return command is null
            ? new ParsedInvocation(null, words[0], [])
            : new ParsedInvocation(command, null, words[1..]);
    }
}
```

- [ ] **Step 8: Run the parser tests**

Run: `dotnet test test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj`

Expected: PASS.

- [ ] **Step 9: Write the listing test — this is the acceptance criterion**

`test/AecoPostMortem.Cli.Tests/CommandListingTests.cs`:

```csharp
namespace AecoPostMortem.Cli.Tests;

public sealed class CommandListingTests
{
    static string Render()
    {
        var writer = new StringWriter();
        CommandListing.Write(writer);
        return writer.ToString();
    }

    [Theory]
    [InlineData("ingest")]
    [InlineData("rebuild")]
    [InlineData("purge")]
    [InlineData("serve")]
    public void Every_command_is_listed_with_its_arguments_and_its_output_channel(string name)
    {
        var command = CommandSurface.Find(name)!;
        var listing = Render();

        Assert.Contains(command.Name, listing);
        Assert.Contains(command.OutputChannel, listing);

        if (command.Arguments.Length > 0)
        {
            Assert.Contains($"{command.Name} {command.Arguments}", listing);
        }
    }

    [Fact]
    public void The_listing_is_generated_from_the_table_so_it_cannot_omit_a_command()
    {
        var listing = Render();

        Assert.All(CommandSurface.Commands, command => Assert.Contains(command.Summary, listing));
    }
}
```

- [ ] **Step 10: Run to verify it fails**

Run: `dotnet test test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj`

Expected: FAIL to compile — `The name 'CommandListing' does not exist`.

- [ ] **Step 11: Write the listing**

`src/AecoPostMortem.Cli/CommandListing.cs`:

```csharp
namespace AecoPostMortem.Cli;

/// <summary>Renders <see cref="CommandSurface.Commands"/>. FR-58 requires each command to appear
/// with its arguments and its output channel, so both come from the table rather than from prose.</summary>
public static class CommandListing
{
    public static void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("AecoPostMortem — reads GitHub Copilot CLI session logs and reports where a");
        writer.WriteLine("session diverged from the process it was given.");
        writer.WriteLine();
        writer.WriteLine("Usage: aecopostmortem <command> [arguments]");
        writer.WriteLine();

        var invocations = CommandSurface.Commands
            .Select(command => command.Arguments.Length == 0
                ? command.Name
                : $"{command.Name} {command.Arguments}")
            .ToArray();

        var width = invocations.Max(invocation => invocation.Length);

        foreach (var (command, invocation) in CommandSurface.Commands.Zip(invocations))
        {
            writer.WriteLine($"  {invocation.PadRight(width)}   {command.Summary}");
            writer.WriteLine($"  {new string(' ', width)}   output: {command.OutputChannel}");
            writer.WriteLine();
        }
    }
}
```

- [ ] **Step 12: Run the listing tests**

Run: `dotnet test test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj`

Expected: PASS.

- [ ] **Step 13: Write the runner tests**

`test/AecoPostMortem.Cli.Tests/CommandRunnerTests.cs`:

```csharp
namespace AecoPostMortem.Cli.Tests;

public sealed class CommandRunnerTests
{
    static (int ExitCode, string Stdout, string Stderr) Run(params string[] arguments)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CommandRunner.Run(arguments, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void With_no_arguments_it_lists_the_commands_on_stdout_and_succeeds()
    {
        var (exitCode, stdout, stderr) = Run();

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("ingest", stdout);
        Assert.Contains("rebuild", stdout);
        Assert.Contains("purge", stdout);
        Assert.Contains("serve", stdout);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void Serve_reports_what_is_not_yet_implemented_rather_than_failing()
    {
        var (exitCode, stdout, stderr) = Run("serve");

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("not implemented yet", stdout);
        Assert.Contains("S-48", stdout);
        Assert.Equal(string.Empty, stderr);
    }

    [Theory]
    [InlineData("ingest")]
    [InlineData("rebuild")]
    [InlineData("purge")]
    public void The_other_commands_report_the_same_way(string name)
    {
        var (exitCode, stdout, _) = Run(name);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("not implemented yet", stdout);
    }

    [Fact]
    public void An_unknown_command_goes_to_stderr_with_a_non_zero_exit_code()
    {
        var (exitCode, stdout, stderr) = Run("digest");

        Assert.Equal(CommandRunner.UnrecognisedCommand, exitCode);
        Assert.Contains("digest", stderr);
        Assert.Contains("ingest", stderr);
        Assert.Equal(string.Empty, stdout);
    }
}
```

- [ ] **Step 14: Run to verify it fails**

Run: `dotnet test test/AecoPostMortem.Cli.Tests/AecoPostMortem.Cli.Tests.csproj`

Expected: FAIL to compile — `The name 'CommandRunner' does not exist`.

- [ ] **Step 15: Write the runner and rewrite `Program`**

`src/AecoPostMortem.Cli/CommandRunner.cs`:

```csharp
namespace AecoPostMortem.Cli;

/// <summary>
/// Dispatch and exit codes. The writers are injected so the whole surface is testable in-process;
/// nothing here starts a child process or touches the console directly.
/// </summary>
public static class CommandRunner
{
    public const int Success = 0;
    public const int UnrecognisedCommand = 2;

    public static int Run(IReadOnlyList<string> arguments, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var invocation = CommandParser.Parse(arguments);

        if (invocation.UnrecognisedName is { } unrecognised)
        {
            stderr.WriteLine($"Unrecognised command '{unrecognised}'.");
            stderr.WriteLine();
            CommandListing.Write(stderr);
            return UnrecognisedCommand;
        }

        if (invocation.ShowsListing)
        {
            CommandListing.Write(stdout);
            return Success;
        }

        // S-47 delivers the surface, not the behaviour. Reporting and exiting zero is the
        // specified behaviour, not a placeholder: FR-58 requires the surface to enumerate itself
        // before anything behind it exists.
        var command = invocation.Command!;
        stdout.WriteLine($"'{command.Name}' is not implemented yet; it arrives with {command.ArrivesWith}.");
        stdout.WriteLine($"When it does, its output goes to {command.OutputChannel}.");
        return Success;
    }
}
```

`src/AecoPostMortem.Cli/Program.cs` — replace the whole file:

```csharp
namespace AecoPostMortem.Cli;

public static class Program
{
    public static int Main(string[] args) => CommandRunner.Run(args, Console.Out, Console.Error);
}
```

- [ ] **Step 16: Run the whole suite**

Run: `dotnet test AecoPostMortem.sln`

Expected: PASS.

- [ ] **Step 17: Run the CLI for real and read its output**

```powershell
dotnet run --project src/AecoPostMortem.Cli
dotnet run --project src/AecoPostMortem.Cli -- serve
dotnet run --project src/AecoPostMortem.Cli -- digest; $LASTEXITCODE
```

Expected: the listing showing all four commands with arguments and output channels; then `serve` reporting S-48; then the unrecognised-command message with exit code 2.

- [ ] **Step 18: Commit**

```bash
git add src/AecoPostMortem.Cli test/AecoPostMortem.Cli.Tests
git commit -m "Give the operator a command surface that enumerates itself"
```

---

## Task 5: The web shell scaffold

**Files:**
- Create: `web/` (Vite React + TypeScript scaffold, including `package-lock.json`)
- Create: `scripts/build-web.ps1`
- Modify: `test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs`

**Interfaces:**
- Consumes: `Repository.Root` from Task 1.
- Produces: `web/package.json` with a `build` script; `scripts/build-web.ps1`.

- [ ] **Step 1: Write the failing test**

Append to `SolutionContainmentTests`:

```csharp
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
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test test/AecoPostMortem.Containment.Tests/AecoPostMortem.Containment.Tests.csproj --filter FullyQualifiedName~The_frontend_lives_under_web`

Expected: FAIL — `web/package.json is missing`.

- [ ] **Step 3: Scaffold Vite inside `web/`**

Create the directory first and run the generator **from inside it**, so no frontend command is ever run from the repository root.

```powershell
New-Item -ItemType Directory -Force web | Out-Null
Push-Location web
npm create vite@latest . -- --template react-ts
Pop-Location
```

If `npm create vite@latest` prompts interactively (newer versions may offer a rolldown variant), cancel it and pin the generator instead:

```powershell
Push-Location web
npm create vite@6 . -- --template react-ts
Pop-Location
```

- [ ] **Step 4: Install and build, generating the lockfile**

```powershell
Push-Location web
npm install
npm run build
Pop-Location
```

Expected: `web/dist/` is produced. `web/node_modules/` and `web/dist/` are already ignored by `.gitignore`; `web/package-lock.json` is not, and must be committed.

- [ ] **Step 5: Write `scripts/build-web.ps1`**

```powershell
#!/usr/bin/env pwsh
# Builds the web shell.
#
# Frontend commands run from web/, never from the repository root (Repo Rule 3, PRD §3.1), which is
# why this script pushes into web/ rather than passing --prefix. dotnet test deliberately does not
# call it: that would make the .NET suite depend on Node and on node_modules being populated.

$ErrorActionPreference = 'Stop'

$web = Join-Path $PSScriptRoot '..' 'web'

Push-Location $web
try {
    if (Test-Path 'package-lock.json') { npm ci } else { npm install }
    if ($LASTEXITCODE -ne 0) { throw "npm install failed with exit code $LASTEXITCODE" }

    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

Write-Output 'web shell built.'
```

- [ ] **Step 6: Run the script and the test**

Run: `pwsh -File scripts/build-web.ps1`

Expected: it completes and prints `web shell built.`

Run: `dotnet test AecoPostMortem.sln`

Expected: PASS, including `The_frontend_lives_under_web_and_not_at_the_repository_root`.

- [ ] **Step 7: Confirm no `package.json` reached the repository root**

Run: `git status --porcelain`

Expected: no `package.json` at the root, and `web/node_modules` absent from the output.

- [ ] **Step 8: Commit**

```bash
git add web scripts/build-web.ps1 test/AecoPostMortem.Containment.Tests/SolutionContainmentTests.cs
git commit -m "Put the web shell in web/, where its only build command runs"
```

---

## Task 6: Module documentation and final verification

**Files:**
- Create: `src/AecoPostMortem.Data/CLAUDE.md`, and the same for `Ingestion`, `Rules`, `Findings`, `Api`, `Cli`
- Create: `web/CLAUDE.md`
- Modify: `CLAUDE.md` (repository root)

**Interfaces:**
- Consumes: everything built above.
- Produces: nothing consumed by code.

The repository's working rules require each module's `CLAUDE.md` to be created in the same change that creates the module, holding architecture and non-obvious decisions — not restating what the code says.

- [ ] **Step 1: Write one `CLAUDE.md` per source project**

Each is short. `src/AecoPostMortem.Rules/CLAUDE.md` is the one that matters most:

```markdown
# AecoPostMortem.Rules

Rule-set extraction and the check-shape catalogue: `<custom_instruction>` extraction, rule-set
versioning, tool-vocabulary and role derivation, operand resolution, the check shapes.

## The invariant

**Nothing here may name a tool, an MCP server or a repository** (FR-34, PRD §3.1). This is the one
requirement the operator called non-negotiable, and it is structural rather than conventional so
that one project's source proves it.

**This project references nothing** — no other project, and no persistence assembly. That is what
turns the invariant from an assertion into a test: a project with no persistence dependency has a
very small surface in which a tool name could hide. `Every_project_in_the_solution_lives_under_src_test_or_web`
and `The_rules_project_references_no_persistence_assembly` in
`test/AecoPostMortem.Containment.Tests` enforce it. Adding a reference here fails the build.

It takes plain inputs — rule statements as text, the discovered tool vocabulary as a list, call
counts as numbers — and returns results. `AecoPostMortem.Findings` does the orchestration, reading
through `AecoPostMortem.Data` and writing findings back.

## Status

Empty. S-47 created it; the check-shape catalogue arrives with E4, E5 and E6.
```

Write the other five in this shape:

```markdown
# <project name>

<purpose, taken from PRD §3.1>

## References

<the projects it references, and why that direction and not the other>

## Status

Empty. S-47 created it; <the story that fills it> populates it.
```

Fill it in from this table — the purpose column is PRD §3.1's own wording:

| Project | Purpose | References | Filled by |
|---|---|---|---|
| `Data` | The `DbContext`, the entity model and the EF Core migrations; the only project that owns the schema | nothing | S-01. Repo Rule 4: only RAW carries a migration |
| `Ingestion` | Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion | `Data` | S-02 through S-07 |
| `Findings` | The four finding classes, provenance, recurrence, the Monitor comparison, suggestions | `Rules`, `Data` — it does the orchestration `Rules` deliberately cannot | E3 |
| `Api` | Endpoints for the three surfaces | `Findings` | S-48 |
| `Cli` | The command surface FR-58 enumerates: `ingest`, `rebuild`, `purge`, `serve` | `Api`, `Findings`, `Ingestion` | the surface exists now; the behaviour behind each command arrives with the story named in its `CommandSpec.ArrivesWith` |

Note in `Data/CLAUDE.md` that RAW appends bypass EF Core change tracking with batched raw SQL, because
a measured 56,138 rows arrive in one full ingest (PRD §3.1) — that is the non-obvious decision a
reader of that project needs and cannot get from the code, since the code is not there yet.

`src/AecoPostMortem.Cli/CLAUDE.md` additionally documents the playbook, because adding a command is
a recurring procedure:

```markdown
## Playbook — adding a command

1. Add a `CommandSpec` to `CommandSurface.Commands`. Nothing else enumerates commands.
2. Add its name to `CommandSurfaceTests.The_surface_is_exactly_the_four_commands_FR_58_enumerates`
   and to the `[InlineData]` sets in `CommandListingTests` and `CommandRunnerTests`.
3. Implement dispatch in `CommandRunner.Run`.

The listing is rendered from the table, so a command cannot exist without being documented — do not
add a second place that lists commands.
```

- [ ] **Step 2: Write `web/CLAUDE.md`**

```markdown
# web

The React + TypeScript + Vite app: the digest, the session view and the Rules Inventory.

**All frontend commands run from here, never from the repository root** (Repo Rule 3, PRD §3.1).
There is no `package.json` at the repository root, and a containment test fails if one appears.
`scripts/build-web.ps1` is the scripted form of the build; it pushes into this directory rather than
passing `--prefix`, for the same reason.

`dotnet test` does not build this project — that would make the .NET suite depend on Node.

## Status

Vite scaffold only. Routing, the three surfaces and the two zero-data states arrive with S-48.
```

- [ ] **Step 3: Update the root `CLAUDE.md`**

In the **Layout** paragraph, replace the project list with one that includes the CLI and the
containment test project. In the **Task → Read These First** table, add:

```markdown
| Add a CLI command | `src/AecoPostMortem.Cli/CLAUDE.md` playbook → `CommandSurface.Commands` |
| Change the solution's shape | `test/AecoPostMortem.Containment.Tests/` — the rules are tests, not conventions |
```

Add to **Repo Rules**:

```markdown
8. `bench/bench.csproj` is deliberately outside the solution — it sits at the repository root, and
   adding it breaks the containment rule that every project lives under `src`, `test` or `web`.
9. Shared MSBuild settings live in `src/Directory.Build.props` and `test/Directory.Build.props`.
   Do not create one at the repository root: it would reach `bench/` and change how it builds.
```

- [ ] **Step 4: Run the documentation checker**

Run: `python scripts/check-claude-md.py --quiet`

It reports 45 pre-existing findings across 5 files that this story did not introduce. Confirm the
count has not risen because of the new files; do not treat the pre-existing findings as a gate.

- [ ] **Step 5: Full verification — every acceptance criterion, in order**

```powershell
dotnet build AecoPostMortem.sln
dotnet test AecoPostMortem.sln
dotnet run --project src/AecoPostMortem.Cli
pwsh -File scripts/build-web.ps1
git status --porcelain
```

Expected, mapped to the story's Gherkin:

| Scenario | Evidence |
|---|---|
| The solution builds and is contained | `dotnet build` succeeds from `AecoPostMortem.sln`; `No_project_references_an_AecoLedger_assembly` passes |
| The command surface exists | `dotnet run` with no arguments lists all four commands with arguments and output channel; `serve` reports rather than fails |
| The test projects exist and run | `dotnet test` discovers seven test projects and all execute |
| The frontend lives in web and builds from there | `scripts/build-web.ps1` succeeds; no root `package.json` |
| Containment is enforced, not conventional | all seven containment tests pass, and Task 3 watched four of them fail |

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md src/*/CLAUDE.md web/CLAUDE.md
git commit -m "Document each module as it is created, and record why bench stays out of the solution"
```

---

## Closing out

After Task 6, use the `github-issue-commit` skill for the closing commit so issue #10 moves to
"Waiting to review". S-47 unblocks S-01 and S-48.
