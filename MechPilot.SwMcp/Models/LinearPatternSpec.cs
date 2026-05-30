using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a linear pattern (one- or two-direction) of a single
/// seed feature. Counts include the seed; spacings are center-to-center in mm.
///
/// LLM use case: "在 100×50 板上 3×5 阵列 Φ5 孔" →
///   create_rectangular_block 100×50×20 →
///   add_axial_hole at corner offset →
///   pattern_linear count1=3 spacing1=20 axis1=x count2=5 spacing2=10 axis2=y.
///
/// Why we require an axis keyword rather than a direction-edge name: the
/// edges of a freshly-created block have no LLM-knowable names; the tool
/// instead walks the part's first solid body, finds the first straight
/// edge whose unit direction matches the requested ±axis (cos similarity
/// > 0.99), and selects that as the FeatureLinearPattern2 direction edge.
/// </summary>
public sealed record LinearPatternSpec
{
    /// <summary>Absolute path to an existing .sldprt to edit. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>Direction-1 axis keyword: "x" / "y" / "z" (case-insensitive).</summary>
    public required string Direction1Axis { get; init; }

    /// <summary>Total instances along direction 1, including the seed. Must &gt;= 2.</summary>
    public required int CountDir1 { get; init; }

    /// <summary>Center-to-center spacing along direction 1 in mm. Must &gt; 0.</summary>
    public required double SpacingDir1Mm { get; init; }

    /// <summary>
    /// Optional direction-2 axis keyword (different from Direction1Axis).
    /// null or empty = single-direction pattern.
    /// </summary>
    public string? Direction2Axis { get; init; }

    /// <summary>
    /// Total instances along direction 2 (including seed). Default 1
    /// (= single-direction). When <see cref="Direction2Axis"/> is set, must &gt;= 2.
    /// </summary>
    public int CountDir2 { get; init; } = 1;

    /// <summary>Center-to-center spacing along direction 2 in mm. Required when Direction2Axis is set.</summary>
    public double SpacingDir2Mm { get; init; }

    /// <summary>
    /// Optional exact seed feature name (e.g. "Cut-Extrude1"). When null or
    /// empty the tool picks the last user-meaningful feature (same boot
    /// filter as inspect_part / mirror_feature).
    /// </summary>
    public string? FeatureName { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>Recognized axis keywords (case-insensitive).</summary>
    public static readonly IReadOnlySet<string> AxisKeywords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "x", "y", "z" };

    // Sanity bounds — same rationale as other specs.
    private const double MinSpacingMm = 0.01;
    private const double MaxSpacingMm = 10_000.0;
    private const int MaxCount = 1_000;   // 10×100 grid is already extreme; cap to catch typos.

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAxis(Direction1Axis, "direction1Axis");
        ValidateCount(CountDir1, "countDir1", minimum: 2);
        ValidateSpacing(SpacingDir1Mm, "spacingDir1");

        if (!string.IsNullOrWhiteSpace(Direction2Axis))
        {
            ValidateAxis(Direction2Axis!, "direction2Axis");
            if (string.Equals(Direction1Axis, Direction2Axis,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new McpToolException(
                    $"direction2Axis must differ from direction1Axis (both got '{Direction1Axis}').");
            }
            ValidateCount(CountDir2, "countDir2", minimum: 2);
            ValidateSpacing(SpacingDir2Mm, "spacingDir2");
        }
        else
        {
            // Single-direction: CountDir2 must stay at 1 (or default 0 also ok).
            if (CountDir2 > 1)
            {
                throw new McpToolException(
                    $"countDir2={CountDir2} but no direction2Axis given. " +
                    "Either pass direction2Axis or leave countDir2 at 1.");
            }
        }

        ValidateInputPath(InputPath);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
    }

    private static void ValidateAxis(string axis, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(axis))
        {
            throw new McpToolException(
                $"{fieldName} must not be empty. Use 'x', 'y', or 'z'.");
        }
        if (!AxisKeywords.Contains(axis))
        {
            throw new McpToolException(
                $"{fieldName} '{axis}' is not recognized. Supported: x, y, z.");
        }
    }

    private static void ValidateCount(int count, string fieldName, int minimum)
    {
        if (count < minimum)
        {
            throw new McpToolException(
                $"{fieldName} must be >= {minimum} (got {count}). " +
                "Count includes the seed feature.");
        }
        if (count > MaxCount)
        {
            throw new McpToolException(
                $"{fieldName} {count} exceeds {MaxCount}. " +
                "If you really need that many instances, please request a larger cap.");
        }
    }

    private static void ValidateSpacing(double spacing, string fieldName)
    {
        if (double.IsNaN(spacing) || double.IsInfinity(spacing) || spacing <= 0)
        {
            throw new McpToolException(
                $"{fieldName} must be > 0 mm (got {spacing}). " +
                "Hint: pass millimeters, e.g. 10 for a 10 mm pitch.");
        }
        if (spacing < MinSpacingMm || spacing > MaxSpacingMm)
        {
            throw new McpToolException(
                $"{fieldName} {spacing} mm is outside the supported range " +
                $"[{MinSpacingMm}, {MaxSpacingMm}] mm.");
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
                "Create the part first (e.g. with create_rectangular_block).");
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
