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
