using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a parametric rectangular block (cuboid) part. Sibling
/// of <see cref="CylinderSpec"/> — same Front-Plane sketch + blind extrude
/// pattern — but uses a centered rectangle instead of a circle. All lengths
/// in millimeters; SW Interop converts to meters at the boundary.
///
/// Coordinate mapping (Front Plane = XY, extrude along +Z):
///   <see cref="LengthMm"/> → block's X extent (face-local width)
///   <see cref="WidthMm"/>  → block's Y extent (face-local height)
///   <see cref="HeightMm"/> → extrusion depth along Z
///
/// LLM use case: "做一个 100×50×20 mm 的支架底板" → one tool call. Also
/// unblocks future pattern_linear (rectangular blocks have straight edges
/// that LinearPattern's direction-edge mark=1 can latch onto, unlike
/// cylinders / flanges).
/// </summary>
public sealed record RectangularBlockSpec
{
    /// <summary>Block length (X extent) in mm. Must be &gt; 0.</summary>
    public required double LengthMm { get; init; }

    /// <summary>Block width (Y extent) in mm. Must be &gt; 0.</summary>
    public required double WidthMm { get; init; }

    /// <summary>Block height (Z extrusion depth) in mm. Must be &gt; 0.</summary>
    public required double HeightMm { get; init; }

    /// <summary>
    /// Absolute output path with .sldprt extension. Parent directory must exist.
    /// </summary>
    public required string SavePath { get; init; }

    // Sanity bounds: same rationale as CylinderSpec — catch LLM unit confusion.
    private const double MinDimMm = 0.1;
    private const double MaxDimMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateDim(LengthMm, "length");
        ValidateDim(WidthMm, "width");
        ValidateDim(HeightMm, "height");
        ValidateSavePath(SavePath);
    }

    private static void ValidateDim(double value, string fieldName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            throw new McpToolException(
                $"{fieldName} must be > 0 mm (got {value}). " +
                $"Hint: pass millimeters, e.g. 100 for a 100 mm {fieldName}.");
        }
        if (value < MinDimMm || value > MaxDimMm)
        {
            throw new McpToolException(
                $"{fieldName} {value} mm is outside the supported range " +
                $"[{MinDimMm}, {MaxDimMm}] mm.");
        }
    }

    private static void ValidateSavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            throw new McpToolException("savePath must not be empty.");
        }
        if (!Path.IsPathRooted(savePath))
        {
            throw new McpToolException(
                $"savePath must be absolute (got '{savePath}'). " +
                "Hint: pass something like 'C:/tmp/block.sldprt'.");
        }
        if (!savePath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"savePath must end in .sldprt (got '{savePath}').");
        }

        var dir = Path.GetDirectoryName(savePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"savePath has no parent directory: '{savePath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"savePath parent directory does not exist: '{dir}'. " +
                "Create it first or pick an existing folder.");
        }
    }
}
