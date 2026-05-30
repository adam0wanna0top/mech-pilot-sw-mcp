using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding an equal-distance chamfer to every edge of an
/// existing part. Sibling of <see cref="FilletSpec"/>: same open → edit → save
/// pipeline, but produces a chamfer (45° equal-width cut) instead of a
/// rounded edge. All lengths in millimeters; SW Interop converts to meters at
/// the boundary.
/// </summary>
public sealed record ChamferSpec
{
    /// <summary>Absolute path to an existing .sldprt to chamfer. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>Chamfer width (equal distance on both sides) in mm. Must be &gt; 0.</summary>
    public required double DistanceMm { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input file
    /// is overwritten in place; when given, its parent directory must exist.
    /// </summary>
    public string? OutputPath { get; init; }

    // Sanity bounds — chamfer widths are small; catch unit confusion / negatives.
    private const double MinDistanceMm = 0.01;
    private const double MaxDistanceMm = 1_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (double.IsNaN(DistanceMm) || double.IsInfinity(DistanceMm) || DistanceMm <= 0)
        {
            throw new McpToolException(
                $"distance must be > 0 mm (got {DistanceMm}). " +
                "Hint: pass millimeters, e.g. 2 for a 2 mm chamfer.");
        }
        if (DistanceMm < MinDistanceMm || DistanceMm > MaxDistanceMm)
        {
            throw new McpToolException(
                $"distance {DistanceMm} mm is outside the supported range " +
                $"[{MinDistanceMm}, {MaxDistanceMm}] mm.");
        }

        ValidateInputPath(InputPath);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
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
                "Create the part first (e.g. with create_cylinder / create_flange).");
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
