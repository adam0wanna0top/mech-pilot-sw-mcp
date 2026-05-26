using MechPilot.SwMcp.Tools;

namespace MechPilot.SwMcp.Tests;

public class PingToolTests
{
    [Fact]
    public void Run_returns_ok_with_pong_message()
    {
        var result = PingTool.Run();

        Assert.Equal("ok", result.Status);
        Assert.Equal("pong", result.Message);
        Assert.Null(result.Path);
    }
}
