using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Interop;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Edits an existing feature's primary dimension and regenerates — the
/// "mechanical Cursor" edit primitive. Two modes:
///   • ACTIVE-doc (M38): edit the live part the generic layer is building (no
///     save — save_part later). Pairs with inspect_active.
///   • FILE (M44): open a .sldprt, edit, rebuild, SAVE (in place / to OutputPath),
///     close — so an assembly's component parts can be resized in place (the
///     part-side counterpart of modify_mate; driven by the inspect_assembly
///     component sourcePath + editableDimensions handle).
///
/// Both share the same NoPIA-safe edit: set the named dimension
/// "D1@&lt;featureName&gt;" via <c>IModelDoc2.Parameter(...).SystemValue</c> +
/// EditRebuild3 (no GetDefinition/ModifyDefinition; M38 finding). "D1" is the
/// primary dimension: extrude / cut → depth (mm); revolve / revolve-cut → angle
/// (deg). SystemValue is SI (metres / radians).
/// </summary>
[McpServerToolType]
public static class ModifyFeatureTool
{
    [McpServerTool(Name = "modify_feature")]
    [Description(
        "Edit an existing feature's primary dimension and regenerate. featureName " +
        "is the EXACT name from inspect_active / inspect_part / inspect_assembly's " +
        "editableDimensions (e.g. '凸台-拉伸2'). value is the new dimension: extrude " +
        "/ cut → depth in mm; revolve / revolve-cut → angle in degrees. By default " +
        "edits the ACTIVE part (the one being built). To edit a SAVED part file " +
        "instead — e.g. an assembly component during a resize — pass partPath (an " +
        "absolute .sldprt); it is opened, edited, saved (in place, or to " +
        "outputPath) and closed. Use inspect_active / inspect_assembly to get the " +
        "names first, then inspect again to confirm.")]
    public static ToolResult Run(
        [Description("Exact feature name (e.g. '凸台-拉伸2').")]
        string featureName,
        [Description("New primary dimension: depth in mm (extrude/cut) or angle in degrees (revolve). > 0.")]
        double value,
        [Description("Optional absolute .sldprt to edit a SAVED part file instead of the active part.")]
        string? partPath = null,
        [Description("Optional output .sldprt (only with partPath). Empty = overwrite in place.")]
        string? outputPath = null)
    {
        return RunWithSpec(new ModifyFeatureSpec
        {
            FeatureName = featureName,
            Value = value,
            PartPath = partPath,
            OutputPath = outputPath,
        });
    }

    public static ToolResult RunWithSpec(ModifyFeatureSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try
        {
            return string.IsNullOrWhiteSpace(spec.PartPath) ? RunActive(spec) : RunFile(spec);
        }
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
    // ACTIVE-doc mode (M38): edit the live part, no save.
    private static ToolResult RunActive(ModifyFeatureSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var what = ApplyModification(model, spec);
        return ToolResult.Ok(message: $"Modified '{spec.FeatureName}': {what} (active part)", path: null);
    }

    // FILE mode (M44): open a saved part, edit, save (Save3 in-place / SaveAs copy), close.
    private static ToolResult RunFile(ModifyFeatureSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();
        int openErrors = 0;
        int openWarnings = 0;
        var model = swApp.OpenDoc6(
            FileName: spec.PartPath!,
            Type: (int)swDocumentTypes_e.swDocPART,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{spec.PartPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var what = ApplyModification(model, spec);

            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath) ? spec.PartPath! : spec.OutputPath!;
            var isInPlace = string.Equals(targetPath, spec.PartPath, StringComparison.OrdinalIgnoreCase);
            int saveErrors = 0;
            int saveWarnings = 0;
            bool savedOk = isInPlace
                ? model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings)
                : model.Extension.SaveAs(targetPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref saveErrors, ref saveWarnings);

            if (!savedOk || !File.Exists(targetPath))
            {
                var api = isInPlace ? "Save3" : "SaveAs";
                throw new McpToolException(
                    $"{api} failed for '{targetPath}'. errors=0x{saveErrors:X} warnings=0x{saveWarnings:X}.");
            }

            return ToolResult.Ok(
                message: $"Modified '{spec.FeatureName}' in '{Path.GetFileName(targetPath)}': {what}; " +
                         $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Shared edit: find the feature, set its primary dimension "D1@&lt;name&gt;"
    /// (SI), EditRebuild3. Returns a human "what changed" string. Throws on an
    /// unknown feature, unsupported type, or failed rebuild.
    /// </summary>
    private static string ApplyModification(IModelDoc2 model, ModifyFeatureSpec spec)
    {
        var feature = FindFeatureByName(model, spec.FeatureName)
            ?? throw new McpToolException(
                $"Cannot find a feature named '{spec.FeatureName}'. " +
                "Call inspect_active / inspect_part to list the feature names.");

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

        return isAngle ? $"angle → {spec.Value}°" : $"depth → {spec.Value} mm";
    }

    /// <summary>
    /// Returns the first feature whose Name matches exactly, walking
    /// FirstFeature → GetNextFeature. Names come from inspect_active / inspect_part.
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
