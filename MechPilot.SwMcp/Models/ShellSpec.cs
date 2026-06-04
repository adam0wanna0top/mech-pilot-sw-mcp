using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for shelling an existing solid part — hollow it out leaving
/// a uniform wall thickness, opening up the +Z end face so the result is a
/// cup-like container (the most common LLM intent for "shell").
///
/// LLM use case: "把这个 D40 圆柱抽壳 2mm" → add_shell thickness=2 → D40
/// cylinder becomes a cup with 2 mm walls and an open top.
///
/// Unlocks LLM-irreplaceable parts (subtractive geometry that can't be
/// approximated by composition):
///   • 电机壳 / 泵壳 / 减速箱外壳
///   • 杯具 / 罐体 / 容器
///   • 接线盒 / 端子盒 / IP6X 防护壳
///
/// MVP scope (M26):
///   • Auto-finds the +Z planar end face as the "removed face" (the opening).
///     Reuses PartGeometryHelpers.FindPlanarEndFace — works for cylinder /
///     block / frustum (axis-aligned-Z extruded parts); hemispheres (axis +Y)
///     are not directly supported in this PR but will be reachable via a
///     future closed-shell or face-selection mode.
///   • Outward=false (default) shells inward (the body's outer geometry stays
///     the same, the interior is hollowed). Outward=true thickens outward.
///   • Closed-shell mode (no opening) and multi-face open-shell are future PR.
///
/// SW API: <c>IModelDoc2.InsertFeatureShell(Thickness, Outward)</c> returns
/// void — no success/failure signal. L2 verifies via inspect_part that the
/// featureCount increased and a feature with typeName="Shell" appeared. This
/// is the same "geometry-not-just-API-ok" pattern M22 收尾 established for
/// pattern_circular.
/// </summary>
public sealed record ShellSpec
{
    /// <summary>Absolute path to an existing .sldprt to shell. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Wall thickness in mm. Must be &gt; 0; practical range (0.01, 100] mm
    /// to catch unit-confusion / sketch-precision bugs at the spec layer.
    /// </summary>
    public required double ThicknessMm { get; init; }

    /// <summary>
    /// When false (default) the body is shelled inward — the outer geometry
    /// stays the same and the interior is hollowed. When true the body is
    /// thickened outward — the original outer surface becomes the inner wall
    /// and the body grows by <see cref="ThicknessMm"/>. LLM-friendly default
    /// is inward shelling.
    /// </summary>
    public bool Outward { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    // Sanity bounds.
    private const double MinThicknessMm = 0.01;
    private const double MaxThicknessMm = 100.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateThickness(ThicknessMm);
        ValidateInputPath(InputPath);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
    }

    private static void ValidateThickness(double thicknessMm)
    {
        if (double.IsNaN(thicknessMm) || double.IsInfinity(thicknessMm) || thicknessMm <= 0)
        {
            throw new McpToolException(
                $"thickness must be > 0 mm (got {thicknessMm}). " +
                "Hint: pass millimeters, e.g. 2 for a 2 mm wall.");
        }
        if (thicknessMm < MinThicknessMm || thicknessMm > MaxThicknessMm)
        {
            throw new McpToolException(
                $"thickness {thicknessMm} mm is outside the supported range " +
                $"[{MinThicknessMm}, {MaxThicknessMm}] mm. " +
                "Most real-world shells are 0.5-10 mm; > 100 mm is almost certainly a unit-confusion bug.");
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
                "Create the part first (e.g. with create_cylinder).");
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
