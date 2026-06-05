using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a round-to-square lofted transition — the M28
/// "first multi-plane sketch" parametric helper. Bottom face is a circle
/// (diameter <see cref="BottomDiameterMm"/>) in the Front Plane; top face
/// is a centered rectangle (<see cref="TopLengthMm"/> × <see cref="TopWidthMm"/>)
/// on a reference plane offset <see cref="HeightMm"/> above. SW lofts a
/// smooth blend body between them via <c>InsertProtrusionBlend</c>.
///
/// LLM use case: "做一个底圆 D60 → 顶方 40×40 高 30 的风管接头"
///   → create_lofted_round_to_square bottomDiameter=60 topLength=40
///     topWidth=40 height=30.
///
/// Unlocks LLM-irreplaceable atomic shapes:
///   • HVAC 风管转接 / 空调出风口
///   • 漏斗式集料口 / 喇叭口转方形出料
///   • 圆烟囱接方形排烟道
///   • 圆形进风口转矩形机箱
///   • 任何"圆变方"的工业过渡件
///
/// Geometry: Z extent = HeightMm (Z ∈ [0, H]); X / Y extents are
/// max(bottomDiameter, topLength) / max(bottomDiameter, topWidth) —
/// the loft body's bbox conservatively encloses both profiles.
/// </summary>
public sealed record LoftedRoundToSquareSpec
{
    /// <summary>Bottom-face circle diameter in mm. Must be &gt; 0.</summary>
    public required double BottomDiameterMm { get; init; }

    /// <summary>Top-face rectangle X extent (length) in mm. Must be &gt; 0.</summary>
    public required double TopLengthMm { get; init; }

    /// <summary>Top-face rectangle Y extent (width) in mm. Must be &gt; 0.</summary>
    public required double TopWidthMm { get; init; }

    /// <summary>
    /// Vertical offset (Z direction) from bottom to top face in mm. Must be &gt; 0.
    /// This is the height of the loft body along Z.
    /// </summary>
    public required double HeightMm { get; init; }

    /// <summary>
    /// Absolute output path with .sldprt extension. Parent directory must exist.
    /// </summary>
    public required string SavePath { get; init; }

    // Same sanity bounds as the other create_* specs.
    private const double MinDimMm = 0.1;
    private const double MaxDimMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateDim(BottomDiameterMm, "bottomDiameter");
        ValidateDim(TopLengthMm, "topLength");
        ValidateDim(TopWidthMm, "topWidth");
        ValidateDim(HeightMm, "height");
        ValidateSavePath(SavePath);
    }

    private static void ValidateDim(double mm, string field)
    {
        if (double.IsNaN(mm) || double.IsInfinity(mm) || mm <= 0)
        {
            throw new McpToolException(
                $"{field} must be > 0 mm (got {mm}). " +
                "Hint: pass millimeters, e.g. 60 for a 60 mm dimension.");
        }
        if (mm < MinDimMm || mm > MaxDimMm)
        {
            throw new McpToolException(
                $"{field} {mm} mm is outside the supported range " +
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
                "Hint: pass something like 'C:/tmp/transition.sldprt'.");
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
