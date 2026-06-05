using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Sweep a profile sketch along a path sketch into a solid body. M32
/// generic-layer tool — no parametric helper exists for this since sweep
/// is too varied (pipes / cables / blades / cams) to capture in one spec.
///
/// Wraps SW's <c>InsertProtrusionSwept</c> (14 args, reflected — minimal
/// MVP version; advanced variants v2/v3/v4 can be added later).
///
/// Selection convention (v1 PR #27): profile mark=1, path mark=4.
///
/// Pipeline:
///   1. RequireActiveDoc.
///   2. ClearSelection2.
///   3. SelectByID2(profileSketchName, "SKETCH", mark=1, append=false).
///   4. SelectByID2(pathSketchName, "SKETCH", mark=4, append=true).
///   5. InsertProtrusionSwept(14 args educated defaults).
/// </summary>
[McpServerToolType]
public static class SweepTool
{
    [McpServerTool(Name = "sweep")]
    [Description(
        "Sweep a profile sketch along a path sketch into a solid body on " +
        "the active part. profileSketchName must be a closed-area sketch " +
        "(the cross-section); pathSketchName must be an open-curve sketch " +
        "(the trajectory). Both names from end_sketch. " +
        "Common uses: pipes / cables / fan blades / cams / extrusions along " +
        "curved paths. The profile and path sketches typically sit on " +
        "perpendicular planes (e.g. profile on Front, path on Right) — use " +
        "add_ref_plane if you need custom planes.")]
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

        // ── 1. Select profile (mark=1) + path (mark=4) — v1 PR #27 convention ──
        model.ClearSelection2(true);
        if (!ext.SelectByID2(
            Name: spec.ProfileSketchName, Type: "SKETCH",
            X: 0.0, Y: 0.0, Z: 0.0,
            Append: false, Mark: 1,
            Callout: null, SelectOption: 0))
        {
            throw new McpToolException(
                $"Cannot select profile sketch '{spec.ProfileSketchName}'.");
        }
        if (!ext.SelectByID2(
            Name: spec.PathSketchName, Type: "SKETCH",
            X: 0.0, Y: 0.0, Z: 0.0,
            Append: true, Mark: 4,
            Callout: null, SelectOption: 0))
        {
            throw new McpToolException(
                $"Cannot select path sketch '{spec.PathSketchName}'.");
        }

        // ── 2. InsertProtrusionSwept — 14 args educated defaults ────────────
        var feature = fm.InsertProtrusionSwept(
            Propagate: false,
            Alignment: false,
            TwistCtrlOption: 0,         // swTwistControl_FollowPath (default)
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
                $"path '{spec.PathSketchName}'. Common causes: profile is not closed, path " +
                "is not a single open curve, profile/path are not on intersecting / nearby " +
                "planes, or sketches were selected with wrong marks (profile must be mark=1, path mark=4).");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Swept profile '{spec.ProfileSketchName}' along path " +
                     $"'{spec.PathSketchName}' → feature '{featureName}'",
            path: null);
    }
#endif
}
