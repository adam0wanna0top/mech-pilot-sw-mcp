using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a parametric solid frustum (truncated cone) — the
/// revolved-geometry sibling of cylinder/hemisphere that handles tapered
/// shapes. LLM-facing helper: LLM gives baseDiameter / topDiameter / height
/// and gets a frustum out, no sketch / centerline / revolve reasoning needed.
///
/// Geometry: trapezoid profile sketched on the Front Plane with the base
/// circle at Y=0 and the top circle at Y=height, revolved 360° around the
/// Y axis. Bounding box baseD × height × baseD (X × Y × Z) with X/Z ∈
/// [−baseR, baseR], Y ∈ [0, height]. Axis along +Y matches create_hemisphere
/// (NOT cylinder's +Z) — same Front-Plane rationale: sketch X = world X /
/// sketch Y = world Y unambiguously.
///
/// LLM use case: "画一个底径 60 顶径 30 高 40 的圆锥台" →
/// create_frustum baseDiameter=60 topDiameter=30 height=40 →
/// 60 × 40 × 60 mm frustum, axis +Y.
///
/// Constraints (documented for LLM):
///   • topDiameter &lt; baseDiameter strictly — for equal, use create_cylinder;
///     for top &gt; base (inverted frustum) is exotic and not supported yet
///   • topDiameter &gt;= 0.1 mm spec-side, but in practice SW sketch precision
///     rejects the tiny top-radius line below ~1 mm — empirical M24 L2
///     finding: topD=1 mm makes ISketchManager.CreateLine return null. LLM
///     should use topD &gt;= 2-3 mm for safety; true cones (topD=0) await a
///     future create_cone tool with degenerate-vertex sketch handling.
/// </summary>
public sealed record FrustumSpec
{
    /// <summary>Base (Y=0) circle diameter in mm. Must be &gt; 0.</summary>
    public required double BaseDiameterMm { get; init; }

    /// <summary>Top (Y=height) circle diameter in mm. Must be &gt; 0 and &lt; BaseDiameterMm.</summary>
    public required double TopDiameterMm { get; init; }

    /// <summary>Frustum height along +Y in mm. Must be &gt; 0.</summary>
    public required double HeightMm { get; init; }

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
        ValidateDim(BaseDiameterMm, "baseDiameter", "60 for a 60 mm bottom diameter");
        ValidateDim(TopDiameterMm, "topDiameter", "30 for a 30 mm top diameter");
        ValidateDim(HeightMm, "height", "40 for a 40 mm tall frustum");

        if (TopDiameterMm >= BaseDiameterMm)
        {
            throw new McpToolException(
                $"topDiameter ({TopDiameterMm}) must be strictly less than baseDiameter " +
                $"({BaseDiameterMm}). If they should be equal, use create_cylinder instead — " +
                "a frustum with equal diameters is just a cylinder. An inverted frustum " +
                "(top > base) is not supported yet.");
        }

        ValidateSavePath(SavePath);
    }

    private static void ValidateDim(double valueMm, string fieldName, string hintExample)
    {
        if (double.IsNaN(valueMm) || double.IsInfinity(valueMm) || valueMm <= 0)
        {
            throw new McpToolException(
                $"{fieldName} must be > 0 mm (got {valueMm}). " +
                $"Hint: pass millimeters, e.g. {hintExample}.");
        }
        if (valueMm < MinDimMm || valueMm > MaxDimMm)
        {
            throw new McpToolException(
                $"{fieldName} {valueMm} mm is outside the supported range " +
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
                "Hint: pass something like 'C:/tmp/frustum.sldprt'.");
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
