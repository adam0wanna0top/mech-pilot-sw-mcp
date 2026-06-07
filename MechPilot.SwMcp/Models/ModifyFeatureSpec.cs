using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for editing an existing feature's primary dimension and
/// regenerating. Two modes (M38 + M44):
///   • ACTIVE-doc mode (<see cref="PartPath"/> null/empty): edits the live part
///     the generic layer is building — the original "mechanical Cursor" tweak
///     loop. Does not save (the caller saves later with save_part).
///   • FILE mode (<see cref="PartPath"/> set): opens that .sldprt, edits,
///     rebuilds, and SAVES (in place, or to <see cref="OutputPath"/>) — so an
///     assembly's component parts can be resized in place (the part-side
///     counterpart of modify_mate).
///
/// <see cref="Value"/> is the feature's natural primary dimension:
///   • extrude / cut  → blind depth in mm
///   • revolve / revolve-cut → angle in degrees
/// </summary>
public sealed record ModifyFeatureSpec
{
    /// <summary>
    /// What to edit: a bare feature name (→ its primary dimension "D1@&lt;feature&gt;")
    /// or a full dimension name from inspect_* editableDimensions (e.g.
    /// "D1@凸台-拉伸1" / "D2@草图1"). M45.
    /// </summary>
    public required string FeatureName { get; init; }

    /// <summary>
    /// New value for the feature's primary dimension — mm for extrude/cut depth,
    /// degrees for revolve angle. Must be a finite number &gt; 0.
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// Optional absolute .sldprt path. Null/empty = edit the ACTIVE part (M38).
    /// Set = open this saved part file, edit, and save (M44) — e.g. an assembly
    /// component during a resize.
    /// </summary>
    public string? PartPath { get; init; }

    /// <summary>
    /// Optional output .sldprt (FILE mode only). Null/empty = overwrite
    /// <see cref="PartPath"/> in place.
    /// </summary>
    public string? OutputPath { get; init; }

    private const double MaxValue = 100_000.0;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FeatureName))
        {
            throw new McpToolException(
                "featureName must not be empty. Pass a feature name from " +
                "inspect_active / inspect_part (e.g. '凸台-拉伸1').");
        }
        if (double.IsNaN(Value) || double.IsInfinity(Value) || Value <= 0)
        {
            throw new McpToolException(
                $"value must be a finite number > 0 (got {Value}). It is the new " +
                "depth (mm), angle (degrees) or radius (mm) depending on the feature type.");
        }
        if (Value > MaxValue)
        {
            throw new McpToolException(
                $"value {Value} is implausibly large (> {MaxValue}).");
        }
        if (!string.IsNullOrWhiteSpace(PartPath))
        {
            if (!Path.IsPathRooted(PartPath))
            {
                throw new McpToolException($"partPath must be absolute (got '{PartPath}').");
            }
            if (!PartPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpToolException($"partPath must end in .sldprt (got '{PartPath}').");
            }
            if (!File.Exists(PartPath))
            {
                throw new McpToolException($"partPath does not exist: '{PartPath}'.");
            }
        }
        if (!string.IsNullOrWhiteSpace(OutputPath) &&
            !OutputPath!.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException($"outputPath must end in .sldprt (got '{OutputPath}').");
        }
    }
}
