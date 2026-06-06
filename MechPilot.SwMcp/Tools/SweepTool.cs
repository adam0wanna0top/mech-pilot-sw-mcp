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
/// Sweep a profile sketch along a path sketch into a solid body. Happy case
/// made to work in M34.
///
/// M34 correction (third M33 misdiagnosis fixed, after extrude_cut/revolve_cut):
/// the simple 14-arg <c>InsertProtrusionSwept</c> works fine — M33's switch to
/// the <c>CreateDefinition(swFmSweep=17) + AccessSelections + CreateFeature</c>
/// path was unnecessary and RPC-faulted (0x80010105). The two real
/// prerequisites M32/M33 missed:
///   • SELECTION MARKS: profile = mark 1, path = mark 4 (loft uses mark=1 for
///     all profiles; sweep does NOT — reusing the loft mark is why M32's
///     14-arg call silently failed).
///   • GEOMETRY: the profile plane must be ~perpendicular to the path at the
///     path's start (M32 swept a Front-Plane circle along an X path that lay
///     IN the profile plane — degenerate). Put the profile on a plane normal
///     to the path's initial direction and start the path at the profile.
///
/// Pipeline:
///   1. ClearSelection2.
///   2. SelectByID2(profile, "SKETCH", mark=1) + SelectByID2(path, "SKETCH",
///      mark=4, append=true).
///   3. InsertProtrusionSwept(14 args, follow-path defaults, merge=true).
///
/// Verified M34: Top-Plane circle profile + Front-Plane Y-line path → straight
/// D10×50 pipe (3 faces / 2 edges); quarter-arc path → clean elbow (same topology).
/// </summary>
[McpServerToolType]
public static class SweepTool
{
    [McpServerTool(Name = "sweep")]
    [Description(
        "Sweep a profile sketch along a path sketch into a solid body. " +
        "profileSketchName must be a single closed-contour sketch (the cross-" +
        "section); pathSketchName must be a single continuous open-curve sketch " +
        "(the trajectory, straight or curved). Both names from end_sketch. " +
        "GEOMETRY: put the profile on a plane PERPENDICULAR to the path's start " +
        "direction and start the path at the profile center — e.g. profile circle " +
        "on the Top plane (normal +Y) + path line/arc on the Front plane starting " +
        "at the origin going +Y. (A path lying in the profile's own plane will " +
        "fail.) Common uses: pipes, bent tubing, cables, fan blades, cams, " +
        "trim along a curve.")]
    public static ToolResult Run(
        [Description("Name of the cross-section profile sketch (closed area).")]
        string profileSketchName,
        [Description("Name of the path sketch (open curve).")]
        string pathSketchName)
    {
        return RunWithSpec(new SweepSpec
        {
            ProfileSketchName = profileSketchName,
            PathSketchName = pathSketchName,
        });
    }

    public static ToolResult RunWithSpec(SweepSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sweep failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sweep requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SweepSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;

        // Sweep selection marks are NOT uniform like loft's: SW expects the
        // profile selected with mark=1 and the path with mark=4. (Loft uses
        // mark=1 for every profile; reusing that here is why M32's sweep
        // silently failed.) InsertProtrusionSwept reads profile + path from
        // these marks; it takes no profile/path arguments.
        model.ClearSelection2(true);
        if (!ext.SelectByID2(spec.ProfileSketchName, "SKETCH", 0.0, 0.0, 0.0, false, 1, null, 0))
        {
            throw new McpToolException(
                $"Cannot select profile sketch '{spec.ProfileSketchName}'. " +
                "Verify the name returned by end_sketch.");
        }
        if (!ext.SelectByID2(spec.PathSketchName, "SKETCH", 0.0, 0.0, 0.0, true, 4, null, 0))
        {
            throw new McpToolException(
                $"Cannot select path sketch '{spec.PathSketchName}'. " +
                "Verify the name returned by end_sketch.");
        }

        // 14-arg InsertProtrusionSwept with educated defaults: follow-path
        // orientation (no twist), no thin wall, merge into the body. M34
        // verified this simple overload works — the M33 CreateDefinition +
        // AccessSelections path RPC-faulted (0x80010105) and was unnecessary.
        var feature = fm.InsertProtrusionSwept(
            Propagate: false,
            Alignment: false,
            TwistCtrlOption: 0,
            KeepTangency: false,
            ForceNonRational: false,
            StartMatchingType: 0,
            EndMatchingType: 0,
            IsThinBody: false,
            Thickness1: 0.0,
            Thickness2: 0.0,
            ThinType: 0,
            Merge: true,
            UseFeatScope: true,
            UseAutoSelect: true);

        if (feature == null)
        {
            throw new McpToolException(
                $"InsertProtrusionSwept returned null for profile '{spec.ProfileSketchName}' " +
                $"along path '{spec.PathSketchName}'. Common causes: the profile is not a " +
                "single closed contour; the path is not a single continuous open curve; or the " +
                "profile plane is not roughly perpendicular to the path at the path's start " +
                "(put the profile on a plane normal to the path's initial direction, and start " +
                "the path at the profile center).");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Swept profile '{spec.ProfileSketchName}' along path " +
                     $"'{spec.PathSketchName}' → feature '{featureName}'",
            path: null);
    }
#endif
}
