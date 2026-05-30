using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding one GB/T 196 metric coarse threaded hole (tap)
/// at the centroid of the part's ±Z end face. Maps an LLM-friendly
/// "M3 / M4 / M5 / M6 / M8 / M10 / M12" thread size to SW HoleWizard5's
/// GB-tap path (StandardIndex=13, FastenerType=359, plus the 4 "magic"
/// Value positions broken by v1 PR #24 from a recorded macro).
///
/// LLM use case: "在端盖中心加一个 M6 螺纹孔" → one tool call.
/// For multi-hole patterns: drill one threaded hole here, then
/// pattern_linear / pattern_circular it. For PCD bolt circles use
/// create_flange (one-shot disk + bolt clearance holes).
///
/// Position is fixed to the face centroid (v1 PR #24 design — simpler
/// LLM surface, covers 90% of "add a screw hole" requests). Off-center
/// HoleWizard placement is a future PR.
/// </summary>
public sealed record ThreadedHoleSpec
{
    /// <summary>Absolute path to an existing .sldprt to edit. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Thread size keyword: "M3" / "M4" / "M5" / "M6" / "M8" / "M10" / "M12"
    /// (case-insensitive). Each maps to GB/T 196 metric coarse drill + pitch.
    /// </summary>
    public required string ThreadSize { get; init; }

    /// <summary>
    /// Blind tap depth in mm. <c>null</c> or omitted = through-all tap;
    /// positive value = blind tap that deep below the end face.
    /// </summary>
    public double? DepthMm { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// GB/T 196-2003 metric-coarse tap table: thread size → (tap drill
    /// diameter, pitch) in mm. Public so the tool layer can look these up
    /// when building HoleWizard5's args (Diameter, Value2).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (double DrillDiameterMm, double PitchMm)>
        GbTapTable = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["M3"] = (2.5, 0.5),
            ["M4"] = (3.3, 0.7),
            ["M5"] = (4.2, 0.8),
            ["M6"] = (5.0, 1.0),
            ["M8"] = (6.8, 1.25),
            ["M10"] = (8.5, 1.5),
            ["M12"] = (10.2, 1.75),
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
                string.Join(", ", GbTapTable.Keys) + ".");
        }
        if (!GbTapTable.ContainsKey(threadSize))
        {
            throw new McpToolException(
                $"threadSize '{threadSize}' is not in the GB metric-coarse table. " +
                $"Supported: {string.Join(", ", GbTapTable.Keys)}.");
        }
    }

    private static void ValidateDepth(double? depth)
    {
        if (!depth.HasValue) return;  // null = through-all, valid
        var d = depth.Value;
        if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
        {
            throw new McpToolException(
                $"depth must be > 0 mm or omitted for through-all (got {d}). " +
                "Hint: omit depth for a through-tap; pass mm for a blind tap.");
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
                "Create the part first (e.g. with create_rectangular_block / create_flange).");
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
