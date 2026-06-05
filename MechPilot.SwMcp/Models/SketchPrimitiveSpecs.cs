using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// 6 sketch-primitive specs for M30 generic sketch tools. All coordinates
/// are in sketch-plane mm (2D — the z direction is implied by the sketch
/// plane). Tools convert mm → m at the SW Interop boundary. Coordinate
/// sanity bounds match the existing tools: ±100,000 mm (100 m square
/// covers any reasonable engineering use; anything beyond is almost
/// certainly an LLM unit-confusion bug).
///
/// Each primitive presumes an active sketch (started with start_sketch).
/// The tools check ISketchManager.ActiveSketch and reject cleanly when
/// no sketch is active.
/// </summary>
internal static class SketchPrimitiveBounds
{
    public const double MinCoordMm = -100_000.0;
    public const double MaxCoordMm = 100_000.0;
    public const double MinRadiusMm = 0.01;
    public const double MaxRadiusMm = 100_000.0;

    public static void ValidateCoord(double mm, string field)
    {
        if (double.IsNaN(mm) || double.IsInfinity(mm))
        {
            throw new McpToolException($"{field} must be a finite number (got {mm}).");
        }
        if (mm < MinCoordMm || mm > MaxCoordMm)
        {
            throw new McpToolException(
                $"{field} {mm} mm is outside the supported range " +
                $"[{MinCoordMm}, {MaxCoordMm}] mm.");
        }
    }

    public static void ValidateRadius(double mm)
    {
        if (double.IsNaN(mm) || double.IsInfinity(mm) || mm <= 0)
        {
            throw new McpToolException(
                $"radius must be > 0 mm (got {mm}).");
        }
        if (mm < MinRadiusMm || mm > MaxRadiusMm)
        {
            throw new McpToolException(
                $"radius {mm} mm is outside the supported range " +
                $"[{MinRadiusMm}, {MaxRadiusMm}] mm.");
        }
    }
}

/// <summary>Sketch a straight line segment between two points.</summary>
public sealed record SketchLineSpec
{
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }

    public void Validate()
    {
        SketchPrimitiveBounds.ValidateCoord(X1, "x1");
        SketchPrimitiveBounds.ValidateCoord(Y1, "y1");
        SketchPrimitiveBounds.ValidateCoord(X2, "x2");
        SketchPrimitiveBounds.ValidateCoord(Y2, "y2");
        if (Math.Abs(X2 - X1) < 1e-9 && Math.Abs(Y2 - Y1) < 1e-9)
        {
            throw new McpToolException(
                "line endpoints are identical (zero-length line). Pass two distinct points.");
        }
    }
}

/// <summary>
/// Sketch an arc through three points: start, end, and one intermediate
/// point on the curve. Prefer this over arc_center when both endpoints
/// might lie on the same axis (which makes arc_center's CCW/CW direction
/// ambiguous — the middle point uniquely defines a 180° arc).
/// </summary>
public sealed record SketchArc3PointSpec
{
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    public required double X3 { get; init; }
    public required double Y3 { get; init; }

    public void Validate()
    {
        SketchPrimitiveBounds.ValidateCoord(X1, "x1");
        SketchPrimitiveBounds.ValidateCoord(Y1, "y1");
        SketchPrimitiveBounds.ValidateCoord(X2, "x2");
        SketchPrimitiveBounds.ValidateCoord(Y2, "y2");
        SketchPrimitiveBounds.ValidateCoord(X3, "x3");
        SketchPrimitiveBounds.ValidateCoord(Y3, "y3");
        // Crude collinearity check via the signed triangle area; if all three
        // points lie on the same line, no arc is defined.
        var area2 = Math.Abs((X2 - X1) * (Y3 - Y1) - (X3 - X1) * (Y2 - Y1));
        if (area2 < 1e-9)
        {
            throw new McpToolException(
                "arc 3-point start/end/middle are collinear; cannot fit a valid arc.");
        }
    }
}

/// <summary>
/// Sketch an arc defined by its center, start point, end point, and
/// rotation direction (1 = CCW viewed from sketch normal, -1 = CW).
/// The radius is taken from the center-to-start distance; the end point
/// is snapped to the same radius.
/// </summary>
public sealed record SketchArcCenterSpec
{
    public required double Cx { get; init; }
    public required double Cy { get; init; }
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    /// <summary>1 = counter-clockwise, -1 = clockwise (viewed from sketch normal).</summary>
    public required int Direction { get; init; }

    public void Validate()
    {
        SketchPrimitiveBounds.ValidateCoord(Cx, "cx");
        SketchPrimitiveBounds.ValidateCoord(Cy, "cy");
        SketchPrimitiveBounds.ValidateCoord(X1, "x1");
        SketchPrimitiveBounds.ValidateCoord(Y1, "y1");
        SketchPrimitiveBounds.ValidateCoord(X2, "x2");
        SketchPrimitiveBounds.ValidateCoord(Y2, "y2");
        if (Direction != 1 && Direction != -1)
        {
            throw new McpToolException(
                $"direction must be 1 (CCW) or -1 (CW), got {Direction}.");
        }
        var r1 = Math.Sqrt((X1 - Cx) * (X1 - Cx) + (Y1 - Cy) * (Y1 - Cy));
        if (r1 < 1e-9)
        {
            throw new McpToolException(
                "arc start point coincides with center (zero radius).");
        }
    }
}

/// <summary>Sketch a circle by center and radius.</summary>
public sealed record SketchCircleSpec
{
    public required double Cx { get; init; }
    public required double Cy { get; init; }
    public required double RadiusMm { get; init; }

    public void Validate()
    {
        SketchPrimitiveBounds.ValidateCoord(Cx, "cx");
        SketchPrimitiveBounds.ValidateCoord(Cy, "cy");
        SketchPrimitiveBounds.ValidateRadius(RadiusMm);
    }
}

/// <summary>
/// Sketch a centerline (construction line). Centerlines are used as
/// the axis of revolution for revolve features when embedded in the
/// same sketch as the profile.
/// </summary>
public sealed record SketchCenterLineSpec
{
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }

    public void Validate()
    {
        SketchPrimitiveBounds.ValidateCoord(X1, "x1");
        SketchPrimitiveBounds.ValidateCoord(Y1, "y1");
        SketchPrimitiveBounds.ValidateCoord(X2, "x2");
        SketchPrimitiveBounds.ValidateCoord(Y2, "y2");
        if (Math.Abs(X2 - X1) < 1e-9 && Math.Abs(Y2 - Y1) < 1e-9)
        {
            throw new McpToolException(
                "centerline endpoints are identical (zero-length).");
        }
    }
}

/// <summary>
/// Sketch a centered rectangle given its center and one corner. The
/// rectangle's sides are axis-aligned (parallel to sketch X / Y axes);
/// width = 2 * |cornerX - centerX|, height = 2 * |cornerY - centerY|.
/// </summary>
public sealed record SketchRectangleCenterSpec
{
    public required double Cx { get; init; }
    public required double Cy { get; init; }
    /// <summary>X coordinate of one corner (opposite corner is (2*Cx - CornerX, 2*Cy - CornerY)).</summary>
    public required double CornerX { get; init; }
    public required double CornerY { get; init; }

    public void Validate()
    {
        SketchPrimitiveBounds.ValidateCoord(Cx, "cx");
        SketchPrimitiveBounds.ValidateCoord(Cy, "cy");
        SketchPrimitiveBounds.ValidateCoord(CornerX, "cornerX");
        SketchPrimitiveBounds.ValidateCoord(CornerY, "cornerY");
        if (Math.Abs(CornerX - Cx) < 1e-9 || Math.Abs(CornerY - Cy) < 1e-9)
        {
            throw new McpToolException(
                "rectangle corner is on the same axis as center (zero width or height).");
        }
    }
}
