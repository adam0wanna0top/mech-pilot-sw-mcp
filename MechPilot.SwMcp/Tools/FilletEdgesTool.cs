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
/// Fillet SPECIFIC edges by their inspect_topology index (M52) — the first
/// topology-level edit primitive. add_fillet rounds EVERY edge (file-based,
/// all-or-nothing); this rounds exactly the edges the LLM picked off the
/// inspect_topology map ("round the two top edges, leave the base alone").
///
/// Mechanics: <see cref="Internal.EdgeSelector"/> re-enumerates edges in
/// TopologyReader's exact order, selects the requested indexes (mark=1) and
/// the same M4-verified <c>FeatureFillet3</c> call rounds the selection
/// (uniform radius, simple type, null array args = VT_EMPTY).
/// Two modes mirroring modify_feature: ACTIVE doc (no save) or partPath FILE
/// mode (open → fillet → save → close).
/// </summary>
[McpServerToolType]
public static class FilletEdgesTool
{
    [McpServerTool(Name = "fillet_edges")]
    [Description(
        "Round SPECIFIC edges with a constant-radius fillet — unlike " +
        "add_fillet (which rounds every edge of a file), this targets exactly " +
        "the edges you pick. edgeIndexes are index values from " +
        "inspect_topology (call it first, pick edges by their type/length/" +
        "position, and re-inspect after ANY edit — indexes refresh). radius " +
        "is mm. By default acts on the ACTIVE part (no save); pass partPath " +
        "(absolute .sldprt) to edit a saved file (saved in place or to " +
        "outputPath). The success message echoes each edge's signature " +
        "(type/length) — cross-check it against your inspect_topology data. " +
        "Inspect again afterwards to verify (face count rises; a new cylinder/" +
        "blend face appears per rounded edge).")]
    public static ToolResult Run(
        [Description("Edge indexes from inspect_topology, e.g. [4, 7].")]
        int[] edgeIndexes,
        [Description("Fillet radius in mm, e.g. 3.")]
        double radius,
        [Description("Optional absolute .sldprt to edit a SAVED part file instead of the active part.")]
        string? partPath = null,
        [Description("Optional output .sldprt (only with partPath). Empty = overwrite in place.")]
        string? outputPath = null)
    {
        return RunWithSpec(new FilletEdgesSpec
        {
            EdgeIndexes = edgeIndexes,
            RadiusMm = radius,
            PartPath = partPath,
            OutputPath = outputPath,
        });
    }

    public static ToolResult RunWithSpec(FilletEdgesSpec spec)
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
                $"fillet_edges failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("fillet_edges requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunActive(FilletEdgesSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var what = ApplyFillet(model, spec);
        return ToolResult.Ok(message: $"{what} (active part)", path: null);
    }

    private static ToolResult RunFile(FilletEdgesSpec spec)
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
            var what = ApplyFillet(model, spec);
            var targetPath = DeleteFeatureTool.SaveActiveModel(model, spec.PartPath!, spec.OutputPath);
            return ToolResult.Ok(
                message: $"{what} in '{Path.GetFileName(targetPath)}'; saved",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    private static string ApplyFillet(IModelDoc2 model, FilletEdgesSpec spec)
    {
        var signatures = Internal.EdgeSelector.SelectEdgesByIndex(model, spec.EdgeIndexes, mark: 1);

        // Same M4-verified FeatureFillet3 call as add_fillet — uniform radius,
        // simple fillet, null array args marshal as VT_EMPTY (SW_API_REFERENCE §1).
        var radiusM = spec.RadiusMm / 1000.0;
        var feature = model.FeatureManager.FeatureFillet3(
            Options: (int)swFeatureFilletOptions_e.swFeatureFilletUniformRadius,
            R1: radiusM,
            R2: 0.0,
            Rho: 0.0,
            Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_Simple,
            OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
            ConicRhoType: 0,
            Radii: null,
            Dist2Arr: null,
            RhoArr: null,
            SetBackDistances: null,
            PointRadiusArray: null,
            PointDist2Array: null,
            PointRhoArray: null);

        if (feature == null)
        {
            throw new McpToolException(
                $"FeatureFillet3 returned null for radius {spec.RadiusMm} mm on " +
                $"edges [{string.Join(", ", signatures)}]. The radius may be too " +
                "large for an adjacent face, or the edges may already be tangent.");
        }

        model.ClearSelection2(true);
        return $"Filleted {signatures.Count} edge(s) [{string.Join(", ", signatures)}] " +
               $"r={spec.RadiusMm} mm → feature '{feature.Name}'";
    }
#endif
}
