using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 unit tests for the M31 feature specs (ExtrudeSpec / RevolveSpec).
/// </summary>
public class FeatureSpecTests
{
    // ── ExtrudeSpec ──────────────────────────────────────────────────────

    [Fact]
    public void ExtrudeSpec_canonical_validates()
    {
        new ExtrudeSpec { SketchName = "草图1", DepthMm = 30 }.Validate();
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(30)]
    [InlineData(10000)]
    public void ExtrudeSpec_valid_depths_validate(double depth)
    {
        new ExtrudeSpec { SketchName = "Sketch1", DepthMm = depth }.Validate();
    }

    [Fact]
    public void ExtrudeSpec_reverse_flag_validates()
    {
        new ExtrudeSpec { SketchName = "草图1", DepthMm = 30, Reverse = true }.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtrudeSpec_empty_sketch_name_is_rejected(string name)
    {
        var spec = new ExtrudeSpec { SketchName = name, DepthMm = 30 };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void ExtrudeSpec_non_positive_depth_is_rejected(double depth)
    {
        var spec = new ExtrudeSpec { SketchName = "草图1", DepthMm = depth };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("depth", ex.Message);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(10001)]
    public void ExtrudeSpec_depth_outside_range_is_rejected(double depth)
    {
        var spec = new ExtrudeSpec { SketchName = "草图1", DepthMm = depth };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ExtrudeSpec_non_finite_depth_is_rejected(double depth)
    {
        var spec = new ExtrudeSpec { SketchName = "草图1", DepthMm = depth };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── RevolveSpec ──────────────────────────────────────────────────────

    [Fact]
    public void RevolveSpec_canonical_validates()
    {
        new RevolveSpec { SketchName = "草图1", AngleDeg = 360 }.Validate();
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(360)]
    public void RevolveSpec_valid_angles_validate(double angle)
    {
        new RevolveSpec { SketchName = "草图1", AngleDeg = angle }.Validate();
    }

    [Fact]
    public void RevolveSpec_reverse_flag_validates()
    {
        new RevolveSpec { SketchName = "草图1", AngleDeg = 360, Reverse = true }.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RevolveSpec_empty_sketch_name_is_rejected(string name)
    {
        var spec = new RevolveSpec { SketchName = name, AngleDeg = 360 };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void RevolveSpec_non_positive_angle_is_rejected(double angle)
    {
        var spec = new RevolveSpec { SketchName = "草图1", AngleDeg = angle };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("angle", ex.Message);
    }

    [Theory]
    [InlineData(0.005)]
    [InlineData(361)]
    [InlineData(720)]
    public void RevolveSpec_angle_outside_range_is_rejected(double angle)
    {
        var spec = new RevolveSpec { SketchName = "草图1", AngleDeg = angle };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }
}
