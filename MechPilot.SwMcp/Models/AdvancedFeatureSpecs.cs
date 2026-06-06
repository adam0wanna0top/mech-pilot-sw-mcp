using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for creating an offset reference plane from an existing
/// plane (M32 generic primitives layer). Wraps SW's <c>InsertRefPlane</c>
/// with the Distance constraint (=8, bitflag enum reflected in M28). The
/// new plane is auto-named "基准面N" / "PlaneN" by SW; LLM passes that
/// name to subsequent <c>start_sketch</c> calls.
///
/// LLM workflow:
///   new_part → start_sketch("front") → sketch_* → end_sketch
///   add_ref_plane(sourcePlane="front", distance=30)   ← returns "基准面1"
///   start_sketch("基准面1") → sketch_* → end_sketch
///   loft(["草图1", "草图2"])
/// </summary>
public sealed record AddRefPlaneSpec
{
    /// <summary>
    /// Source plane name. Can be "front" / "top" / "right" (CN/EN auto-alias)
    /// or a literal SW plane name (e.g. "基准面1" for an already-created RefPlane).
    /// </summary>
    public required string SourcePlane { get; init; }

    /// <summary>Offset distance from the source plane in mm. Must be != 0.</summary>
    public required double DistanceMm { get; init; }

    /// <summary>If true, the offset is in the reverse direction of the source plane's normal. Default false.</summary>
    public bool Reverse { get; init; } = false;

    private const double MinAbsDistanceMm = 0.01;
    private const double MaxAbsDistanceMm = 10_000.0;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourcePlane))
        {
            throw new McpToolException(
                "sourcePlane must not be empty. Use 'front' / 'top' / 'right' for " +
                "SW's default reference planes, or a literal plane name like 'Plane1'.");
        }
        if (double.IsNaN(DistanceMm) || double.IsInfinity(DistanceMm))
        {
            throw new McpToolException($"distance must be a finite number (got {DistanceMm}).");
        }
        var absD = Math.Abs(DistanceMm);
        if (absD < MinAbsDistanceMm)
        {
            throw new McpToolException(
                $"distance |{DistanceMm}| mm is below the {MinAbsDistanceMm} mm minimum. " +
                "Use a non-trivial offset (zero offset means coincident — use the source plane directly).");
        }
        if (absD > MaxAbsDistanceMm)
        {
            throw new McpToolException(
                $"distance |{DistanceMm}| mm exceeds the {MaxAbsDistanceMm} mm maximum.");
        }
    }
}

/// <summary>
/// Specification for a loft (blend) feature over 2+ named sketches in the
/// active part. M32 generic-layer equivalent of M28's
/// <c>create_lofted_round_to_square</c> — but accepts any 2+ sketches the
/// LLM has built, not just the round-to-square hardcoded case.
///
/// LLM workflow (3-profile loft example):
///   new_part → 3 × (start_sketch on different planes + sketch_* + end_sketch)
///   loft(["草图1", "草图2", "草图3"])
///
/// Wraps <c>InsertProtrusionBlend</c> (17 args, reflected in M28) with the
/// same educated defaults as <c>CreateLoftedRoundToSquareTool</c>.
/// </summary>
public sealed record LoftSpec
{
    /// <summary>
    /// Names of the sketches to loft between, in order. Each name is what
    /// <c>end_sketch</c> returned (typically "草图1" / "Sketch1"). Must contain
    /// at least 2 sketches.
    /// </summary>
    public required IReadOnlyList<string> SketchNames { get; init; }

    /// <summary>
    /// If true, treat the sketch list as a closed loop (last connects back to
    /// first). Default false (open loft). Useful for tire-cross-sections,
    /// torus-like blends, etc.
    /// </summary>
    public bool Closed { get; init; } = false;

    public void Validate()
    {
        if (SketchNames == null || SketchNames.Count < 2)
        {
            throw new McpToolException(
                $"loft needs at least 2 sketches (got {(SketchNames?.Count ?? 0)}). " +
                "Build multiple sketches with start_sketch + sketch_* + end_sketch " +
                "first, then pass their names in order.");
        }
        for (int i = 0; i < SketchNames.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(SketchNames[i]))
            {
                throw new McpToolException(
                    $"sketchNames[{i}] is empty. Pass the name returned from end_sketch.");
            }
        }
    }
}

/// <summary>
/// Specification for a sweep feature: drag a profile sketch along a path
/// sketch to form a solid body. M32 — generic-layer sweep (no parametric
/// helper for this; sweep is too varied to capture in a single spec).
///
/// LLM workflow (a simple pipe):
///   new_part
///   start_sketch("front") → sketch_circle (the pipe cross-section) → end_sketch ("草图1")
///   start_sketch("right") → sketch_line (the pipe path) → end_sketch ("草图2")
///   sweep(profileSketchName="草图1", pathSketchName="草图2")
///
/// Wraps <c>InsertProtrusionSwept</c> (14 args, reflected — minimal version,
/// MVP-sufficient). v1 PR #27 selection convention: profile mark=1, path mark=4.
/// </summary>
public sealed record SweepSpec
{
    /// <summary>Name of the cross-section profile sketch (must form a closed area).</summary>
    public required string ProfileSketchName { get; init; }

    /// <summary>Name of the path sketch (open curve along which the profile is swept).</summary>
    public required string PathSketchName { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileSketchName))
        {
            throw new McpToolException(
                "profileSketchName must not be empty. Pass the name of a closed-profile sketch.");
        }
        if (string.IsNullOrWhiteSpace(PathSketchName))
        {
            throw new McpToolException(
                "pathSketchName must not be empty. Pass the name of an open-curve sketch.");
        }
        if (string.Equals(ProfileSketchName, PathSketchName, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"profileSketchName and pathSketchName must differ (both got '{ProfileSketchName}').");
        }
    }
}

/// <summary>
/// Specification for a rib (stiffener / gusset) feature: thicken an OPEN
/// sketch contour into a structural rib that fills toward the existing body
/// walls. M35 generic-layer feature.
///
/// LLM workflow (gusset rib in an L-bracket extruded along Z):
///   ... build the L-bracket body ...
///   add_ref_plane("front", 15)              ← a plane mid-way along the bracket
///   start_sketch("基准面1") → sketch_line (a diagonal across the inner corner) → end_sketch
///   rib("草图N", thickness=6)
///
/// Wraps SW's <c>InsertRib</c> (10 args, reflected). InsertRib returns void,
/// so success is detected by scanning for a "Rib" feature afterward, not a
/// return value. The rib sketch must be an OPEN contour positioned so the rib
/// can reach the body walls; thickness is applied normal to the sketch plane.
/// </summary>
public sealed record RibSpec
{
    /// <summary>Name of the open-contour sketch to thicken into a rib (from end_sketch).</summary>
    public required string SketchName { get; init; }

    /// <summary>Rib thickness in mm (applied normal to the sketch plane). Must be &gt; 0.</summary>
    public required double ThicknessMm { get; init; }

    /// <summary>
    /// If true, flip the side the rib material fills toward. Default false.
    /// The tool auto-detects the fill direction, so this is rarely needed.
    /// </summary>
    public bool Reverse { get; init; } = false;

    private const double MinThicknessMm = 0.1;
    private const double MaxThicknessMm = 1000.0;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SketchName))
        {
            throw new McpToolException(
                "sketchName must not be empty. Pass the name returned from end_sketch.");
        }
        if (double.IsNaN(ThicknessMm) || double.IsInfinity(ThicknessMm) || ThicknessMm <= 0)
        {
            throw new McpToolException(
                $"thickness must be > 0 mm (got {ThicknessMm}). Hint: pass millimeters, e.g. 6.");
        }
        if (ThicknessMm < MinThicknessMm || ThicknessMm > MaxThicknessMm)
        {
            throw new McpToolException(
                $"thickness {ThicknessMm} mm is outside the supported range " +
                $"[{MinThicknessMm}, {MaxThicknessMm}] mm.");
        }
    }
}
