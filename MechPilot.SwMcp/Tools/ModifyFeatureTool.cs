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
/// Both share the same NoPIA-safe edit (M38): set a named dimension's
/// <c>SystemValue</c> + EditRebuild3 (no GetDefinition/ModifyDefinition). The
/// target is "D1@&lt;feature&gt;" for a bare feature name, or ANY full dimension
/// name from inspect_* editableDimensions (M45) — any feature type, not just the
/// primary; its unit (mm / degrees) is taken from the dimension's own type via
/// the M39 display-dimension reader. SystemValue is SI (metres / radians).
/// </summary>
[McpServerToolType]
public static class ModifyFeatureTool
{
    [McpServerTool(Name = "modify_feature")]
    [Description(
        "Edit an existing dimension and regenerate. featureName is EITHER a bare " +
        "feature name (edits that feature's primary dimension 'D1@<feature>') OR a " +
        "full dimension name from inspect_* editableDimensions (e.g. 'D1@凸台-拉伸1', " +
        "'D2@草图1') — so ANY surfaced dimension is editable, not just the primary, " +
        "and any feature type. value is the new value in the dimension's own unit: " +
        "mm for a length, degrees for an angle (auto-detected from the dimension). " +
        "By default edits the ACTIVE part; pass partPath (an absolute .sldprt) to " +
        "edit a SAVED part file instead — e.g. an assembly component during a " +
        "resize — saved in place (or to outputPath). Use inspect_active / " +
        "inspect_assembly to get the names first, then inspect again to confirm.")]
    public static ToolResult Run(
        [Description("A feature name (→ its 'D1@<feature>') or a full dimension name from editableDimensions (e.g. 'D1@凸台-拉伸1').")]
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
        // featureName may be a full dimension name (contains '@', as surfaced by
        // inspect_* editableDimensions, e.g. "D1@凸台-拉伸1" / "D2@草图1") or a bare
        // feature name (→ its primary dimension "D1@<feature>").
        var dimName = spec.FeatureName.Contains('@') ? spec.FeatureName : $"D1@{spec.FeatureName}";

        var found = FindDisplayDimension(model, dimName);
        if (found is null)
        {
            throw new McpToolException(
                $"No editable dimension '{dimName}' on the part. Call inspect_active / " +
                "inspect_part and use a name from a feature's editableDimensions " +
                "(e.g. 'D1@凸台-拉伸1'), or a bare feature name for its primary dimension.");
        }
        var (disp, dim) = found.Value;

        // Unit comes from the dimension's own type (angular → degrees, else mm), so
        // ANY dimension is editable — not just a feature's primary, any feature type
        // (reuses the M39 display-dimension reader). SystemValue is SI (m / rad).
        var isAngle = Internal.DimensionFormat.IsAngular(disp.Type2);
        dim.SystemValue = isAngle ? spec.Value * Math.PI / 180.0 : spec.Value / 1000.0;

        if (!model.EditRebuild3())
        {
            throw new McpToolException(
                $"Rebuild failed after setting '{dimName}' to {spec.Value}. The value may " +
                "be geometrically invalid (breaks this feature or a downstream feature).");
        }

        return isAngle ? $"{dimName} → {spec.Value}°" : $"{dimName} → {spec.Value} mm";
    }

    /// <summary>
    /// Finds the display dimension whose "{shortName}@{feature}" equals dimName,
    /// walking every feature's display dimensions — the inverse of the M39 reader,
    /// so anything inspect surfaces in editableDimensions is editable here.
    /// Returns the display dimension + its underlying IDimension, or null.
    /// </summary>
    private static (IDisplayDimension disp, IDimension dim)? FindDisplayDimension(
        IModelDoc2 model, string dimName)
    {
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            var dispObj = feature.GetFirstDisplayDimension();
            while (dispObj is IDisplayDimension disp)
            {
                if (disp.GetDimension2(0) is IDimension dim &&
                    string.Equals($"{dim.Name}@{feature.Name}", dimName, StringComparison.Ordinal))
                {
                    return (disp, dim);
                }
                dispObj = feature.GetNextDisplayDimension(dispObj);
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return null;
    }
#endif
}
