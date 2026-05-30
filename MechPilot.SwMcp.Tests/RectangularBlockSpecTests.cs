using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// RectangularBlockSpec validates 3 positive dimensions + savePath, mirroring
/// CylinderSpec's structure. Three independent dims means we get a Theory per
/// field for symmetric coverage.
/// </summary>
public class RectangularBlockSpecTests
{
    private static readonly string TempDir = Path.GetTempPath();

    private static RectangularBlockSpec Canonical() => new()
    {
        LengthMm = 100,
        WidthMm = 50,
        HeightMm = 20,
        SavePath = Path.Combine(TempDir, "block.sldprt"),
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData(0.5, 0.5, 0.5)]    // small cube
    [InlineData(100, 50, 20)]      // canonical bracket
    [InlineData(1000, 1000, 1000)] // 1 m cube (boundary)
    [InlineData(1, 9999, 1)]       // thin long bar
    public void Various_dimensions_validate(double l, double w, double h)
    {
        var spec = Canonical() with { LengthMm = l, WidthMm = w, HeightMm = h };
        spec.Validate();
    }

    // ── dimension validation: each axis must be > 0 and within bounds ──────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_length_throws(double bad)
    {
        var spec = Canonical() with { LengthMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("length", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_width_throws(double bad)
    {
        var spec = Canonical() with { WidthMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("width", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_height_throws(double bad)
    {
        var spec = Canonical() with { HeightMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("height", ex.Message);
    }

    [Fact]
    public void Length_above_max_throws()
    {
        var spec = Canonical() with { LengthMm = 20_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    [Fact]
    public void Width_below_min_throws()
    {
        var spec = Canonical() with { WidthMm = 0.001 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    [Fact]
    public void Height_above_max_throws()
    {
        var spec = Canonical() with { HeightMm = 50_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    // ── save-path validation ──────────────────────────────────────────────

    [Fact]
    public void Empty_savePath_throws()
    {
        var spec = Canonical() with { SavePath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("savePath", ex.Message);
    }

    [Fact]
    public void Relative_savePath_throws()
    {
        var spec = Canonical() with { SavePath = "block.sldprt" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_savePath_extension_throws()
    {
        var spec = Canonical() with { SavePath = Path.Combine(TempDir, "block.step") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Nonexistent_savePath_parent_throws()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, "no-dir-" + Guid.NewGuid(), "block.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("directory", ex.Message);
    }
}
