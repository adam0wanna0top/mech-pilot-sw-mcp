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
/// GEOMETRY (the M34 lesson): the cut sketch must sit on a plane/face that
/// BOUNDS the body where the cut enters — e.g. add_ref_plane at the body's far
/// face, sketch there, then cut back through. A sketch on the body's *base*
/// construction plane (the one it was extruded from) does NOT cut, in any
/// direction. This — not "face-based vs plane-based" or selection state, both
/// of which the M33 notes blamed — is the real constraint.
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
        "sketchName is from end_sketch. depth is mm > 0, cut blind along the " +
        "sketch-plane normal; pass depth >= the body thickness for a through " +
        "hole. The cut direction is auto-detected (reverse only forces which " +
        "side to try first). IMPORTANT: sketch the cut on a plane that bounds " +
        "the body where the cut enters — e.g. add_ref_plane at the far face, " +
        "sketch there, then cut back through. A sketch on the body's base plane " +
        "(the one it was extruded from) will NOT cut. The active part MUST " +
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

        // Blind cut to `depth` along the sketch-plane normal. A depth ≥ the body
        // thickness produces a through hole — SW simply stops at the far side.
        //
        // M34 root-cause (corrects the M33 "face-based required / selection-state"
        // guess): the cut sketch must sit on a plane/face that BOUNDS the body on
        // the side the cut enters from (e.g. a ref plane coincident with the
        // cylinder's far face), NOT on the base construction plane the body was
        // extruded from. The selection mechanism is irrelevant —
        // SelectByID2(mark=0) yields the exact state (count=1, type=9 SKETCHES)
        // that M3's implicit post-exit selection does; both work, the geometry
        // is what differs.
        //
        // Direction is auto-detected: a cut that points away from the body just
        // returns null (removes nothing), so we try spec.Reverse first and the
        // opposite if that misses. First non-null wins — for a normal hole only
        // one direction intersects, so the choice is unambiguous.
        var feature = TryCut(model, ext, fm, spec.SketchName, depthM, spec.Reverse)
                   ?? TryCut(model, ext, fm, spec.SketchName, depthM, !spec.Reverse);

        if (feature == null)
        {
            throw new McpToolException(
                $"FeatureCut2 returned null for sketch '{spec.SketchName}' depth {spec.DepthMm} mm " +
                "(tried both directions). Common causes: the sketch is open / zero-area; there is no " +
                "body to cut; the sketch sits on the body's BASE plane (instead sketch on a plane/face " +
                "that bounds the body — e.g. add_ref_plane at the far face, then sketch + cut back " +
                "through); or the profile lies entirely outside the body's cross-section.");
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
        string sketchName, double depthM, bool flip)
    {
        model.ClearSelection2(true);
        if (!ext.SelectByID2(sketchName, "SKETCH", 0.0, 0.0, 0.0, false, 0, null, 0))
        {
            throw new McpToolException(
                $"Cannot select sketch '{sketchName}' on the active part. " +
                "Verify the name returned by end_sketch.");
        }

        return fm.FeatureCut2(
            Sd: true, Flip: flip, Dir: false,
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
