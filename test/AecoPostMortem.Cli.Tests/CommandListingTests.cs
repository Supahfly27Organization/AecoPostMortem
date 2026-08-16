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
