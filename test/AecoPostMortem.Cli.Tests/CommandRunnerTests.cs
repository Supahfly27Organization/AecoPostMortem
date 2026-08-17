using System.Globalization;
using AecoPostMortem.Data;

namespace AecoPostMortem.Cli.Tests;

public sealed class CommandRunnerTests
{
    static (int ExitCode, string Stdout, string Stderr) Run(params string[] arguments) =>
        Run(store: null, arguments);

    static (int ExitCode, string Stdout, string Stderr) Run(LocalStore? store, params string[] arguments)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CommandRunner.Run(arguments, stdout, stderr, store);
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
    public void The_other_commands_report_the_same_way(string name)
    {
        var (exitCode, stdout, _) = Run(name);

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("not implemented yet", stdout);
    }

    [Fact]
    public void Purge_deletes_the_store_and_says_what_it_deleted()
    {
        using var temporary = new TemporaryStore();
        using (temporary.Store.Open())
        {
        }

        var (exitCode, stdout, stderr) = Run(temporary.Store, "purge");

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains(temporary.Store.FilePath, stdout, StringComparison.Ordinal);
        Assert.Contains("Purged", stdout, StringComparison.Ordinal);
        Assert.False(temporary.Store.Exists);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void Purging_when_there_is_nothing_to_purge_reports_that_and_exits_zero()
    {
        using var temporary = new TemporaryStore();

        var (exitCode, stdout, stderr) = Run(temporary.Store, "purge");

        Assert.Equal(CommandRunner.Success, exitCode);
        Assert.Contains("Nothing to purge", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr);
    }

    /// <summary>A store in a throwaway directory: the CLI's default is the operator's real store,
    /// and a test that purged it would be the one thing FR-11 exists to prevent.</summary>
    sealed class TemporaryStore : IDisposable
    {
        readonly string folder;

        public TemporaryStore()
        {
            folder = Path.Combine(
                Path.GetTempPath(),
                "AecoPostMortem.Tests",
                Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

            Store = new LocalStore(Path.Combine(folder, StoreLocation.FileName));
        }

        public LocalStore Store { get; }

        public void Dispose()
        {
            Store.Purge();

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Never created, or already gone.
            }
        }
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
