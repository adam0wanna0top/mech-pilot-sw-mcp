using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Edits an existing feature's primary dimension on the ACTIVE part and
/// regenerates — the "mechanical Cursor" edit primitive (M38). Pairs with
/// inspect_active (read the live model) to close the build → inspect → tweak →
/// regenerate loop without re-deriving the whole part.
///
/// Implementation: sets the feature's primary named dimension directly via
/// <c>IModelDoc2.Parameter("D1@&lt;featureName&gt;").SystemValue</c> + EditRebuild3.
/// This deliberately AVOIDS the GetDefinition → ModifyDefinition round-trip:
/// under <c>EmbedInteropTypes=true</c> (NoPIA), passing the feature-data COM
/// object back into ModifyDefinition's <c>object</c> parameter throws
/// "Could not convert argument 0" (M38 finding). Setting a named dimension is
/// both immune to that and a cleaner "change this number" edit.
///
/// "D1@&lt;feature&gt;" is the primary dimension for the supported feature types:
///   • extrude / cut (Extrusion / ICE)      → D1 = blind depth (length)
///   • revolve / revolve-cut (Revolution / RevCut) → D1 = angle
/// SystemValue is SI (metres / radians), so the caller's mm / degrees are
/// converted accordingly.
/// </summary>
[McpServerToolType]
public static class ModifyFeatureTool
{
    [McpServerTool(Name = "modify_feature")]
    [Description(
        "Edit an existing feature's primary dimension on the active part and " +
        "regenerate (the build → inspect → tweak loop). featureName is the EXACT " +
        "name from inspect_active / inspect_part (e.g. '凸台-拉伸2'). value is the " +
        "new dimension; its meaning depends on the feature type: extrude / cut → " +
        "depth in mm; revolve / revolve-cut → angle in degrees. Requires an active " +
        "part containing that feature. Use inspect_active first to get feature " +
        "names, then inspect_active again afterward to confirm the result.")]
    public static ToolResult Run(
        [Description("Exact feature name from inspect_active / inspect_part (e.g. '凸台-拉伸2').")]
        string featureName,
        [Description("New primary dimension: depth in mm (extrude/cut) or angle in degrees (revolve). > 0.")]
        double value)
    {
        return RunWithSpec(new ModifyFeatureSpec
        {
            FeatureName = featureName,
            Value = value,
        });
    }

    public static ToolResult RunWithSpec(ModifyFeatureSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"modify_feature failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("modify_feature requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(ModifyFeatureSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();

        var feature = FindFeatureByName(model, spec.FeatureName)
            ?? throw new McpToolException(
                $"Cannot find a feature named '{spec.FeatureName}' on the active part. " +
                "Call inspect_active to list the current feature names.");

        var typeName = feature.GetTypeName2() ?? string.Empty;
        var isAngle = typeName is "Revolution" or "RevCut";
        var isDepth = typeName is "Extrusion" or "ICE";
        if (!isAngle && !isDepth)
        {
            throw new McpToolException(
                $"modify_feature does not support feature '{spec.FeatureName}' " +
                $"(type '{typeName}') yet. Supported: extrude / cut (depth, mm) and " +
                "revolve / revolve-cut (angle, degrees).");
        }

        // The feature's primary dimension is "D1@<featureName>". SystemValue is
        // SI (metres for length, radians for angle), so convert from mm / degrees.
        var dimName = $"D1@{spec.FeatureName}";
        if (model.Parameter(dimName) is not IDimension dim)
        {
            throw new McpToolException(
                $"Could not access dimension '{dimName}'. The feature may not expose a " +
                "primary dimension by that name.");
        }
        dim.SystemValue = isAngle ? spec.Value * Math.PI / 180.0 : spec.Value / 1000.0;

        if (!model.EditRebuild3())
        {
            throw new McpToolException(
                $"Rebuild failed after modifying '{spec.FeatureName}' to {spec.Value}. " +
                "The value may be geometrically invalid (breaks this feature or a " +
                "downstream feature).");
        }

        var what = isAngle ? $"angle → {spec.Value}°" : $"depth → {spec.Value} mm";
        return ToolResult.Ok(
            message: $"Modified '{spec.FeatureName}': {what}",
            path: null);
    }

    /// <summary>
    /// Returns the first feature on the active part whose Name matches exactly,
    /// walking FirstFeature → GetNextFeature. Names come from inspect_active /
    /// inspect_part, so an exact (ordinal) match is what the LLM expects.
    /// </summary>
    private static IFeature? FindFeatureByName(IModelDoc2 model, string name)
    {
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            if (string.Equals(feature.Name, name, StringComparison.Ordinal))
            {
                return feature;
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return null;
    }
#endif
}
