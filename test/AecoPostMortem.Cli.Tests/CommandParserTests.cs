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
