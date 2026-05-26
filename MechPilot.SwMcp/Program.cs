using MechPilot.SwMcp.Entrypoints;

namespace MechPilot.SwMcp;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            return await McpServer.RunAsync();
        }

        return await CliRunner.RunAsync(args);
    }
}
