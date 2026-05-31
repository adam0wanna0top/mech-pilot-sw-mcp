using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Interop;
using MechPilot.SwMcp.Tools.Internal;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Adds one GB/T 152.3 counterbore hole (柱形沉头孔, for inner-hex
/// cylindrical-head screws GB/T 70.1 / DIN 912) at the centroid of the
/// part's ±Z end face.
///
/// Sibling of AddThreadedHoleTool — same HoleWizard5 27-arg backbone with
/// different magic constants. v1 PR #25 broke this path by recording a
/// "GB M6 CounterBore" macro:
///   • GenericHoleType = 0 (swWzdCounterBore)
///   • FastenerType    = 361 (GB inner-hex socket cyl head)
///   • Value layout    = { 1: cb_dia, 2: cb_depth, 4: 1.0 (flag),
///                         6: cb_dia + tolerance, 7: π/1.8 (placeholder) }
/// — different from GB Tap's { 7=8=1.0, 11=12=-1.0 } layout because Value
/// semantics are per-hole-type, not universal (v1-history M25 finding 2).
/// </summary>
[McpServerToolType]
public static class AddCounterboreTool
{
    [McpServerTool(Name = "add_counterbore")]
    [Description(
        "Drill one GB/T 152.3 counterbore hole (柱形沉头孔) at the centroid of " +
        "an existing part's ±Z end face. Used by inner-hex socket cylindrical-" +
        "head screws (GB/T 70.1 / DIN 912). threadSize is one of " +
        "'M3', 'M4', 'M5', 'M6', 'M8', 'M10', 'M12' (case-insensitive). " +
        "Pass depth=null or omit for a through-all clearance; pass a positive " +
        "depth in mm for a blind clearance. The counterbore depth itself is " +
        "fixed by GB/T 152.3, not by this depth parameter. inputPath must be " +
        "an absolute path to an existing .sldprt. outputPath optional: " +
        "empty = overwrite the input in place.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to drill.")]
        string inputPath,
        [Description("GB thread size: M3 / M4 / M5 / M6 / M8 / M10 / M12.")]
        string threadSize,
        [Description("Blind clearance depth in mm; omit or null for through-all.")]
        double? depth = null,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new CounterboreSpec
        {
            InputPath = inputPath,
            ThreadSize = threadSize,
            DepthMm = depth,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(CounterboreSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddCounterboreInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_counterbore failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_counterbore requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private const int SwWzdCounterBore = 0;                  // GenericHoleType
    private const int SwStandardGB = 13;                     // StandardIndex
    private const int SwGbCounterboreFastenerType = 361;     // v1 PR #25 magic

    private const short EndCondThroughAll = 1;
    private const short EndCondBlind = 0;

    // Value-position constants (CB-specific; differ from GB-tap layout).
    private const double FeatureEnableFlag = 1.0;            // Value4
    private const double CbDiameterToleranceM = 0.00005;     // Value6: cb_dia + 0.05 mm
    private const double PlaceholderAngleRad = 1.74532925199433;  // Value7: π/1.8 ≈ 100°
    private const double LengthSentinel = -1.0;              // Length field (VBA True)

    private static ToolResult AddCounterboreInSw(CounterboreSpec spec)
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
            var (clearanceMm, cbDiaMm, cbDepthMm) = CounterboreSpec.GbTable[spec.ThreadSize];
            var clearanceM = clearanceMm / 1000.0;
            var cbDiaM = cbDiaMm / 1000.0;
            var cbDepthM = cbDepthMm / 1000.0;
            var isThrough = !spec.DepthMm.HasValue;
            var depthM = isThrough ? 0.01 : spec.DepthMm!.Value / 1000.0;

            // ── 2. Pick a planar ±Z end face (shared helper) ────────────────
            model.ClearSelection2(true);
            var endFace = PartGeometryHelpers.FindPlanarEndFace(model)
                ?? throw new McpToolException(
                    "Could not find a planar end face whose normal is along ±Z. " +
                    "add_counterbore expects a part extruded from the Front Plane.");
            if (!((IEntity)endFace).Select4(Append: false, Data: null))
            {
                throw new McpToolException("Face.Select4 failed on the planar end face.");
            }

            // ── 3. HoleWizard5 — 27 args, GB CounterBore recipe ─────────────
            var hole = fm.HoleWizard5(
                GenericHoleType: SwWzdCounterBore,            // [0] = 0
                StandardIndex: SwStandardGB,                  // [1] = 13
                FastenerTypeIndex: SwGbCounterboreFastenerType, // [2] = 361 ★
                SSize: spec.ThreadSize,                       // [3]
                EndType: isThrough ? EndCondThroughAll : EndCondBlind,
                Diameter: clearanceM,                         // [5] = clearance drill
                Depth: depthM,                                // [6]
                Length: LengthSentinel,                       // [7] = -1.0
                Value1: cbDiaM,                               // [8] = cb diameter ★
                Value2: cbDepthM,                             // [9] = cb depth ★
                Value3: 0.0,                                  // [10]
                Value4: FeatureEnableFlag,                    // [11] = 1.0 ★
                Value5: 0.0,                                  // [12]
                Value6: cbDiaM + CbDiameterToleranceM,        // [13] = cb_dia + 0.05 mm ★
                Value7: PlaceholderAngleRad,                  // [14] = π/1.8 ≈ 100° ★
                Value8: 0.0,                                  // [15]
                Value9: 0.0,                                  // [16]
                Value10: 0.0,                                 // [17]
                Value11: 0.0,                                 // [18]
                Value12: 0.0,                                 // [19]
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
                    $"HoleWizard5 returned null for {spec.ThreadSize} {modeLabel} counterbore. " +
                    "The clearance diameter may exceed the part's end-face dimension. " +
                    "(GB CB path uses Value1=cb_dia, Value2=cb_depth, Value4=1.0, " +
                    "Value6=cb_dia+0.05mm, Value7=π/1.8 — see DEV_LOG M14.)");
            }

            // ── 4. Save (in-place vs copy) ──────────────────────────────────
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
                    $"Drilled {spec.ThreadSize} counterbore {modeStr} at end-face centroid " +
                    $"(clearance Φ{clearanceMm} mm, CB Φ{cbDiaMm}×{cbDepthMm} mm); saved " +
                    $"{(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }
#endif
}
