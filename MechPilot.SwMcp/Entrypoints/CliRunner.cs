using System.CommandLine;
using System.Text.Json;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using MechPilot.SwMcp.Tools;

namespace MechPilot.SwMcp.Entrypoints;

public static class CliRunner
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("mech-pilot-sw: SolidWorks MCP server + CLI");

        root.Subcommands.Add(BuildPingCommand());

        var parseResult = root.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static Command BuildPingCommand()
    {
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("ping", "Sanity check; returns 'pong'.")
        {
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var result = PingTool.Run();
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static void WriteResult(ToolResult result, string format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
        }
        else
        {
            Console.WriteLine(result.Message ?? result.Status);
        }
    }
}
