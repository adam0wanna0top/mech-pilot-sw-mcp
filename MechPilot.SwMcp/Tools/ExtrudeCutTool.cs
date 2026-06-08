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
/// Cut a named sketch into an existing solid body. Generic-layer counterpart
/// to <see cref="ExtrudeTool"/> — same input spec (<see cref="ExtrudeSpec"/>),
/// but uses <c>FeatureCut2</c> (M3-verified, 23 args) to remove material
/// instead of adding it. Happy case made to work in M34.
///
/// Common LLM use: drill a non-cylindrical hole, mill a slot, cut a window,
/// etc. — any sketch-driven subtractive operation.
///
/// GEOMETRY: the cut direction is genuinely auto-detected — TryCut runs
/// FeatureCut2 with Dir=spec.Reverse and, if that removes nothing, again with
/// Dir=!spec.Reverse. So the sketch can sit on ANY plane/face that touches the
/// body — a body face, a ref plane, OR the base construction plane it was
/// extruded from — and the cut goes into the material. (The earlier M34 "base
/// plane won't cut, in any direction" note was a symptom of the reverse→Flip
/// mis-wiring fixed here: both tries used Dir=false = anti-normal only.)
///
/// Pipeline (per direction; tried for spec.Reverse then its opposite):
///   1. ClearSelection2 + SelectByID2(sketchName, "SKETCH", mark=0).
///   2. FeatureCut2 — single-direction blind to depth_m, NormalCut=false,
///      AssemblyFeatureScope/AutoSelectComponents/PropagateFeatureToParts=false.
/// </summary>
[McpServerToolType]
public static class ExtrudeCutTool
{
    [McpServerTool(Name = "extrude_cut")]
    [Description(
        "Cut a named sketch into the active part's existing body (subtractive). " +
        "sketchName is from end_sketch. depth is mm > 0, cut blind to that depth; " +
        "pass depth >= the body thickness for a through hole. The cut direction " +
        "is auto-detected — it tries both directions and keeps whichever removes " +
        "material, so the sketch can sit on ANY plane or face that touches the " +
        "body (a body face, a ref plane, or the base plane it was extruded from); " +
        "reverse only sets which direction is tried first. The active part MUST " +
        "already have a body. Common uses: non-cylindrical holes, slots, " +
        "windows, pockets.")]
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
        var depthM = spec.DepthMm / 1000.0;

        // Blind cut to `depth`. A depth ≥ the body thickness produces a through
        // hole — SW simply stops at the far side.
        //
        // Direction is genuinely auto-detected: TryCut runs FeatureCut2 with
        // Dir=spec.Reverse; a cut pointing away from the body removes nothing and
        // returns null, so we retry with Dir=!spec.Reverse. First non-null wins —
        // for a normal hole only one direction intersects, so it's unambiguous.
        // This is why a sketch on ANY touching plane cuts, including the body's
        // base plane (the pre-fix "base plane won't cut" note was the reverse→Flip
        // bug: both tries were Dir=false, so only the anti-normal side was tried).
        // SelectByID2(mark=0) yields the right selection state (M3-verified); the
        // cut direction, not the selection mechanism, was the missing variable.
        var feature = TryCut(model, ext, fm, spec.SketchName, depthM, spec.Reverse)
                   ?? TryCut(model, ext, fm, spec.SketchName, depthM, !spec.Reverse);

        if (feature == null)
        {
            throw new McpToolException(
                $"FeatureCut2 returned null for sketch '{spec.SketchName}' depth {spec.DepthMm} mm " +
                "(tried both directions). Common causes: the sketch is open / zero-area; there is no " +
                "body to cut; or the profile lies entirely outside the body's cross-section, so neither " +
                "direction removes material.");
        }

        return ToolResult.Ok(
            message: $"Cut '{spec.SketchName}' by {spec.DepthMm} mm → feature '{feature.Name}'",
            path: null);
    }

    /// <summary>
    /// Select the named sketch (mark=0) and attempt one single-direction blind
    /// <c>FeatureCut2</c> (M3-verified 23-arg overload: NormalCut=false, the
    /// assembly trio all false). Re-selects on every call because a null
    /// FeatureCut2 can drop the selection. Returns null if SW could not build
    /// the cut (typically the cut misses the body in this direction).
    /// </summary>
    private static Feature? TryCut(
        IModelDoc2 model, IModelDocExtension ext, IFeatureManager fm,
        string sketchName, double depthM, bool reverseDir)
    {
        model.ClearSelection2(true);
        if (!ext.SelectByID2(sketchName, "SKETCH", 0.0, 0.0, 0.0, false, 0, null, 0))
        {
            throw new McpToolException(
                $"Cannot select sketch '{sketchName}' on the active part. " +
                "Verify the name returned by end_sketch.");
        }

        return fm.FeatureCut2(
            Sd: true, Flip: false, Dir: reverseDir,                 // M47-cut fix: reverse acts on Dir (direction), not Flip (a no-op for the cut direction)
            T1: (int)swEndConditions_e.swEndCondBlind,
            T2: (int)swEndConditions_e.swEndCondBlind,
            D1: depthM, D2: 0.0,
            Dchk1: false, Dchk2: false, Ddir1: false, Ddir2: false,
            Dang1: 0.0, Dang2: 0.0,
            OffsetReverse1: false, OffsetReverse2: false,
            TranslateSurface1: false, TranslateSurface2: false,
            NormalCut: false,
            UseFeatScope: true, UseAutoSelect: true,
            AssemblyFeatureScope: false, AutoSelectComponents: false,
            PropagateFeatureToParts: false);
    }
#endif
}
