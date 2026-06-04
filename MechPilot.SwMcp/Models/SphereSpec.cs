using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a parametric solid sphere — the M23 hemisphere
/// sibling, full-circle revolved instead of quarter-circle. LLM-facing
/// helper: LLM gives a diameter and gets a sphere out, no sketch /
/// centerline / revolve reasoning required.
///
/// Geometry: half-disc profile (one diameter line + one 180° arc) sketched
/// on the Front Plane, revolved 360° around the Y axis. Bounding box is
/// D × D × D — note Y extent is D (the full diameter), not D/2 as for
/// hemisphere; this is how the LLM distinguishes "sphere" from "hemisphere"
/// at the inspect level.
///
/// LLM use case: "画一个 D40 实心球" → create_sphere diameter=40 →
/// 40 × 40 × 40 mm sphere centered on the origin.
///
/// Unlocks LLM-friendly LLM-irreplaceable atomic shapes:
///   • 球阀芯 / 滚珠 / 球形支撑
///   • 装饰球 / 玻璃珠 / 圆球把手
///   • 球关节 (paired with cylindrical socket for a future ball-and-socket joint)
/// </summary>
public sealed record SphereSpec
{
    /// <summary>Sphere outer diameter in mm. Must be &gt; 0.</summary>
    public required double DiameterMm { get; init; }

    /// <summary>
    /// Absolute output path with .sldprt extension. Parent directory must exist.
    /// </summary>
    public required string SavePath { get; init; }

    // Same sanity bounds as CylinderSpec / HemisphereSpec.
    private const double MinDimMm = 0.1;
    private const double MaxDimMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (double.IsNaN(DiameterMm) || double.IsInfinity(DiameterMm) || DiameterMm <= 0)
        {
            throw new McpToolException(
                $"diameter must be > 0 mm (got {DiameterMm}). " +
                "Hint: pass millimeters, e.g. 40 for a D40 sphere.");
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
                "Hint: pass something like 'C:/tmp/sphere.sldprt'.");
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
