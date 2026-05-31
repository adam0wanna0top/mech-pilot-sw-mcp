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
/// Adds one axial (±Z) cylindrical hole to an existing part. Simpler than
/// SW's <c>HoleWizard5</c> — no fastener standard, no counterbore/countersink —
/// but covers the LLM-most-common "drill a Φ N hole" request. For PCD bolt
/// patterns or standard M-series threaded holes, see create_flange (one-shot
/// flange-with-bolts) or future add_hole_wizard.
///
/// Pipeline (combines patterns proven by create_flange + add_fillet):
///   1. OpenDoc6 the input .sldprt (silent).
///   2. Find a planar end face whose normal is along ±Z (body navigation,
///      same heuristic create_flange uses — more reliable than coordinate
///      SelectByID2 in API mode, golden rule #6).
///   3. InsertSketch on that face, draw one circle at (x, y, r), exit sketch.
///   4. FeatureCut2 with end-condition = ThroughAll (null depth) or Blind
///      (positive depth). Uses FeatureCut2 not FeatureCut4 — M3 lesson:
///      FeatureCut4 silently returns null on face-based hole sketches in
///      SW 2026; FeatureCut2 with NormalCut=false works first try.
///   5. Save: in-place → IModelDoc2.Save3; copy → Extension.SaveAs (M5 split).
///   6. CloseDoc (in finally).
/// </summary>
[McpServerToolType]
public static class AddAxialHoleTool
{
    [McpServerTool(Name = "add_axial_hole")]
    [Description(
        "Drill a single axial cylindrical hole (through-all or blind) into an " +
        "existing SolidWorks part, then save it. The hole is centered at " +
        "(positionX, positionY) on the part's ±Z end face. Pass depth=null or " +
        "omit it for a through-all hole; pass a positive depth in mm for a blind " +
        "hole that deep below the end face. inputPath must be an absolute path " +
        "to an existing .sldprt. outputPath is optional: leave it empty to " +
        "overwrite the input file in place, or give an absolute .sldprt path " +
        "to save the result as a copy. For LLM M-series mapping (e.g. M6 → " +
        "Φ6.6 clearance / Φ5 tap drill), the LLM should compute the diameter; " +
        "this tool is geometry-only.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to drill, e.g. C:/tmp/part.sldprt.")]
        string inputPath,
        [Description("Hole diameter in mm, e.g. 6.6 for an M6 clearance hole.")]
        double diameter,
        [Description("Blind depth in mm; omit or null for through-all.")]
        double? depth = null,
        [Description("Hole-center X on the end face in mm. Default 0 (centroid).")]
        double positionX = 0,
        [Description("Hole-center Y on the end face in mm. Default 0 (centroid).")]
        double positionY = 0,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new AxialHoleSpec
        {
            InputPath = inputPath,
            DiameterMm = diameter,
            DepthMm = depth,
            PositionXMm = positionX,
            PositionYMm = positionY,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(AxialHoleSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddAxialHoleInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_axial_hole failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_axial_hole requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult AddAxialHoleInSw(AxialHoleSpec spec)
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
            var skMgr = model.SketchManager;
            var fm = model.FeatureManager;

            // ── 2. Pick a planar ±Z end face (shared helper) ────────────────
            model.ClearSelection2(true);
            var endFace = Internal.PartGeometryHelpers.FindPlanarEndFace(model)
                ?? throw new McpToolException(
                    "Could not find a planar end face whose normal is along ±Z. " +
                    "add_axial_hole expects a part extruded from the Front Plane " +
                    "(create_cylinder / create_flange both qualify); arbitrary " +
                    "geometries need a future add_hole_on_face tool.");
            if (!((IEntity)endFace).Select4(false, null))
            {
                throw new McpToolException("Face.Select4 failed on the planar end face.");
            }

            // ── 3. Sketch the hole as a single circle on that face ──────────
            var radiusM = spec.DiameterMm / 2000.0;  // mm → m, /2 for radius
            var cxM = spec.PositionXMm / 1000.0;
            var cyM = spec.PositionYMm / 1000.0;

            skMgr.InsertSketch(true);
            _ = skMgr.CreateCircleByRadius(cxM, cyM, 0.0, radiusM)
                ?? throw new McpToolException(
                    $"Failed to draw hole circle at ({cxM:F4}, {cyM:F4}) m, r={radiusM:F4} m. " +
                    "Position may fall outside the face boundary.");
            skMgr.InsertSketch(true); // exit sketch — sketch is now the implicit selection

            // ── 4. Cut: ThroughAll for null depth, Blind for positive depth ──
            //   M3 lesson (SW_API_REFERENCE §8.3): FeatureCut4 silently returns
            //   null on face-based hole sketches in SW 2026; FeatureCut2 with
            //   NormalCut=false + AssemblyFeatureScope/AutoSelect trio all false
            //   works on the first try.
            var isThrough = !spec.DepthMm.HasValue;
            var depthM = isThrough ? 0.0 : spec.DepthMm!.Value / 1000.0;
            var endCond = isThrough
                ? swEndConditions_e.swEndCondThroughAll
                : swEndConditions_e.swEndCondBlind;

            var cutFeature = fm.FeatureCut2(
                Sd: true, Flip: false, Dir: false,
                T1: (int)endCond,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: depthM, D2: 0.0,
                Dchk1: false, Dchk2: false,
                Ddir1: false, Ddir2: false,
                Dang1: 0.0, Dang2: 0.0,
                OffsetReverse1: false, OffsetReverse2: false,
                TranslateSurface1: false, TranslateSurface2: false,
                NormalCut: false,
                UseFeatScope: true, UseAutoSelect: true,
                AssemblyFeatureScope: false,
                AutoSelectComponents: false,
                PropagateFeatureToParts: false);

            if (cutFeature == null)
            {
                var modeLabel = isThrough ? "through-all" : $"blind {spec.DepthMm} mm";
                throw new McpToolException(
                    $"FeatureCut2 returned null for Φ{spec.DiameterMm} mm {modeLabel} hole " +
                    $"at ({spec.PositionXMm}, {spec.PositionYMm}) mm. The hole may be larger " +
                    "than the part, overlap the boundary, or fall off the face.");
            }

            // ── 5. Save (in-place vs copy) — same split as M5 ───────────────
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

            var description = isThrough
                ? $"Φ{spec.DiameterMm} mm through hole"
                : $"Φ{spec.DiameterMm} mm × {spec.DepthMm} mm blind hole";
            var positionLabel = (spec.PositionXMm == 0 && spec.PositionYMm == 0)
                ? "at face centroid"
                : $"at ({spec.PositionXMm}, {spec.PositionYMm}) mm";
            return ToolResult.Ok(
                message: $"Drilled {description} {positionLabel}; saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

#endif
}
