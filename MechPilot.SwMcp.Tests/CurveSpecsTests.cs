using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>L1 tests for the M50 curve specs (SketchSplineSpec / InsertHelixSpec).</summary>
public sealed class CurveSpecsTests
{
    // ── SketchSplineSpec ────────────────────────────────────────────────────

    [Fact]
    public void Spline_three_points_passes()
        => new SketchSplineSpec { Points = new double[] { 0, 0, 15, 8, 30, 0 } }.Validate();

    [Fact]
    public void Spline_many_points_passes()
        => new SketchSplineSpec
        {
            Points = new double[] { 0, 0, 10, 5, 20, -5, 30, 5, 40, 0 },
        }.Validate();

    [Fact]
    public void Spline_empty_points_throws()
    {
        var spec = new SketchSplineSpec { Points = Array.Empty<double>() };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("points", ex.Message);
    }

    [Fact]
    public void Spline_odd_count_throws()
    {
        var spec = new SketchSplineSpec { Points = new double[] { 0, 0, 15, 8, 30 } };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("odd", ex.Message);
    }

    [Fact]
    public void Spline_two_points_throws_with_line_hint()
    {
        var spec = new SketchSplineSpec { Points = new double[] { 0, 0, 30, 0 } };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("sketch_line", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Spline_non_finite_coordinate_throws(double bad)
    {
        var spec = new SketchSplineSpec { Points = new double[] { 0, 0, bad, 8, 30, 0 } };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("finite", ex.Message);
    }

    [Fact]
    public void Spline_coordinate_beyond_bound_throws()
    {
        var spec = new SketchSplineSpec { Points = new double[] { 0, 0, 10_001, 8, 30, 0 } };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("sanity", ex.Message);
    }

    [Fact]
    public void Spline_consecutive_duplicate_points_throw()
    {
        var spec = new SketchSplineSpec { Points = new double[] { 0, 0, 15, 8, 15, 8, 30, 0 } };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("identical", ex.Message);
    }

    [Fact]
    public void Spline_non_consecutive_repeat_passes()
        // A closed-ish wave may revisit a coordinate later — only CONSECUTIVE
        // duplicates are degenerate.
        => new SketchSplineSpec { Points = new double[] { 0, 0, 15, 8, 30, 0, 15, -8 } }.Validate();

    // ── InsertHelixSpec ─────────────────────────────────────────────────────

    [Fact]
    public void Helix_defaults_pass_and_are_clockwise()
    {
        var spec = new InsertHelixSpec { PitchMm = 8, Revolutions = 5 };
        Assert.True(spec.Clockwise);
        Assert.False(spec.Reverse);
        spec.Validate();
    }

    [Fact]
    public void Helix_fractional_revolutions_pass()
        => new InsertHelixSpec { PitchMm = 2.5, Revolutions = 0.75 }.Validate();

    [Theory]
    [InlineData(0)]
    [InlineData(-8)]
    [InlineData(double.NaN)]
    public void Helix_non_positive_pitch_throws(double bad)
    {
        var spec = new InsertHelixSpec { PitchMm = bad, Revolutions = 5 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("pitch", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Helix_non_positive_revolutions_throws(double bad)
    {
        var spec = new InsertHelixSpec { PitchMm = 8, Revolutions = bad };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("revolutions", ex.Message);
    }

    [Fact]
    public void Helix_oversize_pitch_throws()
    {
        var spec = new InsertHelixSpec { PitchMm = 10_001, Revolutions = 5 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("sanity", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    [InlineData(720)]
    public void Helix_start_angle_out_of_range_throws(double bad)
    {
        var spec = new InsertHelixSpec { PitchMm = 8, Revolutions = 5, StartAngleDeg = bad };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("startAngle", ex.Message);
    }
}
