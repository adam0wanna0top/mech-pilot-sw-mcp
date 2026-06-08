using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Extrude a named sketch into a solid body. M31 — generic primitives layer
/// third milestone (along with <see cref="RevolveTool"/>). Wraps SW's
/// <c>FeatureExtrusion3</c> with educated defaults: blind end condition,
/// single-direction, merge=true.
///
/// Pipeline:
///   1. ClearSelection2.
///   2. SelectByID2(sketchName, "SKETCH", mark=0). The sketch name is
///      whatever <see cref="EndSketchTool"/> returned (typically "草图1").
///   3. FeatureExtrusion3 with the same 23 educated-default args as
///      CreateCylinderTool (single-direction blind extrude, depth=spec.DepthMm,
///      merge=true, UseFeatScope/UseAutoSelect=true).
///
/// On null return, the most common causes are: (a) the sketch name doesn't
/// resolve in SW; (b) the sketch is open / self-intersecting / zero-area;
/// (c) the active doc is not a part. The error message guides the LLM
/// through these.
/// </summary>
[McpServerToolType]
public static class ExtrudeTool
{
    [McpServerTool(Name = "extrude")]
    [Description(
        "Extrude a named sketch into a solid body on the active part. " +
        "sketchName is the name returned by end_sketch (e.g. '草图1' / " +
        "'Sketch1'). depth is the extrusion length in mm (along the sketch " +
        "plane's normal). reverse=true flips against the default direction. " +
        "Uses blind + single-direction + merge=true defaults — covers ~95% of " +
        "LLM extrude cases. Requires the active part to have the named sketch " +
        "(call start_sketch / sketch_* / end_sketch first). Returns the " +
        "created feature's name (e.g. '凸台-拉伸1') in the result message.")]
    public static ToolResult Run(
        [Description("Name of the sketch to extrude (from end_sketch, e.g. '草图1').")]
        string sketchName,
        [Description("Extrusion depth in mm. Must be > 0, e.g. 30.")]
        double depth,
        [Description("If true, flip the extrude direction. Default false.")]
        bool reverse = false)
    {
        return RunWithSpec(new ExtrudeSpec
        {
            SketchName = sketchName,
            DepthMm = depth,
            Reverse = reverse,
        });
    }

    public static ToolResult RunWithSpec(ExtrudeSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"extrude failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("extrude requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(ExtrudeSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;

        // ── 1. Select the named sketch (mark=0 per FeatureExtrusion3 contract) ──
        model.ClearSelection2(true);
        if (!ext.SelectByID2(
            Name: spec.SketchName, Type: "SKETCH",
            X: 0.0, Y: 0.0, Z: 0.0,
            Append: false, Mark: 0,
            Callout: null, SelectOption: 0))
        {
            throw new McpToolException(
                $"Cannot select sketch '{spec.SketchName}' on the active part. " +
                "Verify the name returned by end_sketch — SW's auto-naming is " +
                "language-sensitive ('草图1' on CN UI, 'Sketch1' on EN UI).");
        }

        // ── 2. FeatureExtrusion3 — same 23 educated defaults as CreateCylinderTool ──
        var depthM = spec.DepthMm / 1000.0;
        var feature = fm.FeatureExtrusion3(
            Sd: true,                                                   // single-direction
            Flip: false,                                                // thin-wall flip — NOT the extrude direction
            Dir: spec.Reverse,                                          // reverse extrude direction (M47 fix: was wired to Flip, a no-op for solid bosses)
            T1: (int)swEndConditions_e.swEndCondBlind,                  // = 0
            T2: (int)swEndConditions_e.swEndCondBlind,
            D1: depthM,
            D2: 0.0,
            Dchk1: false, Dchk2: false,
            Ddir1: false, Ddir2: false,
            Dang1: 0.0, Dang2: 0.0,
            OffsetReverse1: false, OffsetReverse2: false,
            TranslateSurface1: false, TranslateSurface2: false,
            Merge: true,
            UseFeatScope: true,
            UseAutoSelect: true,
            T0: (int)swStartConditions_e.swStartSketchPlane,
            StartOffset: 0.0,
            FlipStartOffset: false);

        if (feature == null)
        {
            throw new McpToolException(
                $"FeatureExtrusion3 returned null for sketch '{spec.SketchName}' " +
                $"depth {spec.DepthMm} mm. Common causes: the sketch is open / " +
                "self-intersecting / zero-area, or the active doc is not a part.");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Extruded '{spec.SketchName}' by {spec.DepthMm} mm → feature '{featureName}'",
            path: null);
    }
#endif
}
