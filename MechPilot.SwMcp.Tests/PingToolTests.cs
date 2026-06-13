using MechPilot.SwMcp.Tools;

namespace MechPilot.SwMcp.Tests;

public class PingToolTests
{
    [Fact]
    public void Run_returns_ok_with_pong_and_build_info()
    {
        var result = PingTool.Run();

        Assert.Equal("ok", result.Status);
        Assert.NotNull(result.Message);
        Assert.StartsWith("pong", result.Message);
        // M57: ping now reports the build (git + build time).
        Assert.Contains("git ", result.Message);
        Assert.Contains("built ", result.Message);
        Assert.Null(result.Path);
    }

    [Fact]
    public void Run_exposes_build_data()
    {
        var result = PingTool.Run();

        Assert.NotNull(result.Data);
        Assert.True(result.Data!.ContainsKey("gitSha"));
        Assert.True(result.Data.ContainsKey("gitDirty"));
        Assert.True(result.Data.ContainsKey("buildTimeUtc"));
        Assert.IsType<bool>(result.Data["gitDirty"]);
    }
}
