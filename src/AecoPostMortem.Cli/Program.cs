namespace AecoPostMortem.Cli;

public static class Program
{
    public static int Main(string[] args) => CommandRunner.Run(args, Console.Out, Console.Error);
}
