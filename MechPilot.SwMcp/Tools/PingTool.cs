using System.ComponentModel;
using MechPilot.SwMcp.Models;
using MechPilot.SwMcp.Tools.Internal;
using ModelContextProtocol.Server;

namespace MechPilot.SwMcp.Tools;

[McpServerToolType]
public static class PingTool
{
    [McpServerTool(Name = "ping")]
    [Description(
        "Sanity check that the MCP server is alive AND report which build is " +
        "running — the git commit (short SHA, '-dirty' if the build had " +
        "uncommitted changes) and the build time. Use this to confirm a " +
        "long-lived server is your latest exe before relying on a newly added " +
        "tool: if the SHA / build time is older than your last build, restart " +
        "the session so the server re-spawns from the current exe.")]
    public static ToolResult Run()
    {
        var (sha, dirty, buildTime) = BuildInfo.Read(typeof(PingTool).Assembly);
        var data = new Dictionary<string, object>
        {
            ["gitSha"] = sha,
            ["gitDirty"] = dirty,
            ["buildTimeUtc"] = buildTime?.ToString("o") ?? "unknown",
        };
        return ToolResult.Ok(
            message: $"pong — {BuildInfo.Describe(sha, dirty, buildTime)}",
            data: data);
    }
}
