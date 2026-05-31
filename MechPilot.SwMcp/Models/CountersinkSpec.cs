using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding one GB/T 152.2 countersink hole (锥形沉头孔, 90°)
/// at the centroid of the part's ±Z end face. Used by flat-head (sink-head)
/// screws (沉头螺钉 GB/T 819 / ISO 7046, 90° standard).
///
/// Maps "M6 / M8 / M10 / M12" to SW HoleWizard5's GB-CounterSink path
/// (StandardIndex=13, FastenerType=363, from v1 PR #25's recorded macro).
///
/// **M3 / M4 / M5 not supported**: v1 PR #25 found SW's internal GB
/// countersink database is missing the smaller sizes, and HoleWizard5
/// silently returns null on those. Spec rejects them up-front with a friendly
/// hint. If you need a small flat-head clearance hole, use add_axial_hole
/// with a manual diameter (no real countersink feature).
///
/// LLM use case: "在端盖上加 4 个 M8 沉头螺钉孔" — drill one with
/// add_countersink, then pattern_linear / pattern_circular it.
/// </summary>
public sealed record CountersinkSpec
{
    /// <summary>Absolute path to an existing .sldprt to edit. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Thread size keyword: "M6" / "M8" / "M10" / "M12" (case-insensitive).
    /// Smaller sizes (M3/M4/M5) are rejected — see class summary.
    /// </summary>
    public required string ThreadSize { get; init; }

    /// <summary>
    /// Blind clearance-hole depth in mm. <c>null</c> or omitted = through-all
    /// clearance (the countersink itself takes a fixed cone defined by
    /// GB/T 152.2 and is independent of this depth).
    /// </summary>
    public double? DepthMm { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// GB/T 152.2 table for flat-head (sink-head) screws (90° angle):
    /// thread size → (clearance hole dia, countersink major dia) all in mm.
    /// </summary>
    public static readonly IReadOnlyDictionary<string,
            (double ClearanceMm, double CsDiameterMm)>
        GbTable = new Dictionary<string, (double, double)>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["M6"] = (6.6, 12.4),
            ["M8"] = (9.0, 16.4),
            ["M10"] = (11.0, 20.4),
            ["M12"] = (13.5, 24.4),
        };

    private const double MinDepthMm = 0.1;
    private const double MaxDepthMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateThreadSize(ThreadSize);
        ValidateDepth(DepthMm);
        ValidateInputPath(InputPath);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
    }

    private static void ValidateThreadSize(string threadSize)
    {
        if (string.IsNullOrWhiteSpace(threadSize))
        {
            throw new McpToolException(
                "threadSize must not be empty. Use one of: " +
                string.Join(", ", GbTable.Keys) + ".");
        }
        if (!GbTable.ContainsKey(threadSize))
        {
            throw new McpToolException(
                $"threadSize '{threadSize}' is not in the GB/T 152.2 countersink table. " +
                $"Supported: {string.Join(", ", GbTable.Keys)}. " +
                "(M3/M4/M5 are not supported — SW's internal GB countersink database " +
                "is missing those sizes. Use add_axial_hole for small flat-head clearance.)");
        }
    }

    private static void ValidateDepth(double? depth)
    {
        if (!depth.HasValue) return;
        var d = depth.Value;
        if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
        {
            throw new McpToolException(
                $"depth must be > 0 mm or omitted for through-all (got {d}).");
        }
        if (d < MinDepthMm || d > MaxDepthMm)
        {
            throw new McpToolException(
                $"depth {d} mm is outside the supported range [{MinDepthMm}, {MaxDepthMm}] mm.");
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
