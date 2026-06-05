using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for an extrude feature in the M31 generic primitives layer.
/// Takes a sketch name (from end_sketch) and a depth, builds a boss-extrude
/// feature on the active part via <c>FeatureExtrusion3</c>.
///
/// LLM workflow:
///   start_sketch → sketch_* → end_sketch (returns "草图1") → extrude("草图1", 30)
///
/// Bounded to "blind" + single-direction for the MVP — covers ~95% of LLM
/// extrude use cases. Through-all / both-direction / mid-plane variants can
/// be added in a future PR (FeatureExtrusion3 already supports them).
/// </summary>
public sealed record ExtrudeSpec
{
    /// <summary>
    /// Name of the sketch to extrude (typically returned from end_sketch).
    /// e.g. "草图1" / "Sketch1".
    /// </summary>
    public required string SketchName { get; init; }

    /// <summary>Extrusion depth in mm along the sketch plane's normal. Must be &gt; 0.</summary>
    public required double DepthMm { get; init; }

    /// <summary>
    /// If true, the extrude direction flips against the default sketch-plane
    /// normal. Default false. Useful when SW's default direction extrudes into
    /// the wrong side (e.g. for a Front-Plane sketch that should extrude in -Z).
    /// </summary>
    public bool Reverse { get; init; } = false;

    // Sanity bounds matching CylinderSpec.LengthMm.
    private const double MinDepthMm = 0.1;
    private const double MaxDepthMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SketchName))
        {
            throw new McpToolException(
                "sketchName must not be empty. Pass the name returned from end_sketch " +
                "(typically '草图1' / 'Sketch1' / etc.).");
        }
        if (double.IsNaN(DepthMm) || double.IsInfinity(DepthMm) || DepthMm <= 0)
        {
            throw new McpToolException(
                $"depth must be > 0 mm (got {DepthMm}). " +
                "Hint: pass millimeters, e.g. 30 for a 30 mm extrusion.");
        }
        if (DepthMm < MinDepthMm || DepthMm > MaxDepthMm)
        {
            throw new McpToolException(
                $"depth {DepthMm} mm is outside the supported range " +
                $"[{MinDepthMm}, {MaxDepthMm}] mm.");
        }
    }
}

/// <summary>
/// Specification for a revolve feature in the M31 generic primitives layer.
/// Takes a sketch name (containing a profile + an embedded centerline) and
/// an angle in degrees, builds a boss-revolve feature on the active part
/// via <c>FeatureRevolve2</c>.
///
/// LLM workflow:
///   start_sketch → sketch_* (profile + sketch_centerline as axis) →
///   end_sketch (returns "草图1") → revolve("草图1", 360)
///
/// The revolve axis is the centerline embedded in the sketch (SW
/// auto-binds it when the sketch is selected with mark=0); no separate
/// axis argument is needed. For full revolution (most common), pass 360.
/// </summary>
public sealed record RevolveSpec
{
    /// <summary>Name of the sketch to revolve (must contain a profile + a centerline).</summary>
    public required string SketchName { get; init; }

    /// <summary>
    /// Revolve sweep angle in degrees. Must be in (0, 360]. Default 360
    /// (full revolution) is the most common case.
    /// </summary>
    public required double AngleDeg { get; init; }

    /// <summary>
    /// If true, revolve in the opposite direction. Default false.
    /// </summary>
    public bool Reverse { get; init; } = false;

    private const double MinAngleDeg = 0.01;
    private const double MaxAngleDeg = 360.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SketchName))
        {
            throw new McpToolException(
                "sketchName must not be empty. Pass the name returned from end_sketch.");
        }
        if (double.IsNaN(AngleDeg) || double.IsInfinity(AngleDeg) || AngleDeg <= 0)
        {
            throw new McpToolException(
                $"angle must be > 0 degrees (got {AngleDeg}). " +
                "Hint: pass degrees, e.g. 360 for a full revolution.");
        }
        if (AngleDeg < MinAngleDeg || AngleDeg > MaxAngleDeg)
        {
            throw new McpToolException(
                $"angle {AngleDeg}° is outside the supported range " +
                $"[{MinAngleDeg}, {MaxAngleDeg}] degrees.");
        }
    }
}
