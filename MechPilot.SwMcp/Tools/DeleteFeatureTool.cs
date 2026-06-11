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
/// Deletes a named feature — the "mechanical Cursor" rollback primitive
/// (M48). Born from the fan dogfooding pain: a mistaken extrude could not be
/// removed, forcing full part rebuilds.
///
/// Mechanics (reflection-verified): select the feature
/// (<c>IFeature.Select2</c>) → <c>IModelDocExtension.DeleteSelection2</c>
/// with <c>swDelete_Children | swDelete_Absorbed</c> so the absorbed sketch
/// and dependent children go silently with it (no SW dialog) → rebuild.
///
/// Two modes mirroring modify_feature (M38/M44): ACTIVE doc (default, no
/// save) or FILE mode via partPath (open → delete → save → close).
/// Reference/boot geometry (default planes, origin, ref planes) is refused.
/// </summary>
[McpServerToolType]
public static class DeleteFeatureTool
{
    [McpServerTool(Name = "delete_feature")]
    [Description(
        "Delete a feature by exact name (from inspect_active / inspect_part " +
        "features list), cascading to its absorbed sketch and dependent " +
        "children — the undo/rollback primitive: built the wrong boss? delete " +
        "it and continue, no full rebuild. By default acts on the ACTIVE part " +
        "(no save); pass partPath (absolute .sldprt) to edit a saved part " +
        "file instead (saved in place, or to outputPath). Reference geometry " +
        "(default planes, origin, ref planes) is refused. Deletion is " +
        "permanent — for a reversible variant use suppress_feature. Inspect " +
        "again afterwards to see the resulting tree/geometry.")]
    public static ToolResult Run(
        [Description("Exact feature name from inspect_* (e.g. '凸台-拉伸2').")]
        string featureName,
        [Description("Optional absolute .sldprt to edit a SAVED part file instead of the active part.")]
        string? partPath = null,
        [Description("Optional output .sldprt (only with partPath). Empty = overwrite in place.")]
        string? outputPath = null)
    {
        return RunWithSpec(new DeleteFeatureSpec
        {
            FeatureName = featureName,
            PartPath = partPath,
            OutputPath = outputPath,
        });
    }

    public static ToolResult RunWithSpec(DeleteFeatureSpec spec)
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
                $"delete_feature failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("delete_feature requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunActive(DeleteFeatureSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        ApplyDelete(model, spec.FeatureName);
        return ToolResult.Ok(
            message: $"Deleted feature '{spec.FeatureName}' (with absorbed/child features) on the active part",
            path: null);
    }

    private static ToolResult RunFile(DeleteFeatureSpec spec)
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
            ApplyDelete(model, spec.FeatureName);
            var targetPath = SaveActiveModel(model, spec.PartPath!, spec.OutputPath);
            return ToolResult.Ok(
                message: $"Deleted feature '{spec.FeatureName}' in '{Path.GetFileName(targetPath)}'; saved",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Shared core: find by exact name, refuse boot/reference geometry,
    /// select (mark 0), DeleteSelection2 with Children|Absorbed (silent
    /// cascade), rebuild.
    /// </summary>
    private static void ApplyDelete(IModelDoc2 model, string featureName)
    {
        var feature = Internal.FeatureLookup.RequireFeatureByName(model, featureName);
        Internal.FeatureLookup.RejectBootFeature(feature, "delete");

        model.ClearSelection2(true);
        if (!feature.Select2(false, 0))
        {
            throw new McpToolException(
                $"Could not select feature '{featureName}' for deletion (Select2 failed).");
        }

        var options = (int)(swDeleteSelectionOptions_e.swDelete_Children
                          | swDeleteSelectionOptions_e.swDelete_Absorbed);
        if (!model.Extension.DeleteSelection2(options))
        {
            throw new McpToolException(
                $"DeleteSelection2 failed for '{featureName}'. The feature may be " +
                "consumed by a downstream feature SW refuses to cascade into — " +
                "inspect the tree and delete the downstream feature first.");
        }

        model.ClearSelection2(true);
        model.EditRebuild3();
    }

    /// <summary>Save in place (Save3, M5 lesson) or as a copy (SaveAs).</summary>
    internal static string SaveActiveModel(IModelDoc2 model, string partPath, string? outputPath)
    {
        var targetPath = string.IsNullOrWhiteSpace(outputPath) ? partPath : outputPath!;
        var isInPlace = string.Equals(targetPath, partPath, StringComparison.OrdinalIgnoreCase);
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
        return targetPath;
    }
#endif
}
