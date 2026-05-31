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
/// Creates a parametric flange / end-cap / bolt-circle plate part. The geometry:
/// circular disk (D_outer × thickness), optional concentric center hole,
/// optional N bolt holes evenly distributed on a pitch circle.
///
/// Implementation strategy (per docs/v1-history.md §8.3 PR #35):
/// **one sketch + one through-all cut**. Avoids FeatureCircularPattern entirely,
/// which silently fails on multi-cut bodies in SW 2026 even with 12-stage probes
/// and recorded macros — a SW limitation, not a mech-pilot bug.
///
/// Pipeline:
///   1. Boss-extrude the outer disk on the Front Plane.
///   2. Select the +Z planar end face of the disk.
///   3. Open a new sketch on that face; draw the center hole (if any) and
///      all bolt holes (computed via cos/sin from PCD) in this single sketch.
///   4. Through-all cut with FeatureCut4. All holes punched in one operation.
///   5. Save .sldprt, close the document.
/// </summary>
[McpServerToolType]
public static class CreateFlangeTool
{
    [McpServerTool(Name = "create_flange")]
    [Description(
        "Create a parametric flange / end-cap / bolt-circle plate in SolidWorks and " +
        "save it to disk. A circular disk of the requested outer diameter and thickness, " +
        "with an optional concentric center hole and optional N bolt holes evenly " +
        "distributed around a pitch circle (PCD). All lengths in millimeters. " +
        "savePath must be an absolute path ending in .sldprt; parent directory must exist. " +
        "Set centerHoleDiameter=0 for a solid disk. Set boltCount=0 for no bolt holes.")]
    public static ToolResult Run(
        [Description("Outer disk diameter in mm, e.g. 80.")]
        double outerDiameter,
        [Description("Disk thickness (extrusion depth) in mm, e.g. 10.")]
        double thickness,
        [Description("Absolute output .sldprt path, e.g. C:/tmp/flange.sldprt.")]
        string savePath,
        [Description("Concentric center hole diameter in mm; 0 for none. Default 0.")]
        double centerHoleDiameter = 0,
        [Description("Number of bolt holes evenly distributed on the PCD; 0 for none. Default 0.")]
        int boltCount = 0,
        [Description("Diameter of each bolt clearance hole in mm. Required if boltCount > 0.")]
        double boltDiameter = 0,
        [Description("Pitch circle diameter (PCD) for bolt holes in mm. Required if boltCount > 0.")]
        double boltCircleDiameter = 0)
    {
        var spec = new FlangeSpec
        {
            OuterDiameterMm = outerDiameter,
            ThicknessMm = thickness,
            CenterHoleDiameterMm = centerHoleDiameter,
            BoltCount = boltCount,
            BoltDiameterMm = boltDiameter,
            BoltCircleDiameterMm = boltCircleDiameter,
            SavePath = savePath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(FlangeSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CreateFlangeInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"create_flange failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "create_flange requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static readonly string[] FrontPlaneAliases = { "前视基准面", "Front Plane" };
    private static readonly string[] Sketch1Aliases = { "草图1", "Sketch1" };

    private static ToolResult CreateFlangeInSw(FlangeSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Locate the default part template ─────────────────────────────
        var template = swApp.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            throw new McpToolException(
                $"Default part template not found (resolved to '{template}'). " +
                "Open SW once and set Tools → Options → Default Templates → Part.");
        }

        // ── 2. New part document ────────────────────────────────────────────
        var model = swApp.NewDocument(template, 0, 0.0, 0.0) as IModelDoc2
            ?? throw new McpToolException(
                $"swApp.NewDocument returned null for template '{template}'.");

        var ext = model.Extension;
        var skMgr = model.SketchManager;
        var fm = model.FeatureManager;

        var outerRadiusM = spec.OuterDiameterMm / 2000.0;
        var thicknessM = spec.ThicknessMm / 1000.0;

        // ── 3-5. Boss-extrude the outer disk on the Front Plane ────────────
        if (!SelectFirstMatch(ext, FrontPlaneAliases, "PLANE", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select Front Plane. Tried: {string.Join(" / ", FrontPlaneAliases)}.");
        }
        skMgr.InsertSketch(true);
        _ = skMgr.CreateCircleByRadius(0.0, 0.0, 0.0, outerRadiusM)
            ?? throw new McpToolException($"Failed to draw outer circle (r={outerRadiusM} m).");
        skMgr.InsertSketch(true); // exit sketch

        model.ClearSelection2(true);
        if (!SelectFirstMatch(ext, Sketch1Aliases, "SKETCH", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select Sketch1 after creation. Tried: {string.Join(" / ", Sketch1Aliases)}.");
        }
        var diskFeature = fm.FeatureExtrusion3(
            Sd: true, Flip: false, Dir: false,
            T1: (int)swEndConditions_e.swEndCondBlind, T2: (int)swEndConditions_e.swEndCondBlind,
            D1: thicknessM, D2: 0.0,
            Dchk1: false, Dchk2: false,
            Ddir1: false, Ddir2: false,
            Dang1: 0.0, Dang2: 0.0,
            OffsetReverse1: false, OffsetReverse2: false,
            TranslateSurface1: false, TranslateSurface2: false,
            Merge: true,
            UseFeatScope: true, UseAutoSelect: true,
            T0: (int)swStartConditions_e.swStartSketchPlane,
            StartOffset: 0.0, FlipStartOffset: false);

        if (diskFeature == null)
        {
            throw new McpToolException("Boss extrude of outer disk returned null.");
        }

        // ── 6. Early-out if no holes were requested ─────────────────────────
        var hasCenterHole = spec.CenterHoleDiameterMm > 0;
        var hasBoltHoles = spec.BoltCount > 0;
        if (!hasCenterHole && !hasBoltHoles)
        {
            return SaveAndClose(swApp, model, ext, spec,
                $"Created solid flange D{spec.OuterDiameterMm} mm × t{spec.ThicknessMm} mm (no holes)");
        }

        // ── 7. Select a planar end face of the disk for the holes sketch ──
        //   Shared helper (Tools/Internal/PartGeometryHelpers) does the body →
        //   faces → ±Z-normal walk. More reliable than coordinate SelectByID2
        //   (golden rule #6 — coord ray-cast is flaky in API mode).
        model.ClearSelection2(true);
        var endFace = Internal.PartGeometryHelpers.FindPlanarEndFace(model)
            ?? throw new McpToolException(
                "Could not find a planar end face on the disk body after boss-extrude. " +
                "Body may have unexpected topology.");
        if (!((IEntity)endFace).Select4(false, null))
        {
            throw new McpToolException("Face.Select4 failed on the planar end face.");
        }

        // ── 8. Sketch all holes on the picked face in one go ───────────────
        skMgr.InsertSketch(true);

        if (hasCenterHole)
        {
            var centerRadiusM = spec.CenterHoleDiameterMm / 2000.0;
            _ = skMgr.CreateCircleByRadius(0.0, 0.0, 0.0, centerRadiusM)
                ?? throw new McpToolException(
                    $"Failed to draw center hole (r={centerRadiusM} m).");
        }

        if (hasBoltHoles)
        {
            var pcdRadiusM = spec.BoltCircleDiameterMm / 2000.0;
            var boltRadiusM = spec.BoltDiameterMm / 2000.0;
            for (int i = 0; i < spec.BoltCount; i++)
            {
                var angle = 2.0 * Math.PI * i / spec.BoltCount;
                var bx = pcdRadiusM * Math.Cos(angle);
                var by = pcdRadiusM * Math.Sin(angle);
                _ = skMgr.CreateCircleByRadius(bx, by, 0.0, boltRadiusM)
                    ?? throw new McpToolException(
                        $"Failed to draw bolt circle #{i} at ({bx:F4}, {by:F4}) m.");
            }
        }

        skMgr.InsertSketch(true); // exit sketch — sketch is implicitly the current selection after this

        // ── 9. Through-all cut.
        //
        //   We use FeatureCut2 (23 args) not FeatureCut4 (27 args). Experimentally,
        //   FeatureCut4 returned null in SW 2026 on every variant we tried
        //   (Flip true/false, with/without explicit Sketch2 re-select, with/without
        //   NormalCut, with/without intermediate ClearSelection2). FeatureCut2 with
        //   NormalCut=false and the AssemblyFeatureScope/AutoSelectComponents/
        //   PropagateFeatureToParts trio all false works on the first try.
        //
        //   Hypothesis: FeatureCut4 (and possibly FeatureCut3) has stricter
        //   selection-state preconditions for the OptimizeGeometry / start-condition
        //   parameters that don't apply to face-based hole sketches in part docs.
        //   FeatureCut2's smaller surface area sidesteps those.
        //
        //   We also do NOT ClearSelection / re-SelectByID2 the sketch — after
        //   InsertSketch(exit), the sketch is already the implicit current selection
        //   (verified: GetSelectedObjectCount2=1, GetSelectedObjectType3=9=swSelSKETCHES).
        var cutFeature = fm.FeatureCut2(
            Sd: true, Flip: false, Dir: false,
            T1: (int)swEndConditions_e.swEndCondThroughAll,
            T2: (int)swEndConditions_e.swEndCondBlind,
            D1: 0.0, D2: 0.0,
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
            throw new McpToolException(
                "FeatureCut4 returned null. The hole sketch may be empty, overlapping with " +
                "the disk boundary, or otherwise invalid. (See FeatureManager log in SW UI.)");
        }

        // ── 10. Save + close ────────────────────────────────────────────────
        var holeSummary = (hasCenterHole, hasBoltHoles) switch
        {
            (true, true) => $"center D{spec.CenterHoleDiameterMm} + {spec.BoltCount}×D{spec.BoltDiameterMm} on PCD{spec.BoltCircleDiameterMm}",
            (true, false) => $"center D{spec.CenterHoleDiameterMm}",
            (false, true) => $"{spec.BoltCount}×D{spec.BoltDiameterMm} on PCD{spec.BoltCircleDiameterMm}",
            _ => "no holes",
        };
        return SaveAndClose(swApp, model, ext, spec,
            $"Created flange D{spec.OuterDiameterMm} × t{spec.ThicknessMm} mm; {holeSummary}");
    }

    private static ToolResult SaveAndClose(
        ISldWorks swApp, IModelDoc2 model, IModelDocExtension ext, FlangeSpec spec, string message)
    {
        int saveErrors = 0;
        int saveWarnings = 0;
        var savedOk = ext.SaveAs(
            Name: spec.SavePath,
            Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
            Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            ExportData: null,
            Errors: ref saveErrors,
            Warnings: ref saveWarnings);

        if (!savedOk || !File.Exists(spec.SavePath))
        {
            throw new McpToolException(
                $"SaveAs failed for '{spec.SavePath}'. errors=0x{saveErrors:X} " +
                $"warnings=0x{saveWarnings:X}. (See swFileSaveError_e in swconst.chm.)");
        }

        swApp.CloseDoc(model.GetTitle());
        return ToolResult.Ok(message: message, path: spec.SavePath);
    }

    private static bool SelectFirstMatch(
        IModelDocExtension ext,
        IReadOnlyList<string> aliases,
        string swSelectionType,
        int mark)
    {
        foreach (var alias in aliases)
        {
            if (ext.SelectByID2(
                Name: alias,
                Type: swSelectionType,
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: false,
                Mark: mark,
                Callout: null,
                SelectOption: 0))
            {
                return true;
            }
        }
        return false;
    }
#endif
}
