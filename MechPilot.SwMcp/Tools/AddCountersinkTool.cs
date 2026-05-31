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
/// Adds one GB/T 152.2 countersink hole (锥形沉头孔, 90°, for flat-head
/// screws GB/T 819 / ISO 7046) at the centroid of the part's ±Z end face.
///
/// Sibling of AddThreadedHoleTool / AddCounterboreTool — same HoleWizard5
/// 27-arg backbone, different magic constants. v1 PR #25:
///   • GenericHoleType = 1 (swWzdCounterSink)
///   • FastenerType    = 363 (GB flat-head)
///   • Value layout    = { 1: cs_dia, 2: π/2 (90° angle), 4: 1.0 (flag),
///                         10=11=12: -1.0 (SW default sentinels) }
/// — different from CB's { 1, 2, 4, 6, 7 } layout because Value semantics
/// are per-hole-type, not universal.
///
/// **M3/M4/M5 not supported** — SW's internal GB countersink database is
/// missing those, HoleWizard5 returns null. Spec rejects them up front.
/// </summary>
[McpServerToolType]
public static class AddCountersinkTool
{
    [McpServerTool(Name = "add_countersink")]
    [Description(
        "Drill one GB/T 152.2 countersink hole (锥形沉头孔, 90°) at the " +
        "centroid of an existing part's ±Z end face. Used by flat-head " +
        "(sink-head) screws (GB/T 819 / ISO 7046). threadSize is one of " +
        "'M6', 'M8', 'M10', 'M12' (case-insensitive). M3/M4/M5 are NOT " +
        "supported — SW's internal GB countersink database is missing those " +
        "(v1 PR #25 finding). Pass depth=null or omit for through-all clearance; " +
        "pass a positive depth in mm for blind clearance. inputPath must be " +
        "an absolute path to an existing .sldprt. outputPath optional.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to drill.")]
        string inputPath,
        [Description("GB thread size: M6 / M8 / M10 / M12 (M3-M5 not supported).")]
        string threadSize,
        [Description("Blind clearance depth in mm; omit or null for through-all.")]
        double? depth = null,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new CountersinkSpec
        {
            InputPath = inputPath,
            ThreadSize = threadSize,
            DepthMm = depth,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(CountersinkSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddCountersinkInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_countersink failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_countersink requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private const int SwWzdCounterSink = 1;                  // GenericHoleType
    private const int SwStandardGB = 13;                     // StandardIndex
    private const int SwGbCountersinkFastenerType = 363;     // v1 PR #25 magic

    private const short EndCondThroughAll = 1;
    private const short EndCondBlind = 0;

    // Value-position constants (CSK-specific; differ from CB & GB-tap layouts).
    private const double FeatureEnableFlag = 1.0;            // Value4
    private const double CountersinkAngleRad = 1.5707963267948966;  // Value2 = π/2 = 90°
    private const double SwDefaultSentinel = -1.0;           // Value10/11/12
    private const double LengthSentinel = -1.0;              // Length field

    private static ToolResult AddCountersinkInSw(CountersinkSpec spec)
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
            var (clearanceMm, csDiaMm) = CountersinkSpec.GbTable[spec.ThreadSize];
            var clearanceM = clearanceMm / 1000.0;
            var csDiaM = csDiaMm / 1000.0;
            var isThrough = !spec.DepthMm.HasValue;
            var depthM = isThrough ? 0.01 : spec.DepthMm!.Value / 1000.0;

            // ── 2. Pick a planar ±Z end face (shared helper) ────────────────
            model.ClearSelection2(true);
            var endFace = PartGeometryHelpers.FindPlanarEndFace(model)
                ?? throw new McpToolException(
                    "Could not find a planar end face whose normal is along ±Z. " +
                    "add_countersink expects a part extruded from the Front Plane.");
            if (!((IEntity)endFace).Select4(Append: false, Data: null))
            {
                throw new McpToolException("Face.Select4 failed on the planar end face.");
            }

            // ── 3. HoleWizard5 — 27 args, GB CounterSink recipe ─────────────
            var hole = fm.HoleWizard5(
                GenericHoleType: SwWzdCounterSink,            // [0] = 1
                StandardIndex: SwStandardGB,                  // [1] = 13
                FastenerTypeIndex: SwGbCountersinkFastenerType, // [2] = 363 ★
                SSize: spec.ThreadSize,                       // [3]
                EndType: isThrough ? EndCondThroughAll : EndCondBlind,
                Diameter: clearanceM,                         // [5] = clearance drill
                Depth: depthM,                                // [6]
                Length: LengthSentinel,                       // [7] = -1.0
                Value1: csDiaM,                               // [8] = cs major dia ★
                Value2: CountersinkAngleRad,                  // [9] = π/2 = 90° ★
                Value3: 0.0,                                  // [10]
                Value4: FeatureEnableFlag,                    // [11] = 1.0 ★
                Value5: 0.0,                                  // [12]
                Value6: 0.0,                                  // [13]
                Value7: 0.0,                                  // [14]
                Value8: 0.0,                                  // [15]
                Value9: 0.0,                                  // [16]
                Value10: SwDefaultSentinel,                   // [17] = -1.0 ★
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
                    $"HoleWizard5 returned null for {spec.ThreadSize} {modeLabel} countersink. " +
                    "The clearance diameter may exceed the part's end-face dimension. " +
                    "(GB CSK path uses Value1=cs_dia, Value2=π/2 (90°), Value4=1.0, " +
                    "Value10/11/12=-1.0 — see DEV_LOG M14.)");
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
                    $"Drilled {spec.ThreadSize} countersink {modeStr} at end-face centroid " +
                    $"(clearance Φ{clearanceMm} mm, CS Φ{csDiaMm} mm × 90°); saved " +
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
