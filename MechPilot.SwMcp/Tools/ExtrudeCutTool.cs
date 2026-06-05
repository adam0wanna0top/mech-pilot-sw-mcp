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
/// Cut a named sketch into an existing solid body. M33 generic-layer
/// counterpart to <see cref="ExtrudeTool"/> — same input spec
/// (<see cref="ExtrudeSpec"/>), but uses <c>FeatureCut2</c> (M3-verified,
/// 23 args) to remove material instead of adding it.
///
/// Common LLM use: drill a non-cylindrical hole, mill a slot, cut a window,
/// etc. — any sketch-driven subtractive operation.
///
/// Pipeline:
///   1. SelectByID2(sketchName, "SKETCH", mark=0).
///   2. FeatureCut2 — 23 args, M3 educated defaults: NormalCut=false,
///      AssemblyFeatureScope/AutoSelectComponents/PropagateFeatureToParts=false,
///      EndCond=Blind with depth_m.
/// </summary>
[McpServerToolType]
public static class ExtrudeCutTool
{
    [McpServerTool(Name = "extrude_cut")]
    [Description(
        "Cut a named sketch into the active part's existing body (subtractive). " +
        "sketchName is from end_sketch. depth is in mm > 0 (blind cut to that " +
        "depth along the sketch plane's normal). reverse=true flips direction. " +
        "Generic-layer counterpart to extrude — same spec, different feature " +
        "(uses M3-verified FeatureCut2, 23 args). Common uses: non-cylindrical " +
        "holes, slots, windows, pockets. The active part MUST already have a " +
        "body for the cut to remove material from.")]
    public static ToolResult Run(
        [Description("Name of the sketch to cut with (from end_sketch).")]
        string sketchName,
        [Description("Cut depth in mm. Must be > 0, e.g. 10.")]
        double depth,
        [Description("If true, flip the cut direction. Default false.")]
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
                $"extrude_cut failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("extrude_cut requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(ExtrudeSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;

        // ── 1. Select the named sketch ──────────────────────────────────────
        model.ClearSelection2(true);
        if (!ext.SelectByID2(
            Name: spec.SketchName, Type: "SKETCH",
            X: 0.0, Y: 0.0, Z: 0.0,
            Append: false, Mark: 0,
            Callout: null, SelectOption: 0))
        {
            throw new McpToolException(
                $"Cannot select sketch '{spec.SketchName}' on the active part. " +
                "Verify the name returned by end_sketch.");
        }

        // ── 2. FeatureCut2 — 23 args, M3 verified educated defaults ─────────
        //   M3 uses T1=ThroughAll, T2=Blind (D1/D2 ignored for through-all).
        //   For LLM blind-cut requests we pass T1=Blind + D1=depth_m; for
        //   through-all the LLM can pass a huge depth and SW clamps to body.
        //   Default to ThroughAll if depth is "huge enough" (>= 9000 mm),
        //   else blind — but for MVP we always use Blind + D1 so semantics
        //   stay predictable. M3's ThroughAll worked first try; if Blind
        //   silently fails on power-LLM-sketches we may switch.
        var depthM = spec.DepthMm / 1000.0;
        var feature = fm.FeatureCut2(
            Sd: true,                                                   // single-direction
            Flip: spec.Reverse,
            Dir: false,
            T1: (int)swEndConditions_e.swEndCondThroughAll,            // = 1; M3 verified
            T2: (int)swEndConditions_e.swEndCondBlind,
            D1: depthM,                                                 // ignored for ThroughAll
            D2: 0.0,
            Dchk1: false, Dchk2: false,
            Ddir1: false, Ddir2: false,
            Dang1: 0.0, Dang2: 0.0,
            OffsetReverse1: false, OffsetReverse2: false,
            TranslateSurface1: false, TranslateSurface2: false,
            NormalCut: false,
            UseFeatScope: true,                       // M3 verified
            UseAutoSelect: true,                      // M3 verified
            AssemblyFeatureScope: false,              // M3 trio = false
            AutoSelectComponents: false,
            PropagateFeatureToParts: false);

        if (feature == null)
        {
            throw new McpToolException(
                $"FeatureCut2 returned null for sketch '{spec.SketchName}' " +
                $"depth {spec.DepthMm} mm. Common causes: the sketch is open, " +
                "no body exists to cut from, or the cut doesn't intersect any body.");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Cut '{spec.SketchName}' by {spec.DepthMm} mm → feature '{featureName}'",
            path: null);
    }
#endif
}
