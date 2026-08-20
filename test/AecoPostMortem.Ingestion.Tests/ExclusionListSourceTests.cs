using System.Globalization;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-7's "operator-configured, not compiled in" half. <see cref="ExclusionListSource.Load"/>
/// re-reads the configured file every call, which is what makes Scenario 2 ("a path added to the
/// list is honoured without rebuilding the product") true structurally rather than by a mechanism
/// that has to be kept correct.
/// </summary>
public sealed class ExclusionListSourceTests : IDisposable
{
    readonly string root;

    public ExclusionListSourceTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "AecoPostMortem.Tests",
            Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void A_configured_file_is_honoured()
    {
        var path = Path.Combine(root, "exclusions.json");
        File.WriteAllText(path, """["C:\\repo\\AecoPostMortem"]""");

        var roots = ExclusionListSource.Load(path);

        Assert.Equal([@"C:\repo\AecoPostMortem"], roots);
    }

    /// <summary>Scenario 2 itself: editing the file on disk changes what the very next call
    /// returns, with no rebuild or restart in between.</summary>
    [Fact]
    public void Editing_the_file_changes_the_very_next_load_with_no_rebuild()
    {
        var path = Path.Combine(root, "exclusions.json");
        File.WriteAllText(path, "[]");
        Assert.Empty(ExclusionListSource.Load(path));

        File.WriteAllText(path, """["/home/op/other-repo"]""");

        Assert.Equal(["/home/op/other-repo"], ExclusionListSource.Load(path));
    }

    [Fact]
    public void An_explicit_empty_list_overrides_the_default_rather_than_falling_back_to_it()
    {
        var path = Path.Combine(root, "exclusions.json");
        File.WriteAllText(path, "[]");

        var roots = ExclusionListSource.Load(path, root);

        Assert.Empty(roots);
    }

    [Fact]
    public void Malformed_json_reads_as_no_exclusions_rather_than_throwing()
    {
        var path = Path.Combine(root, "exclusions.json");
        File.WriteAllText(path, "not json");

        var roots = ExclusionListSource.Load(path);

        Assert.Empty(roots);
    }

    [Fact]
    public void With_no_config_file_the_default_is_this_products_own_repository_root()
    {
        var nested = Path.Combine(root, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, ExclusionListSource.SolutionMarkerFileName), string.Empty);

        var path = Path.Combine(root, "does-not-exist.json");

        var roots = ExclusionListSource.Load(path, nested);

        Assert.Equal([root], roots);
    }

    [Fact]
    public void With_no_config_file_and_no_discoverable_repository_root_the_default_is_empty()
    {
        var path = Path.Combine(root, "does-not-exist.json");

        var roots = ExclusionListSource.Load(path, root);

        Assert.Empty(roots);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created, or already gone.
        }
    }
}
