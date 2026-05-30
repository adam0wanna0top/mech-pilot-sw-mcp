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
/// Adds one GB/T 196 metric-coarse threaded hole (tap) at the centroid of
/// the part's ±Z end face. Wraps SW's <c>HoleWizard5</c> (27 args) with the
/// GB-tap configuration broken by v1 PR #24.
///
/// **The 4 "magic" Value positions** (v1 found these by recording a macro;
/// without them HoleWizard5 silently returns null on the GB path):
///   • Value3 = π/1.8 ≈ 1.7453 — countersink angle default 100° (tap doesn't
///     have a countersink, but SW still requires the field)
///   • Value7 = Value8 = 1.0 — "feature enable" flags
///   • Value11 = Value12 = -1.0 — "use SW default" sentinels
///
/// Plus the 7 standard GB metric-coarse thread sizes (M3..M12) have their
/// tap drill diameter + pitch in <see cref="ThreadedHoleSpec.GbTapTable"/>
/// per GB/T 196-2003.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt.
///   2. Find planar ±Z end face, select it (mark=0).
///   3. Call HoleWizard5 with the 27-arg GB-tap recipe.
///   4. Save (isInPlace → Save3, copy → SaveAs).
///   5. CloseDoc (in finally).
/// </summary>
[McpServerToolType]
public static class AddThreadedHoleTool
{
    [McpServerTool(Name = "add_threaded_hole")]
    [Description(
        "Drill one GB/T 196 metric-coarse threaded hole (tap) at the centroid " +
        "of an existing part's ±Z end face. threadSize is one of " +
        "'M3', 'M4', 'M5', 'M6', 'M8', 'M10', 'M12' (case-insensitive). " +
        "Pass depth=null or omit it for a through-all tap; pass a positive " +
        "depth in mm for a blind tap that deep below the end face. " +
        "inputPath must be an absolute path to an existing .sldprt. " +
        "outputPath is optional: empty = overwrite the input in place. " +
        "Position is fixed to face centroid; for off-center / multi-hole, " +
        "combine with pattern_linear or use create_flange (PCD bolt circle).")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to drill, e.g. C:/tmp/part.sldprt.")]
        string inputPath,
        [Description("GB metric-coarse thread size: M3 / M4 / M5 / M6 / M8 / M10 / M12.")]
        string threadSize,
        [Description("Blind tap depth in mm; omit or null for through-all.")]
        double? depth = null,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new ThreadedHoleSpec
        {
            InputPath = inputPath,
            ThreadSize = threadSize,
            DepthMm = depth,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ThreadedHoleSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddThreadedHoleInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_threaded_hole failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_threaded_hole requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // Constants from v1 PR #24's recorded macro (CHM doesn't publish these).
    private const int SwWzdTap = 4;                         // GenericHoleType
    private const int SwStandardGB = 13;                    // StandardIndex
    private const int SwGbTapFastenerType = 359;            // FastenerTypeIndex (the magic 359)

    private const short EndCondThroughAll = 1;              // EndType (Int16 in this API)
    private const short EndCondBlind = 0;

    // The 4 "magic" Value positions — see class summary.
    private const double CountersinkAngleRad = 1.74532925199433;  // π/1.8 ≈ 100°
    private const double FeatureEnableFlag = 1.0;                  // Value7 / Value8
    private const double SwDefaultSentinel = -1.0;                 // Value11 / Value12 / Length
    private const double VbaTrueAsDouble = -1.0;                   // Length: VBA True → -1.0

    private static ToolResult AddThreadedHoleInSw(ThreadedHoleSpec spec)
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
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var ext = model.Extension;
            var fm = model.FeatureManager;
            var (drillMm, pitchMm) = ThreadedHoleSpec.GbTapTable[spec.ThreadSize];
            var drillM = drillMm / 1000.0;
            var pitchM = pitchMm / 1000.0;
            var isThrough = !spec.DepthMm.HasValue;
            // Depth in m. For through-all we pass a small positive value (SW ignores
            // when EndType=ThroughAll, but won't accept exactly 0 in some versions).
            var depthM = isThrough ? 0.01 : spec.DepthMm!.Value / 1000.0;

            // ── 2. Pick a planar ±Z end face (same heuristic as create_flange / axial_hole) ──
            model.ClearSelection2(true);
            var endFace = FindPlanarEndFace(model)
                ?? throw new McpToolException(
                    "Could not find a planar end face whose normal is along ±Z. " +
                    "add_threaded_hole expects a part extruded from the Front Plane " +
                    "(create_cylinder / create_flange / create_rectangular_block all qualify).");
            if (!((IEntity)endFace).Select4(Append: false, Data: null))
            {
                throw new McpToolException("Face.Select4 failed on the planar end face.");
            }

            // ── 3. HoleWizard5 — 27 args, GB tap recipe (v1 PR #24 magic) ──
            var hole = fm.HoleWizard5(
                GenericHoleType: SwWzdTap,                    // [0] = 4
                StandardIndex: SwStandardGB,                  // [1] = 13
                FastenerTypeIndex: SwGbTapFastenerType,       // [2] = 359
                SSize: spec.ThreadSize,                       // [3] = "M4" / ...
                EndType: isThrough ? EndCondThroughAll : EndCondBlind,  // [4]
                Diameter: drillM,                             // [5] = tap drill (m)
                Depth: depthM,                                // [6]
                Length: VbaTrueAsDouble,                      // [7] = -1.0 ("VBA True" sentinel)
                Value1: depthM,                               // [8] = thread length = depth
                Value2: pitchM,                               // [9] = pitch (m)
                Value3: CountersinkAngleRad,                  // [10] = π/1.8 ≈ 100° ★
                Value4: 0.0,                                  // [11]
                Value5: 0.0,                                  // [12]
                Value6: 0.0,                                  // [13]
                Value7: FeatureEnableFlag,                    // [14] = 1.0 ★
                Value8: FeatureEnableFlag,                    // [15] = 1.0 ★
                Value9: 0.0,                                  // [16]
                Value10: 0.0,                                 // [17]
                Value11: SwDefaultSentinel,                   // [18] = -1.0 ★
                Value12: SwDefaultSentinel,                   // [19] = -1.0 ★
                ThreadClass: string.Empty,                    // [20]
                RevDir: false,                                // [21]
                FeatureScope: true,                           // [22]
                AutoSelect: true,                             // [23]
                AssemblyFeatureScope: true,                   // [24]
                AutoSelectComponents: true,                   // [25]
                PropagateFeatureToParts: false);              // [26]

            if (hole == null)
            {
                var modeLabel = isThrough ? "through-all" : $"blind {spec.DepthMm} mm";
                throw new McpToolException(
                    $"HoleWizard5 returned null for {spec.ThreadSize} {modeLabel} tap. " +
                    "The tap-drill diameter may exceed the part's end-face dimension, " +
                    "or the face selection may not have stuck. (v1 PR #24 broke this " +
                    "path with magic Value7/8/11/12 — see DEV_LOG M13.)");
            }

            // ── 4. Save (in-place vs copy) — same M5 split ──────────────────
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
                    ref saveErrors, ref saveWarnings);
            }
            else
            {
                savedOk = ext.SaveAs(
                    Name: targetPath,
                    Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ExportData: null,
                    Errors: ref saveErrors, Warnings: ref saveWarnings);
            }

            if (!savedOk || !File.Exists(targetPath))
            {
                var api = isInPlace ? "Save3" : "SaveAs";
                throw new McpToolException(
                    $"{api} failed for '{targetPath}'. errors=0x{saveErrors:X} " +
                    $"warnings=0x{saveWarnings:X}.");
            }

            var modeStr = isThrough ? "through-all" : $"{spec.DepthMm} mm deep";
            return ToolResult.Ok(
                message:
                    $"Tapped {spec.ThreadSize} {modeStr} thread at end-face centroid " +
                    $"(drill Φ{drillMm} mm, pitch {pitchMm} mm); saved " +
                    $"{(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Returns the first planar face on the part's first solid body whose
    /// normal is aligned with the Z axis. Same heuristic as create_flange /
    /// add_axial_hole — kept private here (3rd duplicate; the boot-filter
    /// helper is the more pressing rule-of-three extraction target).
    /// </summary>
    private static IFace2? FindPlanarEndFace(IModelDoc2 model)
    {
        var part = (IPartDoc)model;
        var bodiesObj = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies || bodies.Length == 0)
        {
            return null;
        }
        var body = (IBody2)bodies[0];

        if (body.GetFaces() is not object[] faces) return null;

        foreach (var faceObj in faces)
        {
            var face = (IFace2)faceObj;
            var surface = (ISurface)face.GetSurface();
            if (!surface.IsPlane()) continue;
            if (face.Normal is not double[] normal || normal.Length < 3) continue;
            if (Math.Abs(normal[2]) > 0.99) return face;
        }
        return null;
    }
#endif
}
