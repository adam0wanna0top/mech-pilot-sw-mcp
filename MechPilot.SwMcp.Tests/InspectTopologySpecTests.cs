using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>L1 tests for <see cref="InspectTopologySpec"/> (M51).</summary>
public sealed class InspectTopologySpecTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _partPath;

    public InspectTopologySpecTests()
    {
        _tmpDir = Path.Combine(
            Path.GetTempPath(), $"mech-pilot-topo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _partPath = Path.Combine(_tmpDir, "part.sldprt");
        File.WriteAllText(_partPath, "stub");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Active_mode_no_part_path_passes()
        => new InspectTopologySpec().Validate();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_part_path_means_active_mode_and_passes(string? blank)
        => new InspectTopologySpec { PartPath = blank }.Validate();

    [Fact]
    public void File_mode_with_existing_part_passes()
        => new InspectTopologySpec { PartPath = _partPath }.Validate();

    [Fact]
    public void Relative_part_path_throws()
    {
        var spec = new InspectTopologySpec { PartPath = "rel/part.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_extension_throws()
    {
        var spec = new InspectTopologySpec
        {
            PartPath = Path.Combine(_tmpDir, "asm.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Missing_file_throws()
    {
        var spec = new InspectTopologySpec
        {
            PartPath = Path.Combine(_tmpDir, "missing.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("does not exist", ex.Message);
    }
}
