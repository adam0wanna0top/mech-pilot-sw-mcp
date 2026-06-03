using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a circular (rotational) pattern of a single seed feature
/// around the part's first axial-Z cylindrical face. Count includes the seed.
///
/// LLM use case: "在 D40 圆柱端面 PCD20 圆周阵列 6 个 Φ5 通孔" →
///   create_cylinder D40 L20 →
///   add_axial_hole at (PCD/2, 0) = (10, 0) →
///   pattern_circular count=6 (default 360°).
///
/// Why no axis keyword: unlike pattern_linear (which needs a direction edge),
/// circular pattern needs an axis. All mech-pilot-sw extruded parts are
/// extruded along ±Z, so the natural axis is whichever cylindrical face
/// has its axis along ±Z. The tool walks the body, finds the first such face,
/// and uses it as the axis reference.
///
/// SW limitation (v1 PR #35): if the part already has multiple cut features
/// stacked on the same body (e.g. cylinder + center hole + offset hole),
/// FeatureCircularPattern3 silent-fails along every known path (12 stages
/// probed in v1, including SW-UI-recorded macros replayed verbatim). For
/// flange-class parts with PCD bolt holes, use create_flange (one sketch +
/// one cut, no pattern API).
/// </summary>
public sealed record CircularPatternSpec
{
    /// <summary>Absolute path to an existing .sldprt to edit. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Total instances around the axis, including the seed. Must be &gt;= 2.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>
    /// Total sweep angle in degrees. Default 360 (full circle / equal-pitch
    /// PCD). Use less than 360 for a partial arc (e.g. 180° = half-circle
    /// pattern). Range: (0, 360]. Per-instance spacing is computed as
    /// <c>totalAngle / count</c> (v1 PR #32 真根因：
    /// <c>FeatureCircularPattern3.Spacing</c> with <c>EqualSpacing=false</c>
    /// is per-instance, not total).
    /// </summary>
    public double TotalAngleDeg { get; init; } = 360.0;

    /// <summary>
    /// Optional exact seed feature name (e.g. "Cut-Extrude1"). When null or
    /// empty the tool picks the last user-meaningful feature (same boot
    /// filter as inspect_part / mirror_feature / pattern_linear).
    /// </summary>
    public string? FeatureName { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    // Sanity bounds.
    private const double MinAngleDeg = 1.0;
    private const double MaxAngleDeg = 360.0;
    private const int MinCount = 2;
    private const int MaxCount = 360;   // 1° per instance ceiling.

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateCount(Count);
        ValidateAngle(TotalAngleDeg);
        ValidateInputPath(InputPath);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
    }

    private static void ValidateCount(int count)
    {
        if (count < MinCount)
        {
            throw new McpToolException(
                $"count must be >= {MinCount} (got {count}). " +
                "Count includes the seed feature — a count of 1 is a no-op.");
        }
        if (count > MaxCount)
        {
            throw new McpToolException(
                $"count {count} exceeds {MaxCount}. " +
                "If you really need that many instances, please request a larger cap.");
        }
    }

    private static void ValidateAngle(double angleDeg)
    {
        if (double.IsNaN(angleDeg) || double.IsInfinity(angleDeg))
        {
            throw new McpToolException(
                $"totalAngleDeg must be a finite number (got {angleDeg}).");
        }
        if (angleDeg < MinAngleDeg || angleDeg > MaxAngleDeg)
        {
            throw new McpToolException(
                $"totalAngleDeg {angleDeg} is outside the supported range " +
                $"[{MinAngleDeg}, {MaxAngleDeg}] degrees. " +
                "Use 360 for a full equal-pitch circle (default), or a value < 360 for a partial arc.");
        }
    }

    private static void ValidateInputPath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new McpToolException("inputPath must not be empty.");
        }
        if (!Path.IsPathRooted(inputPath))
        {
            throw new McpToolException(
                $"inputPath must be absolute (got '{inputPath}').");
        }
        if (!inputPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"inputPath must end in .sldprt (got '{inputPath}').");
        }
        if (!File.Exists(inputPath))
        {
            throw new McpToolException(
                $"inputPath does not exist: '{inputPath}'. " +
                "Create the part first (e.g. with create_cylinder + add_axial_hole).");
        }
    }

    private static void ValidateOutputPath(string outputPath)
    {
        if (!Path.IsPathRooted(outputPath))
        {
            throw new McpToolException(
                $"outputPath must be absolute (got '{outputPath}').");
        }
        if (!outputPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"outputPath must end in .sldprt (got '{outputPath}').");
        }
        var dir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"outputPath has no parent directory: '{outputPath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"outputPath parent directory does not exist: '{dir}'.");
        }
    }
}
