using System.Reflection;
using System.Runtime.Versioning;

namespace AecoPostMortem.Cli.Tests;

/// <summary>
/// A thin test, and deliberately so: S-47 requires every test project to execute while the source
/// projects are still empty. It proves the reference is real and the target framework has not
/// drifted. S-01 replaces it with coverage of the store.
/// </summary>
public sealed class ProjectReferenceTests
{
    const string SubjectAssembly = "AecoPostMortem.Cli";

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
