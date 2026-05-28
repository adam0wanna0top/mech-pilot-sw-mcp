using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding a constant-radius fillet to every edge of an
/// existing part. Unlike <see cref="CylinderSpec"/> / <see cref="FlangeSpec"/>
/// (which build a part from scratch), this edits an existing .sldprt:
/// open → fillet all edges → save. All lengths in millimeters; SW Interop
/// converts to meters at the boundary.
/// </summary>
public sealed record FilletSpec
{
    /// <summary>Absolute path to an existing .sldprt to fillet. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>Constant fillet radius in mm applied to every edge. Must be &gt; 0.</summary>
    public required double RadiusMm { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input file
    /// is overwritten in place; when given, its parent directory must exist.
    /// </summary>
    public string? OutputPath { get; init; }

    // Sanity bounds — fillet radii are small; catch unit confusion / negatives.
    private const double MinRadiusMm = 0.01;
    private const double MaxRadiusMm = 1_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (double.IsNaN(RadiusMm) || double.IsInfinity(RadiusMm) || RadiusMm <= 0)
        {
            throw new McpToolException(
                $"radius must be > 0 mm (got {RadiusMm}). " +
                "Hint: pass millimeters, e.g. 2 for an R2 fillet.");
        }
        if (RadiusMm < MinRadiusMm || RadiusMm > MaxRadiusMm)
        {
            throw new McpToolException(
                $"radius {RadiusMm} mm is outside the supported range " +
                $"[{MinRadiusMm}, {MaxRadiusMm}] mm.");
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
