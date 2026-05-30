using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding a single axial (±Z) cylindrical hole to an
/// existing part. Simpler than SW's HoleWizard5 (no fastener standard, no
/// counterbore / countersink) — covers the LLM-most-common case "drill a Φ N
/// through hole (or N-deep blind hole) at (x, y) on the part's end face".
/// All lengths in millimeters; SW Interop converts to meters at the boundary.
///
/// Through-vs-blind is driven by <see cref="DepthMm"/>:
///   • <c>null</c> (or omitted) → through-all (cuts through the entire part)
///   • <c>&gt; 0</c>              → blind, this depth below the end face
///
/// The hole is placed on the first planar face whose normal is along ±Z
/// (same heuristic create_flange uses), at sketch-plane coordinates
/// (<see cref="PositionXMm"/>, <see cref="PositionYMm"/>). For a part
/// extruded from the Front Plane this maps to the end face's local XY
/// system; (0, 0) is the face centroid for symmetric extrusions.
/// </summary>
public sealed record AxialHoleSpec
{
    /// <summary>Absolute path to an existing .sldprt to drill. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>Hole diameter in mm. Must be &gt; 0.</summary>
    public required double DiameterMm { get; init; }

    /// <summary>
    /// Blind depth in mm. <c>null</c> or omitted = through-all cut;
    /// a positive value = blind cut that depth below the end face.
    /// </summary>
    public double? DepthMm { get; init; }

    /// <summary>Hole-center X on the sketch plane in mm. Default 0 (face centroid).</summary>
    public double PositionXMm { get; init; }

    /// <summary>Hole-center Y on the sketch plane in mm. Default 0 (face centroid).</summary>
    public double PositionYMm { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    // Sanity bounds — same rationale as FilletSpec: catch unit confusion / negatives.
    private const double MinSizeMm = 0.1;
    private const double MaxSizeMm = 10_000.0;
    private const double MaxAbsPositionMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateDiameter(DiameterMm);
        ValidateDepth(DepthMm);
        ValidatePosition(PositionXMm, nameof(PositionXMm));
        ValidatePosition(PositionYMm, nameof(PositionYMm));
        ValidateInputPath(InputPath);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
    }

    private static void ValidateDiameter(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
        {
            throw new McpToolException(
                $"diameter must be > 0 mm (got {d}). " +
                "Hint: pass millimeters, e.g. 6.6 for a Φ6.6 clearance hole.");
        }
        if (d < MinSizeMm || d > MaxSizeMm)
        {
            throw new McpToolException(
                $"diameter {d} mm is outside the supported range [{MinSizeMm}, {MaxSizeMm}] mm.");
        }
    }

    private static void ValidateDepth(double? depth)
    {
        if (!depth.HasValue) return;  // null = through-all, valid.
        var d = depth.Value;
        if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
        {
            throw new McpToolException(
                $"depth must be > 0 mm or omitted for through-all (got {d}). " +
                "Hint: omit depth for a through hole; pass mm for a blind hole.");
        }
        if (d < MinSizeMm || d > MaxSizeMm)
        {
            throw new McpToolException(
                $"depth {d} mm is outside the supported range [{MinSizeMm}, {MaxSizeMm}] mm.");
        }
    }

    private static void ValidatePosition(double v, string name)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            throw new McpToolException($"{name} must be a finite number (got {v}).");
        }
        if (Math.Abs(v) > MaxAbsPositionMm)
        {
            throw new McpToolException(
                $"{name} {v} mm exceeds ±{MaxAbsPositionMm} mm sanity bound.");
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
