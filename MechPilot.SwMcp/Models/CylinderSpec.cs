using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a parametric cylinder part. All lengths in millimeters,
/// matching what humans / LLMs naturally write. Internal SW Interop calls
/// convert to meters (SW's native unit) at the boundary.
/// </summary>
public sealed record CylinderSpec
{
    /// <summary>Cylinder outer diameter in mm. Must be &gt; 0.</summary>
    public required double DiameterMm { get; init; }

    /// <summary>Extrusion length in mm. Must be &gt; 0.</summary>
    public required double LengthMm { get; init; }

    /// <summary>
    /// Absolute output path with .sldprt extension. Parent directory must exist.
    /// </summary>
    public required string SavePath { get; init; }

    // Sanity bounds: a 10 m cylinder is almost certainly an LLM unit-confusion bug
    // (passing meters instead of mm); a 0.01 mm cylinder is below sketch precision.
    private const double MinDimMm = 0.1;
    private const double MaxDimMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (double.IsNaN(DiameterMm) || double.IsInfinity(DiameterMm) || DiameterMm <= 0)
        {
            throw new McpToolException(
                $"diameter must be > 0 mm (got {DiameterMm}). " +
                "Hint: pass millimeters, e.g. 30 for a 30 mm cylinder.");
        }
        if (DiameterMm < MinDimMm || DiameterMm > MaxDimMm)
        {
            throw new McpToolException(
                $"diameter {DiameterMm} mm is outside the supported range " +
                $"[{MinDimMm}, {MaxDimMm}] mm.");
        }

        if (double.IsNaN(LengthMm) || double.IsInfinity(LengthMm) || LengthMm <= 0)
        {
            throw new McpToolException(
                $"length must be > 0 mm (got {LengthMm}). " +
                "Hint: pass millimeters, e.g. 50 for a 50 mm cylinder.");
        }
        if (LengthMm < MinDimMm || LengthMm > MaxDimMm)
        {
            throw new McpToolException(
                $"length {LengthMm} mm is outside the supported range " +
                $"[{MinDimMm}, {MaxDimMm}] mm.");
        }

        if (string.IsNullOrWhiteSpace(SavePath))
        {
            throw new McpToolException("savePath must not be empty.");
        }
        if (!Path.IsPathRooted(SavePath))
        {
            throw new McpToolException(
                $"savePath must be absolute (got '{SavePath}'). " +
                "Hint: pass something like 'C:/tmp/cyl.sldprt'.");
        }
        if (!SavePath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"savePath must end in .sldprt (got '{SavePath}').");
        }

        var dir = Path.GetDirectoryName(SavePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"savePath has no parent directory: '{SavePath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"savePath parent directory does not exist: '{dir}'. " +
                "Create it first or pick an existing folder.");
        }
    }
}
