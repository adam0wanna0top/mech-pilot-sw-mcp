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
/// Suppresses / unsuppresses a named feature — the REVERSIBLE sibling of
/// <see cref="DeleteFeatureTool"/> (M48). Suppressed geometry drops out of
/// the rebuild but stays in the tree (inspect_* features list shows
/// suppressed=true), so "what does it look like without that fillet?" is a
/// suppress → inspect → unsuppress round trip.
///
/// Mechanics (reflection-verified): <c>IFeature.SetSuppression2(state,
/// swThisConfiguration, null)</c> with swSuppressFeature=0 /
/// swUnSuppressFeature=1, then rebuild. No selection dance needed — the
/// call goes straight to the feature object.
/// </summary>
[McpServerToolType]
public static class SuppressFeatureTool
{
    [McpServerTool(Name = "suppress_feature")]
    [Description(
        "Suppress (default) or unsuppress a feature by exact name (from " +
        "inspect_active / inspect_part features list). Suppression removes " +
        "the feature's geometry from the rebuilt part but keeps it in the " +
        "tree (inspect shows suppressed=true) — a reversible 'what if it " +
        "wasn't there?' / rollback step; pass suppress=false to restore. By " +
        "default acts on the ACTIVE part (no save); pass partPath (absolute " +
        ".sldprt) to edit a saved part file instead (saved in place, or to " +
        "outputPath). Reference geometry is refused. For permanent removal " +
        "use delete_feature.")]
    public static ToolResult Run(
        [Description("Exact feature name from inspect_* (e.g. '凸台-拉伸2').")]
        string featureName,
        [Description("True (default) = suppress; false = unsuppress (restore the feature).")]
        bool suppress = true,
        [Description("Optional absolute .sldprt to edit a SAVED part file instead of the active part.")]
        string? partPath = null,
        [Description("Optional output .sldprt (only with partPath). Empty = overwrite in place.")]
        string? outputPath = null)
    {
        return RunWithSpec(new SuppressFeatureSpec
        {
            FeatureName = featureName,
            Suppress = suppress,
            PartPath = partPath,
            OutputPath = outputPath,
        });
    }

    public static ToolResult RunWithSpec(SuppressFeatureSpec spec)
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
                $"suppress_feature failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("suppress_feature requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunActive(SuppressFeatureSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var verb = ApplySuppression(model, spec);
        return ToolResult.Ok(
            message: $"{verb} feature '{spec.FeatureName}' on the active part",
            path: null);
    }

    private static ToolResult RunFile(SuppressFeatureSpec spec)
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
            var verb = ApplySuppression(model, spec);
            var targetPath = DeleteFeatureTool.SaveActiveModel(model, spec.PartPath!, spec.OutputPath);
            return ToolResult.Ok(
                message: $"{verb} feature '{spec.FeatureName}' in '{Path.GetFileName(targetPath)}'; saved",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Shared core: find by exact name (suppressed features are still in the
    /// tree, so unsuppress finds its target), refuse boot geometry, set the
    /// suppression state in this configuration, rebuild. Returns the verb
    /// for the result message.
    /// </summary>
    private static string ApplySuppression(IModelDoc2 model, SuppressFeatureSpec spec)
    {
        var feature = Internal.FeatureLookup.RequireFeatureByName(model, spec.FeatureName);
        Internal.FeatureLookup.RejectBootFeature(feature, spec.Suppress ? "suppress" : "unsuppress");

        var state = spec.Suppress
            ? (int)swFeatureSuppressionAction_e.swSuppressFeature
            : (int)swFeatureSuppressionAction_e.swUnSuppressFeature;
        if (!feature.SetSuppression2(
                state, (int)swInConfigurationOpts_e.swThisConfiguration, null))
        {
            throw new McpToolException(
                $"SetSuppression2 failed for '{spec.FeatureName}'. The feature may not " +
                "support suppression, or its state is locked by a parent feature.");
        }

        model.EditRebuild3();
        return spec.Suppress ? "Suppressed" : "Unsuppressed";
    }
#endif
}
