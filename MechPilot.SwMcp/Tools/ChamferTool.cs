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
/// Adds an equal-distance chamfer to every edge of an existing part.
///
/// Sibling of <see cref="AddFilletTool"/> — same open → select all edges →
/// apply feature → save pipeline, but produces a chamfer (45° equal-width
/// cut) instead of a rounded edge. The save branch follows M5: in-place uses
/// <c>Save3</c>, copy uses <c>Extension.SaveAs</c>.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt (silent).
///   2. Navigate body → edges; select every edge with mark=1.
///   3. InsertFeatureChamfer with ChamferType=swChamferEqualDistance
///      (single-distance equal chamfer; Width carries the size, OtherDist
///      is ignored in this mode but passed equal as a safety belt).
///   4. Save: in-place → IModelDoc2.Save3; copy → Extension.SaveAs.
///   5. CloseDoc (in finally).
/// </summary>
[McpServerToolType]
public static class ChamferTool
{
    [McpServerTool(Name = "add_chamfer")]
    [Description(
        "Add an equal-distance chamfer (45° cut) to EVERY edge of an existing " +
        "SolidWorks part, then save it. Opens an existing .sldprt, chamfers all " +
        "edges with the given width, and writes the result. distance is in " +
        "millimeters. inputPath must be an absolute path to an existing .sldprt. " +
        "outputPath is optional: leave it empty to overwrite the input file in " +
        "place, or give an absolute .sldprt path to save the chamfered part as a copy.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to chamfer, e.g. C:/tmp/part.sldprt.")]
        string inputPath,
        [Description("Equal-distance chamfer width in mm applied to every edge, e.g. 2.")]
        double distance,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new ChamferSpec
        {
            InputPath = inputPath,
            DistanceMm = distance,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ChamferSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddChamferInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_chamfer failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_chamfer requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult AddChamferInSw(ChamferSpec spec)
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
            var distanceM = spec.DistanceMm / 1000.0;

            // ── 2. Select every edge of every solid body with mark=1 ────────
            model.ClearSelection2(true);
            var edgeCount = SelectAllEdges(model);
            if (edgeCount == 0)
            {
                throw new McpToolException(
                    "No edges found on any solid body in the part — nothing to chamfer.");
            }

            // ── 3. Equal-distance chamfer on all selected edges ─────────────
            //   ChamferType = swChamferEqualDistance (16): single-distance
            //   constant-width chamfer. Width carries the size; OtherDist is
            //   ignored in this mode but passed equal as a safety belt in case
            //   SW 2026 cross-validates the two.
            var chamferFeature = fm.InsertFeatureChamfer(
                Options: 0,
                ChamferType: (int)swChamferType_e.swChamferEqualDistance,
                Width: distanceM,
                Angle: 0.0,
                OtherDist: distanceM,
                VertexChamDist1: 0.0,
                VertexChamDist2: 0.0,
                VertexChamDist3: 0.0);

            if (chamferFeature == null)
            {
                throw new McpToolException(
                    $"InsertFeatureChamfer returned null for distance {spec.DistanceMm} mm. " +
                    "The width may be too large for the geometry (larger than an " +
                    "adjacent face), or some edges may already be tangent. " +
                    "Try a smaller distance.");
            }

            // ── 4. Save (in place, or to outputPath) — same split as M5 ─────
            //   in-place → IModelDoc2.Save3 (SW API designed for "overwrite the
            //   currently-active doc"; Extension.SaveAs(samepath) returns
            //   errors=0x1 under a long-lived SW instance — see DEV_LOG M5).
            //   copy     → Extension.SaveAs to a different path.
            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? spec.InputPath
                : spec.OutputPath!;
            var isInPlace = string.Equals(targetPath, spec.InputPath, StringComparison.OrdinalIgnoreCase);

            int saveErrors = 0;
            int saveWarnings = 0;
            bool savedOk;
            if (isInPlace)
            {
                savedOk = model.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref saveErrors,
                    ref saveWarnings);
            }
            else
            {
                savedOk = ext.SaveAs(
                    Name: targetPath,
                    Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ExportData: null,
                    Errors: ref saveErrors,
                    Warnings: ref saveWarnings);
            }

            if (!savedOk || !File.Exists(targetPath))
            {
                var api = isInPlace ? "Save3" : "SaveAs";
                throw new McpToolException(
                    $"{api} failed for '{targetPath}'. errors=0x{saveErrors:X} " +
                    $"warnings=0x{saveWarnings:X}.");
            }

            return ToolResult.Ok(
                message: $"Chamfered {edgeCount} edge(s) with D{spec.DistanceMm} mm; saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Selects every edge of every solid body in the part with mark=1
    /// (consistent with FilletTool — chamfer reuses the same selection mark).
    /// Uses body navigation rather than coordinate SelectByID2, which is
    /// unreliable in API mode without an active view (golden rule #6).
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
