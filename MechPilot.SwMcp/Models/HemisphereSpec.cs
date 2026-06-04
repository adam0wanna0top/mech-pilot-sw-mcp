using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a parametric solid hemisphere — the simplest revolved
/// part. LLM-facing helper alongside create_cylinder / create_flange /
/// create_rectangular_block: LLM gives a diameter and gets a hemisphere out,
/// no sketch / centerline / revolve-angle reasoning required.
///
/// Geometry: full upper hemisphere with axis along +Y (sketched on the Front
/// Plane and revolved 360° around the Y axis). Bounding box D × D/2 × D
/// (X × Y × Z) with X ∈ [−R, R], Y ∈ [0, R], Z ∈ [−R, R]. This intentionally
/// keeps the sketch coordinates one-to-one with world XY (no Right/Top Plane
/// sketch-orientation pitfalls) — the +Y axis convention is documented for
/// the LLM and is mate-able if a different orientation is later needed in
/// an assembly.
///
/// LLM use case: "画一个 D60 的半球" → create_hemisphere diameter=60 →
/// 60 × 30 × 60 mm hemisphere centered on the Y axis, base on Y=0.
/// </summary>
public sealed record HemisphereSpec
{
    /// <summary>Hemisphere outer diameter in mm. Must be &gt; 0.</summary>
    public required double DiameterMm { get; init; }

    /// <summary>
    /// Absolute output path with .sldprt extension. Parent directory must exist.
    /// </summary>
    public required string SavePath { get; init; }

    // Same sanity bounds as CylinderSpec — a 10 m hemisphere is almost certainly
    // an LLM unit-confusion bug (passing meters instead of mm); 0.01 mm is below
    // sketch precision.
    private const double MinDimMm = 0.1;
    private const double MaxDimMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (double.IsNaN(DiameterMm) || double.IsInfinity(DiameterMm) || DiameterMm <= 0)
        {
            throw new McpToolException(
                $"diameter must be > 0 mm (got {DiameterMm}). " +
                "Hint: pass millimeters, e.g. 60 for a D60 hemisphere.");
        }
        if (DiameterMm < MinDimMm || DiameterMm > MaxDimMm)
        {
            throw new McpToolException(
                $"diameter {DiameterMm} mm is outside the supported range " +
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
                "Hint: pass something like 'C:/tmp/hemi.sldprt'.");
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
