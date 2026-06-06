using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Add a structural rib (stiffener / gusset) to the active part by thickening
/// an open sketch contour. M35 generic-layer feature. Wraps SW's
/// <c>InsertRib</c> (10 args, reflected).
///
/// M35 note: rib had been deferred since M27 as a "1-2 day scary exploration"
/// (v1 hit "selection 不识"). Like the M34 cut/sweep misdiagnoses, that fear
/// was overblown — reflecting the signature + correct geometry + the standard
/// rib options worked on the first sensible parameter combo. v1's failure was
/// a late-binding artifact, not real SW complexity.
///
/// InsertRib returns void (no Feature to null-check), so success is detected
/// by counting "Rib"-type features before/after. The fill direction is
/// auto-detected: a rib pointing away from the body's walls produces nothing
/// (count unchanged), so we try one direction then the other.
///
/// Fixed options (cover the common gusset/stiffener case): Is2Sided=true
/// (thickness symmetric about the sketch plane — sketch on a plane through the
/// middle of the wall span), parallel-to-sketch extrusion (IsNormToSketch=false,
/// the rib grows in-plane until it reaches the walls), no draft.
///
/// GEOMETRY: sketch an OPEN contour (typically a single line) on a plane that
/// cuts through the body where the rib belongs (e.g. add_ref_plane mid-way),
/// with the line spanning between the walls the rib should connect.
/// </summary>
[McpServerToolType]
public static class RibTool
{
    [McpServerTool(Name = "rib")]
    [Description(
        "Add a structural rib (stiffener / gusset) to the active part's body. " +
        "sketchName is an OPEN-contour sketch from end_sketch — typically a single " +
        "line spanning between the walls the rib connects. thickness is mm > 0, " +
        "applied symmetrically about the sketch plane, so sketch on a plane through " +
        "the MIDDLE of the rib's span (e.g. add_ref_plane mid-way along the body). " +
        "The rib grows in the sketch plane until it reaches the body walls; its " +
        "fill direction is auto-detected. The active part MUST already have a body. " +
        "Common uses: gussets in brackets, stiffeners under plates, webs between bosses.")]
    public static ToolResult Run(
        [Description("Name of the open-contour rib sketch (from end_sketch).")]
        string sketchName,
        [Description("Rib thickness in mm. Must be > 0, e.g. 6.")]
        double thickness,
        [Description("If true, try the opposite fill direction first. Default false (auto-detected).")]
        bool reverse = false)
    {
        return RunWithSpec(new RibSpec
        {
            SketchName = sketchName,
            ThicknessMm = thickness,
            Reverse = reverse,
        });
    }

    public static ToolResult RunWithSpec(RibSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"rib failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("rib requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(RibSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;
        var thicknessM = spec.ThicknessMm / 1000.0;

        // InsertRib returns void, so detect success by the rib-feature count
        // delta. Auto-detect fill direction: try spec.Reverse first, then the
        // opposite — a rib pointing away from the walls just produces nothing.
        var before = CountRibs(model);
        if (!TryRib(model, ext, fm, spec.SketchName, thicknessM, spec.Reverse) || CountRibs(model) == before)
        {
            TryRib(model, ext, fm, spec.SketchName, thicknessM, !spec.Reverse);
        }

        if (CountRibs(model) == before)
        {
            throw new McpToolException(
                $"InsertRib produced no rib for sketch '{spec.SketchName}' (tried both fill " +
                "directions). Common causes: the sketch is not an open contour; there is no body " +
                "for the rib to fill against; or the sketch does not span between walls (sketch a " +
                "line on a plane through the middle of the gap, with endpoints reaching the walls).");
        }

        return ToolResult.Ok(
            message: $"Added rib from '{spec.SketchName}', thickness {spec.ThicknessMm} mm",
            path: null);
    }

    /// <summary>
    /// Select the named sketch (mark=0) and call InsertRib once with the common
    /// gusset options (2-sided symmetric thickness, parallel-to-sketch fill, no
    /// draft). Returns false if the sketch could not be selected. InsertRib's
    /// own success is checked by the caller via the rib-feature count.
    /// </summary>
    private static bool TryRib(
        IModelDoc2 model, IModelDocExtension ext, IFeatureManager fm,
        string sketchName, double thicknessM, bool reverseMaterial)
    {
        model.ClearSelection2(true);
        if (!ext.SelectByID2(sketchName, "SKETCH", 0.0, 0.0, 0.0, false, 0, null, 0))
        {
            return false;
        }

        fm.InsertRib(
            Is2Sided: true,
            ReverseThicknessDir: false,
            Thickness: thicknessM,
            ReferenceEdgeIndex: 0,
            ReverseMaterialDir: reverseMaterial,
            IsDrafted: false,
            DraftOutward: false,
            DraftAngle: 0.0,
            IsNormToSketch: false,
            IsDraftedFromWall: false);
        return true;
    }

    /// <summary>Counts features of type "Rib" on the part (InsertRib returns void).</summary>
    private static int CountRibs(IModelDoc2 model)
    {
        int n = 0;
        var f = model.FirstFeature() as IFeature;
        while (f != null)
        {
            if (string.Equals(f.GetTypeName2(), "Rib", StringComparison.Ordinal))
            {
                n++;
            }
            f = f.GetNextFeature() as IFeature;
        }
        return n;
    }
#endif
}
