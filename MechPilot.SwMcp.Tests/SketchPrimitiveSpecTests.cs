using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 unit tests for the M30 sketch primitive specs. All 8 specs share
/// the same coordinate validation (finite numbers, ±100,000 mm bounds);
/// the per-spec tests focus on the geometry-specific guards (e.g. zero-
/// length line, collinear arc points, ambiguous arc direction).
/// </summary>
public class SketchPrimitiveSpecTests
{
    // ── StartSketchSpec ──────────────────────────────────────────────────

    [Theory]
    [InlineData("front")]
    [InlineData("top")]
    [InlineData("right")]
    [InlineData("Front")]
    [InlineData("Plane1")]    // literal plane name
    [InlineData("基准面1")]   // literal CN plane name
    public void StartSketchSpec_valid_plane_validates(string plane)
    {
        new StartSketchSpec { Plane = plane }.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartSketchSpec_empty_plane_is_rejected(string plane)
    {
        var spec = new StartSketchSpec { Plane = plane };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── EndSketchSpec ────────────────────────────────────────────────────

    [Fact]
    public void EndSketchSpec_validates_with_no_fields()
    {
        new EndSketchSpec().Validate();
    }

    // ── SketchLineSpec ───────────────────────────────────────────────────

    [Fact]
    public void SketchLineSpec_canonical_validates()
    {
        new SketchLineSpec { X1 = 0, Y1 = 0, X2 = 10, Y2 = 5 }.Validate();
    }

    [Fact]
    public void SketchLineSpec_zero_length_is_rejected()
    {
        var spec = new SketchLineSpec { X1 = 1, Y1 = 2, X2 = 1, Y2 = 2 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("zero-length", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SketchLineSpec_non_finite_coord_is_rejected(double bad)
    {
        var spec = new SketchLineSpec { X1 = bad, Y1 = 0, X2 = 10, Y2 = 5 };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void SketchLineSpec_coord_outside_range_is_rejected()
    {
        var spec = new SketchLineSpec { X1 = 1_000_000, Y1 = 0, X2 = 10, Y2 = 5 };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── SketchArc3PointSpec ──────────────────────────────────────────────

    [Fact]
    public void SketchArc3PointSpec_canonical_validates()
    {
        new SketchArc3PointSpec { X1 = 10, Y1 = 0, X2 = 0, Y2 = 10, X3 = 7.07, Y3 = 7.07 }.Validate();
    }

    [Fact]
    public void SketchArc3PointSpec_collinear_is_rejected()
    {
        // (0, 0) → (10, 0) → (5, 0) are all on Y=0.
        var spec = new SketchArc3PointSpec { X1 = 0, Y1 = 0, X2 = 10, Y2 = 0, X3 = 5, Y3 = 0 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("collinear", ex.Message);
    }

    // ── SketchArcCenterSpec ──────────────────────────────────────────────

    [Fact]
    public void SketchArcCenterSpec_canonical_validates()
    {
        new SketchArcCenterSpec
        {
            Cx = 0,
            Cy = 0,
            X1 = 10,
            Y1 = 0,
            X2 = 0,
            Y2 = 10,
            Direction = 1,
        }.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    public void SketchArcCenterSpec_invalid_direction_is_rejected(int direction)
    {
        var spec = new SketchArcCenterSpec
        {
            Cx = 0,
            Cy = 0,
            X1 = 10,
            Y1 = 0,
            X2 = 0,
            Y2 = 10,
            Direction = direction,
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("direction", ex.Message);
    }

    [Fact]
    public void SketchArcCenterSpec_zero_radius_is_rejected()
    {
        var spec = new SketchArcCenterSpec
        {
            Cx = 5,
            Cy = 5,
            X1 = 5,
            Y1 = 5,
            X2 = 0,
            Y2 = 10,
            Direction = 1,
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("zero radius", ex.Message);
    }

    // ── SketchCircleSpec ─────────────────────────────────────────────────

    [Fact]
    public void SketchCircleSpec_canonical_validates()
    {
        new SketchCircleSpec { Cx = 0, Cy = 0, RadiusMm = 10 }.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SketchCircleSpec_non_positive_radius_is_rejected(double r)
    {
        var spec = new SketchCircleSpec { Cx = 0, Cy = 0, RadiusMm = r };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── SketchCenterLineSpec ─────────────────────────────────────────────

    [Fact]
    public void SketchCenterLineSpec_canonical_validates()
    {
        new SketchCenterLineSpec { X1 = 0, Y1 = -10, X2 = 0, Y2 = 10 }.Validate();
    }

    [Fact]
    public void SketchCenterLineSpec_zero_length_is_rejected()
    {
        var spec = new SketchCenterLineSpec { X1 = 0, Y1 = 0, X2 = 0, Y2 = 0 };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── SketchRectangleCenterSpec ────────────────────────────────────────

    [Fact]
    public void SketchRectangleCenterSpec_canonical_validates()
    {
        new SketchRectangleCenterSpec { Cx = 0, Cy = 0, CornerX = 25, CornerY = 15 }.Validate();
    }

    [Fact]
    public void SketchRectangleCenterSpec_zero_width_is_rejected()
    {
        // CornerX == Cx → width = 0.
        var spec = new SketchRectangleCenterSpec { Cx = 5, Cy = 0, CornerX = 5, CornerY = 10 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("zero", ex.Message);
    }
}
