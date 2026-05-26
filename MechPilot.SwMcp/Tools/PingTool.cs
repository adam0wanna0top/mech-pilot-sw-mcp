using System.ComponentModel;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;

namespace MechPilot.SwMcp.Tools;

[McpServerToolType]
public static class PingTool
{
    [McpServerTool(Name = "ping")]
    [Description("Sanity check that the MCP server is alive. Returns 'pong'.")]
    public static ToolResult Run() =>
        ToolResult.Ok(message: "pong");
}
