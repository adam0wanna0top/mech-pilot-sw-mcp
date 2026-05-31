using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// CounterboreSpec validates 7 GB sizes (M3-M12) and exposes GB/T 152.3 table.
/// Mirrors ThreadedHoleSpec's structure.
/// </summary>
public class CounterboreSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public CounterboreSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"cb-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private CounterboreSpec Canonical(string thread = "M6") => new()
    {
        InputPath = _existingPart,
        ThreadSize = thread,
    };

    [Theory]
    [InlineData("M3")]
    [InlineData("M4")]
    [InlineData("M5")]
    [InlineData("M6")]
    [InlineData("M8")]
    [InlineData("M10")]
    [InlineData("M12")]
    [InlineData("m6")]
    public void Supported_thread_sizes_validate(string thread)
    {
        Canonical(thread).Validate();
    }

    [Theory]
    [InlineData("M3", 3.4, 6.5, 3.4)]
    [InlineData("M4", 4.5, 8.0, 4.6)]
    [InlineData("M5", 5.5, 10.0, 5.7)]
    [InlineData("M6", 6.6, 11.0, 6.8)]
    [InlineData("M8", 9.0, 15.0, 9.0)]
    [InlineData("M10", 11.0, 18.0, 11.0)]
    [InlineData("M12", 13.5, 20.0, 13.0)]
    public void GbTable_matches_GBT_152_3(string thread, double cl, double cb, double dep)
    {
        Assert.True(CounterboreSpec.GbTable.TryGetValue(thread, out var entry));
        Assert.Equal(cl, entry.ClearanceMm);
        Assert.Equal(cb, entry.CbDiameterMm);
        Assert.Equal(dep, entry.CbDepthMm);
    }

    [Fact]
    public void GbTable_has_exactly_7_entries()
    {
        Assert.Equal(7, CounterboreSpec.GbTable.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("M2")]
    [InlineData("M14")]
    [InlineData("1/4")]
    public void Unsupported_thread_throws(string bad)
    {
        var spec = Canonical(bad);
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.True(
            ex.Message.Contains("not in the GB") || ex.Message.Contains("threadSize"),
            $"unexpected message: {ex.Message}");
    }

    [Fact]
    public void Blind_depth_validates()
    {
        var spec = Canonical() with { DepthMm = 12 };
        spec.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_depth_throws(double bad)
    {
        var spec = Canonical() with { DepthMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("depth", ex.Message);
    }

    [Fact]
    public void Nonexistent_input_throws()
    {
        var spec = Canonical() with
        {
            InputPath = Path.Combine(TempDir, $"no-such-part-{Guid.NewGuid()}.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Relative_output_throws()
    {
        var spec = Canonical() with { OutputPath = "out.sldprt" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }
}
