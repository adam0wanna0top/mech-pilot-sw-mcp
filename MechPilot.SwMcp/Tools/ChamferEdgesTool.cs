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
/// Chamfer SPECIFIC edges by their inspect_topology index (M52) — sibling of
/// <see cref="FilletEdgesTool"/>, same selection contract, same two modes.
///
/// M52 root-cause finding (probe matrix): <c>swChamferEqualDistance</c> (16)
/// makes <c>InsertFeatureChamfer</c> build a DEGENERATE feature in SW 2026 —
/// it lands in the tree but changes zero geometry, no error anywhere. The
/// working call is <c>swChamferAngleDistance</c> (1) + Angle = π/4, which IS
/// the 45° equal-leg chamfer. (M6's add_chamfer shipped with type 16 and was
/// a silent geometric no-op since birth — its L2 never asserted face count;
/// fixed in the same PR.) A face-count delta guard turns any future
/// degenerate chamfer into a loud failure.
/// </summary>
[McpServerToolType]
public static class ChamferEdgesTool
{
    [McpServerTool(Name = "chamfer_edges")]
    [Description(
        "Chamfer SPECIFIC edges (equal-distance, 45°) — unlike add_chamfer " +
        "(which chamfers every edge of a file), this targets exactly the " +
        "edges you pick. edgeIndexes are index values from inspect_topology " +
        "(call it first; re-inspect after ANY edit — indexes refresh). " +
        "distance is the chamfer width in mm. By default acts on the ACTIVE " +
        "part (no save); pass partPath (absolute .sldprt) to edit a saved " +
        "file (saved in place or to outputPath). The success message echoes " +
        "each edge's signature (type/length) for cross-checking. Inspect " +
        "again afterwards to verify (a new flat face appears per chamfered edge).")]
    public static ToolResult Run(
        [Description("Edge indexes from inspect_topology, e.g. [4, 7].")]
        int[] edgeIndexes,
        [Description("Equal chamfer distance in mm, e.g. 2.")]
        double distance,
        [Description("Optional absolute .sldprt to edit a SAVED part file instead of the active part.")]
        string? partPath = null,
        [Description("Optional output .sldprt (only with partPath). Empty = overwrite in place.")]
        string? outputPath = null)
    {
        return RunWithSpec(new ChamferEdgesSpec
        {
            EdgeIndexes = edgeIndexes,
            DistanceMm = distance,
            PartPath = partPath,
            OutputPath = outputPath,
        });
    }

    public static ToolResult RunWithSpec(ChamferEdgesSpec spec)
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
                $"chamfer_edges failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("chamfer_edges requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunActive(ChamferEdgesSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var what = ApplyChamfer(model, spec);
        return ToolResult.Ok(message: $"{what} (active part)", path: null);
    }

    private static ToolResult RunFile(ChamferEdgesSpec spec)
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
            var what = ApplyChamfer(model, spec);
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

    private static string ApplyChamfer(IModelDoc2 model, ChamferEdgesSpec spec)
    {
        var signatures = Internal.EdgeSelector.SelectEdgesByIndex(model, spec.EdgeIndexes, mark: 1);

        // M52 finding: a Chamfer feature can land in the tree DEGENERATE
        // (zero geometry change) without any error — guard with a face-count
        // delta so a silent no-op becomes a loud failure.
        var facesBefore = CountFaces(model);

        // Classic angle-distance chamfer (45°): swChamferAngleDistance with
        // Width = distance, Angle = π/4 rad.
        var distanceM = spec.DistanceMm / 1000.0;
        var feature = model.FeatureManager.InsertFeatureChamfer(
            Options: 0,
            ChamferType: (int)swChamferType_e.swChamferAngleDistance,
            Width: distanceM,
            Angle: Math.PI / 4.0,
            OtherDist: 0.0,
            VertexChamDist1: 0.0,
            VertexChamDist2: 0.0,
            VertexChamDist3: 0.0);

        if (feature == null)
        {
            throw new McpToolException(
                $"InsertFeatureChamfer returned null for distance {spec.DistanceMm} mm " +
                $"on edges [{string.Join(", ", signatures)}].");
        }

        model.EditRebuild3();
        var facesAfter = CountFaces(model);
        if (facesAfter <= facesBefore)
        {
            throw new McpToolException(
                $"Chamfer feature '{feature.Name}' was created but changed no geometry " +
                $"(face count stayed {facesBefore}) — a degenerate chamfer. The distance " +
                $"({spec.DistanceMm} mm) may be too large for an adjacent face.");
        }

        model.ClearSelection2(true);
        return $"Chamfered {signatures.Count} edge(s) [{string.Join(", ", signatures)}] " +
               $"d={spec.DistanceMm} mm → feature '{feature.Name}'";
    }

    private static int CountFaces(IModelDoc2 model)
    {
        object bodiesObj = ((IPartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies)
        {
            return 0;
        }
        var n = 0;
        foreach (var bodyObj in bodies)
        {
            n += ((IBody2)bodyObj).GetFaceCount();
        }
        return n;
    }
#endif
}
