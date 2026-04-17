namespace CSharpCoverage.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        return CommandRouter.Run(args);
    }
}
