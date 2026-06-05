using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 unit tests for the M32 advanced feature specs (AddRefPlaneSpec /
/// LoftSpec / SweepSpec).
/// </summary>
public class AdvancedFeatureSpecTests
{
    // ── AddRefPlaneSpec ──────────────────────────────────────────────────

    [Fact]
    public void AddRefPlaneSpec_canonical_validates()
    {
        new AddRefPlaneSpec { SourcePlane = "front", DistanceMm = 30 }.Validate();
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(30)]
    [InlineData(-30)]      // negative offset
    [InlineData(10000)]
    public void AddRefPlaneSpec_valid_distances_validate(double d)
    {
        new AddRefPlaneSpec { SourcePlane = "top", DistanceMm = d }.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddRefPlaneSpec_empty_source_is_rejected(string s)
    {
        Assert.Throws<McpToolException>(() =>
            new AddRefPlaneSpec { SourcePlane = s, DistanceMm = 30 }.Validate());
    }

    [Fact]
    public void AddRefPlaneSpec_zero_distance_is_rejected()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new AddRefPlaneSpec { SourcePlane = "front", DistanceMm = 0 }.Validate());
        Assert.Contains("minimum", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AddRefPlaneSpec_non_finite_distance_is_rejected(double d)
    {
        Assert.Throws<McpToolException>(() =>
            new AddRefPlaneSpec { SourcePlane = "front", DistanceMm = d }.Validate());
    }

    [Theory]
    [InlineData(0.005)]   // below 0.01 mm min
    [InlineData(10001)]   // above 10 m max
    public void AddRefPlaneSpec_distance_outside_range_is_rejected(double d)
    {
        Assert.Throws<McpToolException>(() =>
            new AddRefPlaneSpec { SourcePlane = "front", DistanceMm = d }.Validate());
    }

    // ── LoftSpec ─────────────────────────────────────────────────────────

    [Fact]
    public void LoftSpec_two_sketches_validates()
    {
        new LoftSpec { SketchNames = new[] { "草图1", "草图2" } }.Validate();
    }

    [Fact]
    public void LoftSpec_three_sketches_validates()
    {
        new LoftSpec { SketchNames = new[] { "草图1", "草图2", "草图3" } }.Validate();
    }

    [Fact]
    public void LoftSpec_closed_flag_validates()
    {
        new LoftSpec
        {
            SketchNames = new[] { "草图1", "草图2", "草图3" },
            Closed = true,
        }.Validate();
    }

    [Fact]
    public void LoftSpec_single_sketch_is_rejected()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new LoftSpec { SketchNames = new[] { "草图1" } }.Validate());
        Assert.Contains("at least 2", ex.Message);
    }

    [Fact]
    public void LoftSpec_empty_list_is_rejected()
    {
        Assert.Throws<McpToolException>(() =>
            new LoftSpec { SketchNames = Array.Empty<string>() }.Validate());
    }

    [Fact]
    public void LoftSpec_empty_name_in_list_is_rejected()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new LoftSpec { SketchNames = new[] { "草图1", "", "草图3" } }.Validate());
        Assert.Contains("[1]", ex.Message);
    }

    // ── SweepSpec ────────────────────────────────────────────────────────

    [Fact]
    public void SweepSpec_canonical_validates()
    {
        new SweepSpec { ProfileSketchName = "草图1", PathSketchName = "草图2" }.Validate();
    }

    [Theory]
    [InlineData("", "草图2")]
    [InlineData("草图1", "")]
    [InlineData("   ", "草图2")]
    public void SweepSpec_empty_name_is_rejected(string profile, string path)
    {
        Assert.Throws<McpToolException>(() =>
            new SweepSpec { ProfileSketchName = profile, PathSketchName = path }.Validate());
    }

    [Fact]
    public void SweepSpec_same_profile_and_path_is_rejected()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new SweepSpec { ProfileSketchName = "草图1", PathSketchName = "草图1" }.Validate());
        Assert.Contains("must differ", ex.Message);
    }

    [Fact]
    public void SweepSpec_case_insensitive_same_name_is_rejected()
    {
        Assert.Throws<McpToolException>(() =>
            new SweepSpec { ProfileSketchName = "Sketch1", PathSketchName = "sketch1" }.Validate());
    }
}
