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
/// Adds a constant-radius fillet to every edge of an existing part.
///
/// This is the project's first "edit an existing part" tool: unlike
/// create_cylinder / create_flange (which build from NewDocument), it opens an
/// existing .sldprt with OpenDoc6, fillets, and saves.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt (silent).
///   2. Navigate body → edges; select every edge with mark=1 (the mark
///      FeatureFillet3 requires — docs/SW_API_REFERENCE.md §4).
///   3. FeatureFillet3 with uniform radius (Options=UniformRadius, Ftyp=Simple).
///   4. SaveAs the target path (defaults to overwriting the input in place).
///   5. CloseDoc (in finally, so an opened doc is never left dangling in SW).
///
/// Edge selection uses body navigation (GetBodies2 → GetEdges → Select2), not
/// coordinate-based SelectByID2, which is unreliable in API mode without an
/// active view (docs/v1-history.md M3 lesson, golden rule #6).
/// </summary>
[McpServerToolType]
public static class AddFilletTool
{
    [McpServerTool(Name = "add_fillet")]
    [Description(
        "Add a constant-radius fillet (rounded edge) to EVERY edge of an existing " +
        "SolidWorks part, then save it. Opens an existing .sldprt, rounds all edges " +
        "to the given radius, and writes the result. radius is in millimeters. " +
        "inputPath must be an absolute path to an existing .sldprt. " +
        "outputPath is optional: leave it empty to overwrite the input file in " +
        "place, or give an absolute .sldprt path to save the filleted part as a copy.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to fillet, e.g. C:/tmp/part.sldprt.")]
        string inputPath,
        [Description("Constant fillet radius in mm applied to every edge, e.g. 2.")]
        double radius,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new FilletSpec
        {
            InputPath = inputPath,
            RadiusMm = radius,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(FilletSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddFilletInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_fillet failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_fillet requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult AddFilletInSw(FilletSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Open the existing part ───────────────────────────────────────
        int openErrors = 0;
        int openWarnings = 0;
        var model = swApp.OpenDoc6(
            FileName: spec.InputPath,
            Type: (int)swDocumentTypes_e.swDocPART,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{spec.InputPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}. " +
                "(See swFileLoadError_e in swconst.chm.)");
        }

        try
        {
            var ext = model.Extension;
            var fm = model.FeatureManager;
            var radiusM = spec.RadiusMm / 1000.0;

            // ── 2. Select every edge of every solid body with mark=1 ────────
            model.ClearSelection2(true);
            var edgeCount = SelectAllEdges(model);
            if (edgeCount == 0)
            {
                throw new McpToolException(
                    "No edges found on any solid body in the part — nothing to fillet.");
            }

            // ── 3. Constant-radius fillet on all selected edges ─────────────
            //   Options = UniformRadius (2): every selected edge shares R1.
            //   Ftyp = Simple (0): constant radius (not variable / face / full-round).
            //
            //   The 7 trailing array params are passed as null. v1 (Python) found
            //   that passing None or an empty tuple made FeatureFillet3 return null,
            //   and only an explicit VT_EMPTY variant worked (SW_API_REFERENCE §1).
            //   In C# COM marshaling a null `object` arg becomes exactly that
            //   VT_EMPTY variant, so null here == v1's empty_variant().
            //   (If L2 ever shows this returning null, the first fallback to try is
            //   Array.Empty<double>() for each array param.)
            var filletFeature = fm.FeatureFillet3(
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

            if (filletFeature == null)
            {
                throw new McpToolException(
                    $"FeatureFillet3 returned null for radius {spec.RadiusMm} mm. " +
                    "The radius may be too large for the geometry (larger than an " +
                    "adjacent face), or some edges may already be tangent. " +
                    "Try a smaller radius.");
            }

            // ── 4. Save (in place, or to outputPath) ────────────────────────
            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? spec.InputPath
                : spec.OutputPath!;

            int saveErrors = 0;
            int saveWarnings = 0;
            var savedOk = ext.SaveAs(
                Name: targetPath,
                Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                ExportData: null,
                Errors: ref saveErrors,
                Warnings: ref saveWarnings);

            if (!savedOk || !File.Exists(targetPath))
            {
                throw new McpToolException(
                    $"SaveAs failed for '{targetPath}'. errors=0x{saveErrors:X} " +
                    $"warnings=0x{saveWarnings:X}.");
            }

            var where = string.Equals(targetPath, spec.InputPath, StringComparison.OrdinalIgnoreCase)
                ? "in place"
                : "as a copy";
            return ToolResult.Ok(
                message: $"Filleted {edgeCount} edge(s) with R{spec.RadiusMm} mm; saved {where}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Selects every edge of every solid body in the part with mark=1
    /// (FeatureFillet3's required mark — docs/SW_API_REFERENCE.md §4), appending
    /// each so the whole set is selected at once. Returns the edge count.
    /// Uses body navigation rather than coordinate SelectByID2, which is
    /// unreliable in API mode without an active view (M3 lesson).
    /// </summary>
    private static int SelectAllEdges(IModelDoc2 model)
    {
        var part = (IPartDoc)model;
        var bodiesObj = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies || bodies.Length == 0)
        {
            return 0;
        }

        var selected = 0;
        foreach (var bodyObj in bodies)
        {
            var body = (IBody2)bodyObj;
            if (body.GetEdges() is not object[] edges)
            {
                continue;
            }
            foreach (var edgeObj in edges)
            {
                // Select2(Append, Mark): append each edge, mark=1 for FeatureFillet3.
                if (((IEntity)edgeObj).Select2(true, 1))
                {
                    selected++;
                }
            }
        }
        return selected;
    }
#endif
}
