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
/// Revolve a named sketch around its embedded centerline into a solid body.
/// M31 — generic primitives layer third milestone (along with
/// <see cref="ExtrudeTool"/>). Wraps SW's <c>FeatureRevolve2</c> (20 args,
/// reflected — v1 PR #5 lesson: docs say 15) with the same educated
/// defaults as CreateHemisphereTool / CreateSphereTool / CreateFrustumTool.
///
/// The revolve axis is the centerline embedded in the sketch (added via
/// sketch_centerline before end_sketch); SW auto-binds it when the sketch
/// is selected with mark=0. The sketch profile + centerline must coexist
/// in the same sketch.
///
/// Pipeline:
///   1. ClearSelection2.
///   2. SelectByID2(sketchName, "SKETCH", mark=0).
///   3. FeatureRevolve2 — 20 args, all same educated defaults as
///      hemisphere/sphere/frustum (SingleDir=true, IsSolid=true, IsCut=false,
///      Dir1Type=Blind, Dir1Angle=angle_rad, Merge=true,
///      UseFeatScope/UseAutoSelect=true, all other position fields 0/false).
/// </summary>
[McpServerToolType]
public static class RevolveTool
{
    [McpServerTool(Name = "revolve")]
    [Description(
        "Revolve a named sketch around its embedded centerline into a solid " +
        "body. The sketch MUST contain a profile + at least one centerline " +
        "(added via sketch_centerline before end_sketch) — SW uses the " +
        "centerline as the axis of revolution. sketchName is the name " +
        "returned by end_sketch. angle is the sweep in degrees in (0, 360]; " +
        "360 (full revolution) is the most common. reverse=true flips the " +
        "revolve direction. Uses solid + boss + merge=true defaults. " +
        "Returns the created feature's name (e.g. '旋转1') in the result message.")]
    public static ToolResult Run(
        [Description("Name of the sketch to revolve (from end_sketch, e.g. '草图1').")]
        string sketchName,
        [Description("Revolve angle in degrees. (0, 360], typically 360.")]
        double angle = 360.0,
        [Description("If true, flip the revolve direction. Default false.")]
        bool reverse = false)
    {
        return RunWithSpec(new RevolveSpec
        {
            SketchName = sketchName,
            AngleDeg = angle,
            Reverse = reverse,
        });
    }

    public static ToolResult RunWithSpec(RevolveSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"revolve failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("revolve requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(RevolveSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;

        // ── 1. Select the named sketch (mark=0; SW auto-binds embedded centerline as axis) ──
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

        // ── 2. FeatureRevolve2 — 20 args, same educated defaults as CreateHemisphereTool ──
        var angleRad = spec.AngleDeg * Math.PI / 180.0;
        var feature = fm.FeatureRevolve2(
            SingleDir: true,
            IsSolid: true,
            IsThin: false,
            IsCut: false,
            ReverseDir: spec.Reverse,
            BothDirectionUpToSameEntity: false,
            Dir1Type: (int)swEndConditions_e.swEndCondBlind,
            Dir2Type: (int)swEndConditions_e.swEndCondBlind,
            Dir1Angle: angleRad,
            Dir2Angle: 0.0,
            OffsetReverse1: false,
            OffsetReverse2: false,
            OffsetDistance1: 0.0,
            OffsetDistance2: 0.0,
            ThinType: 0,
            ThinThickness1: 0.0,
            ThinThickness2: 0.0,
            Merge: true,
            UseFeatScope: true,
            UseAutoSelect: true);

        if (feature == null)
        {
            throw new McpToolException(
                $"FeatureRevolve2 returned null for sketch '{spec.SketchName}' " +
                $"angle {spec.AngleDeg}°. Common causes: the sketch profile is " +
                "open / self-intersecting / does not touch the centerline, or " +
                "the centerline was not embedded in the sketch (call " +
                "sketch_centerline before end_sketch).");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Revolved '{spec.SketchName}' by {spec.AngleDeg}° → feature '{featureName}'",
            path: null);
    }
#endif
}
