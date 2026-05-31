using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding one GB/T 152.3 counterbore hole (柱形沉头孔)
/// at the centroid of the part's ±Z end face. Used by inner-hex socket
/// cylindrical-head screws (内六角圆柱头螺钉 GB/T 70.1 / DIN 912).
///
/// Maps an LLM-friendly "M3 / M4 / M5 / M6 / M8 / M10 / M12" thread size to
/// SW HoleWizard5's GB-CounterBore path (StandardIndex=13, FastenerType=361,
/// from v1 PR #25's recorded macro).
///
/// LLM use case: "在底板上加一个 M6 内六角沉孔" → one tool call. The hole
/// is a clearance hole through the part, with a counterbore deep enough for
/// the screw head to sit flush.
///
/// Position is fixed to the face centroid (same v1 design as add_threaded_hole).
/// For PCD bolt patterns, drill one and pattern_linear / pattern_circular it.
/// </summary>
public sealed record CounterboreSpec
{
    /// <summary>Absolute path to an existing .sldprt to edit. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Thread size keyword: "M3" / "M4" / "M5" / "M6" / "M8" / "M10" / "M12"
    /// (case-insensitive). Looked up against <see cref="GbTable"/>.
    /// </summary>
    public required string ThreadSize { get; init; }

    /// <summary>
    /// Blind clearance-hole depth in mm. <c>null</c> or omitted = through-all
    /// clearance hole (most common); positive value = blind clearance hole
    /// that deep. The counterbore depth itself is fixed by GB/T 152.3, not
    /// by this field.
    /// </summary>
    public double? DepthMm { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// GB/T 152.3 table for inner-hex socket cylindrical-head screws:
    /// thread size → (clearance hole dia, counterbore dia, counterbore depth)
    /// all in mm. Clearance follows GB/T 5277 medium fit.
    /// </summary>
    public static readonly IReadOnlyDictionary<string,
            (double ClearanceMm, double CbDiameterMm, double CbDepthMm)>
        GbTable = new Dictionary<string, (double, double, double)>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["M3"] = (3.4, 6.5, 3.4),
            ["M4"] = (4.5, 8.0, 4.6),
            ["M5"] = (5.5, 10.0, 5.7),
            ["M6"] = (6.6, 11.0, 6.8),
            ["M8"] = (9.0, 15.0, 9.0),
            ["M10"] = (11.0, 18.0, 11.0),
            ["M12"] = (13.5, 20.0, 13.0),
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
                $"threadSize '{threadSize}' is not in the GB/T 152.3 counterbore table. " +
                $"Supported: {string.Join(", ", GbTable.Keys)}.");
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
