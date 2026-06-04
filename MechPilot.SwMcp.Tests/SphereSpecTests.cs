using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// SphereSpec validates the diameter (positive, within sanity bounds) and
/// the save path. Same shape as HemisphereSpecTests since SphereSpec and
/// HemisphereSpec have identical fields — they differ only in tool docstring
/// and revolved geometry (half-disc vs. quarter-disc profile).
/// </summary>
public class SphereSpecTests
{
    private static readonly string TempDir = Path.GetTempPath();

    private static SphereSpec Canonical() => new()
    {
        DiameterMm = 40,
        SavePath = Path.Combine(TempDir, $"sphere-{Guid.NewGuid()}.sldprt"),
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(1.0)]
    [InlineData(20.0)]
    [InlineData(40.0)]     // ball-bearing / valve-core scale
    [InlineData(500.0)]
    [InlineData(10000.0)]
    public void Valid_diameters_validate(double diameterMm)
    {
        var spec = Canonical() with { DiameterMm = diameterMm };
        spec.Validate();
    }

    [Fact]
    public void Save_path_with_uppercase_extension_validates()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"sphere-{Guid.NewGuid()}.SLDPRT"),
        };
        spec.Validate();
    }

    // ── diameter rejections ───────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(-40.0)]
    public void Non_positive_diameters_are_rejected(double diameterMm)
    {
        var spec = Canonical() with { DiameterMm = diameterMm };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("diameter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(10001.0)]
    [InlineData(1_000_000.0)]
    public void Diameters_outside_range_are_rejected(double diameterMm)
    {
        var spec = Canonical() with { DiameterMm = diameterMm };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("range", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_diameters_are_rejected(double diameterMm)
    {
        var spec = Canonical() with { DiameterMm = diameterMm };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── path rejections ───────────────────────────────────────────────────

    [Fact]
    public void Empty_path_is_rejected()
    {
        var spec = Canonical() with { SavePath = string.Empty };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Whitespace_path_is_rejected()
    {
        var spec = Canonical() with { SavePath = "   " };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void Relative_path_is_rejected()
    {
        var spec = Canonical() with { SavePath = "sphere.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_extension_is_rejected()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"sphere-{Guid.NewGuid()}.step"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void No_extension_is_rejected()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"sphere-{Guid.NewGuid()}"),
        };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void Missing_parent_directory_is_rejected()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"no-such-dir-{Guid.NewGuid()}", "sphere.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("parent directory does not exist", ex.Message);
    }
}
