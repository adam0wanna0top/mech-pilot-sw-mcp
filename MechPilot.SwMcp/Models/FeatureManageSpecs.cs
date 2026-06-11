using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specs for the M48 feature-management pair (delete_feature /
/// suppress_feature) — the "mechanical Cursor" undo/rollback primitives.
/// Both follow modify_feature's two-mode shape (M38 + M44):
///   • ACTIVE-doc mode (PartPath null/empty): act on the live part being
///     built; no save (save_part later).
///   • FILE mode (PartPath set): open the .sldprt, act, rebuild, save
///     (in place or to OutputPath), close.
/// </summary>
internal static class FeatureManageValidation
{
    public static void ValidateFeatureName(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            throw new McpToolException(
                "featureName must not be empty. Pass a feature name from " +
                "inspect_active / inspect_part (e.g. '凸台-拉伸2').");
        }
    }

    public static void ValidatePaths(string? partPath, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(partPath))
        {
            if (!Path.IsPathRooted(partPath))
            {
                throw new McpToolException($"partPath must be absolute (got '{partPath}').");
            }
            if (!partPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpToolException($"partPath must end in .sldprt (got '{partPath}').");
            }
            if (!File.Exists(partPath))
            {
                throw new McpToolException($"partPath does not exist: '{partPath}'.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(outputPath))
        {
            throw new McpToolException(
                "outputPath is only valid together with partPath (FILE mode).");
        }

        if (!string.IsNullOrWhiteSpace(outputPath) &&
            !outputPath!.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException($"outputPath must end in .sldprt (got '{outputPath}').");
        }
    }
}

/// <summary>
/// Delete a feature (cascading to its absorbed sketch and dependent children)
/// from the active part or a saved part file. Reference/boot geometry
/// (default planes, origin, ...) is refused at the tool layer.
/// </summary>
public sealed record DeleteFeatureSpec
{
    /// <summary>Exact feature name from inspect_* (e.g. "凸台-拉伸2").</summary>
    public required string FeatureName { get; init; }

    /// <summary>Optional absolute .sldprt — FILE mode. Null/empty = active part.</summary>
    public string? PartPath { get; init; }

    /// <summary>Optional output .sldprt (FILE mode only). Null/empty = in place.</summary>
    public string? OutputPath { get; init; }

    public void Validate()
    {
        FeatureManageValidation.ValidateFeatureName(FeatureName);
        FeatureManageValidation.ValidatePaths(PartPath, OutputPath);
    }
}

/// <summary>
/// Suppress (or unsuppress) a feature on the active part or a saved part
/// file. Suppression is the reversible sibling of delete — geometry drops
/// out of the rebuild but the feature stays in the tree (inspect_* shows
/// suppressed=true) and can be restored.
/// </summary>
public sealed record SuppressFeatureSpec
{
    /// <summary>Exact feature name from inspect_* (e.g. "凸台-拉伸2").</summary>
    public required string FeatureName { get; init; }

    /// <summary>True (default) = suppress; false = unsuppress (restore).</summary>
    public bool Suppress { get; init; } = true;

    /// <summary>Optional absolute .sldprt — FILE mode. Null/empty = active part.</summary>
    public string? PartPath { get; init; }

    /// <summary>Optional output .sldprt (FILE mode only). Null/empty = in place.</summary>
    public string? OutputPath { get; init; }

    public void Validate()
    {
        FeatureManageValidation.ValidateFeatureName(FeatureName);
        FeatureManageValidation.ValidatePaths(PartPath, OutputPath);
    }
}
