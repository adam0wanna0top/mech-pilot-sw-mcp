using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for editing an existing feature's primary dimension on the
/// ACTIVE part (M38) — the "mechanical Cursor" edit primitive: build → inspect
/// → tweak a dimension → regenerate, all on the live doc.
///
/// <see cref="Value"/> is applied to the feature's natural primary dimension,
/// which depends on the feature type:
///   • extrude / cut  → blind depth in mm
///   • revolve / revolve-cut → angle in degrees
///
/// LLM workflow:
///   ... build ... → inspect_active (read feature names) →
///   modify_feature("凸台-拉伸2", 25) → inspect_active (see the change)
/// </summary>
public sealed record ModifyFeatureSpec
{
    /// <summary>
    /// Exact feature name to edit, as reported by inspect_active / inspect_part
    /// (e.g. "凸台-拉伸1" / "旋转1" / "圆角1").
    /// </summary>
    public required string FeatureName { get; init; }

    /// <summary>
    /// New value for the feature's primary dimension — mm for extrude/cut depth,
    /// degrees for revolve angle. Must be a finite number &gt; 0.
    /// </summary>
    public required double Value { get; init; }

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
    }
}
