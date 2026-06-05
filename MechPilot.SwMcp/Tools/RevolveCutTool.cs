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
/// Cut a body by revolving a named sketch around its embedded centerline.
/// M33 generic-layer counterpart to <see cref="RevolveTool"/> — same input
/// spec (<see cref="RevolveSpec"/>), but calls <c>FeatureRevolve2</c> with
/// <c>IsCut=true</c> to remove material instead of adding it.
///
/// Common LLM use: turn a groove, cut a shaped hollow, machine a profile
/// into an existing axis-symmetric body.
/// </summary>
[McpServerToolType]
public static class RevolveCutTool
{
    [McpServerTool(Name = "revolve_cut")]
    [Description(
        "Revolve-cut: revolve a named sketch around its embedded centerline " +
        "and SUBTRACT the resulting volume from the active body. sketchName " +
        "is from end_sketch and must contain a profile + a centerline. angle " +
        "is the sweep in degrees (default 360). Generic-layer counterpart to " +
        "revolve — same spec, uses FeatureRevolve2 with IsCut=true. Common " +
        "uses: turn a groove, cut a shaped hollow, machine a profile into an " +
        "existing axis-symmetric body. The active part MUST already have a body.")]
    public static ToolResult Run(
        [Description("Name of the sketch to revolve-cut (from end_sketch, must contain centerline).")]
        string sketchName,
        [Description("Revolve angle in degrees. (0, 360], default 360.")]
        double angle = 360.0,
        [Description("If true, flip revolve direction. Default false.")]
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
                $"revolve_cut failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("revolve_cut requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(RevolveSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;

        // ── 1. Select the named sketch (mark=0; SW auto-binds centerline as axis) ──
        model.ClearSelection2(true);
        if (!ext.SelectByID2(
            Name: spec.SketchName, Type: "SKETCH",
            X: 0.0, Y: 0.0, Z: 0.0,
            Append: false, Mark: 0,
            Callout: null, SelectOption: 0))
        {
            throw new McpToolException(
                $"Cannot select sketch '{spec.SketchName}' on the active part.");
        }

        // ── 2. FeatureRevolve2 with IsCut=true — same 20 args as M23 ───────
        var angleRad = spec.AngleDeg * Math.PI / 180.0;
        var feature = fm.FeatureRevolve2(
            SingleDir: true,
            IsSolid: true,
            IsThin: false,
            IsCut: true,                            // ← the only delta vs RevolveTool
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
                $"FeatureRevolve2 (IsCut=true) returned null for sketch " +
                $"'{spec.SketchName}' angle {spec.AngleDeg}°. Common causes: " +
                "no body to cut from, or the cut doesn't intersect any body.");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Revolve-cut '{spec.SketchName}' by {spec.AngleDeg}° → feature '{featureName}'",
            path: null);
    }
#endif
}
