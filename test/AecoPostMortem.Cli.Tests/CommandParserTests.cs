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

    [Fact]
    public void A_help_word_after_a_command_is_pinned_as_a_plain_argument_not_a_help_request()
    {
        // This pins current behaviour, not desired behaviour: help words are recognised in the
        // first position only, so `ingest --help` is parsed as ingest with "--help" as a plain
        // argument rather than as a request for help. Real argument handling arrives with the
        // next epic; when that happens, changing this is a deliberate decision made against this
        // test, not an accidental side effect.
        var invocation = CommandParser.Parse(["ingest", "--help"]);

        Assert.False(invocation.ShowsListing);
        Assert.Equal("ingest", invocation.Command?.Name);
        Assert.Equal(new[] { "--help" }, invocation.Arguments);
    }
}
