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

    /// <summary>`--store <path>` is global: it applies to every command, so it is taken here rather
    /// than by each command's own argument handling.</summary>
    [Fact]
    public void A_store_option_is_read_off_any_command()
    {
        var invocation = CommandParser.Parse(["purge", "--store", @"D:\elsewhere\store.db"]);

        Assert.Equal(@"D:\elsewhere\store.db", invocation.StorePath);
        Assert.Null(invocation.OptionError);
    }

    /// <summary>The real hazard in adding a global option to a command that already takes a
    /// positional argument: `ingest [path]` reads `Arguments[0]`, so the flag and its value must be
    /// out of that list, with everything else left in its original order.</summary>
    [Fact]
    public void The_store_option_and_its_value_are_taken_out_of_the_remaining_arguments()
    {
        var invocation = CommandParser.Parse(
            ["ingest", "--store", @"D:\elsewhere\store.db", @"C:\sessions"]);

        Assert.Equal(new[] { @"C:\sessions" }, invocation.Arguments);
    }

    [Fact]
    public void A_store_option_after_a_positional_argument_is_still_taken_and_leaves_order_intact()
    {
        var invocation = CommandParser.Parse(
            ["serve", "--port", "5000", "--store", @"D:\elsewhere\store.db"]);

        Assert.Equal(@"D:\elsewhere\store.db", invocation.StorePath);
        Assert.Equal(new[] { "--port", "5000" }, invocation.Arguments);
    }

    [Fact]
    public void A_store_option_with_no_value_is_reported_as_an_error_rather_than_ignored()
    {
        var invocation = CommandParser.Parse(["purge", "--store"]);

        Assert.NotNull(invocation.OptionError);
        Assert.Contains("--store", invocation.OptionError!, StringComparison.Ordinal);
        Assert.Null(invocation.StorePath);
    }

    [Fact]
    public void No_store_option_leaves_the_path_unset_rather_than_defaulting_here()
    {
        // The default location is resolved by the caller (CommandRunner), not by this parser — it
        // stays pure: no environment, no file system.
        var invocation = CommandParser.Parse(["purge"]);

        Assert.Null(invocation.StorePath);
        Assert.Null(invocation.OptionError);
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
