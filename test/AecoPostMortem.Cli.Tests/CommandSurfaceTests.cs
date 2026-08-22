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
        Assert.Equal("[path] [--store <path>]", CommandSurface.Find("ingest")!.Arguments);
        Assert.Equal("[--port <n>] [--store <path>]", CommandSurface.Find("serve")!.Arguments);
    }

    /// <summary>`--store` is global — every command opens the store, so every command documents it.
    /// This table is the only place commands are enumerated, so a flag that works but is missing
    /// here would be undiscoverable from the listing.</summary>
    [Fact]
    public void Every_command_documents_the_global_store_option()
    {
        Assert.All(
            CommandSurface.Commands,
            command => Assert.Contains("--store <path>", command.Arguments, StringComparison.Ordinal));
    }

    [Fact]
    public void Command_lookup_ignores_case()
    {
        Assert.NotNull(CommandSurface.Find("INGEST"));
    }
}
