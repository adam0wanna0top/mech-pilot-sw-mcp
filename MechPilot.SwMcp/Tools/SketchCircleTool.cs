using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Interop;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>Sketch a circle by center and radius.</summary>
[McpServerToolType]
public static class SketchCircleTool
{
    [McpServerTool(Name = "sketch_circle")]
    [Description(
        "Add a circle centered at (cx, cy) with the given radius to the " +
        "active sketch. Coordinates and radius are in mm in the sketch plane. " +
        "Requires an active sketch (call start_sketch first). Radius must be > 0.")]
    public static ToolResult Run(
        [Description("Center X coordinate in mm.")] double cx,
        [Description("Center Y coordinate in mm.")] double cy,
        [Description("Radius in mm (must be > 0).")] double radius)
    {
        return RunWithSpec(new SketchCircleSpec { Cx = cx, Cy = cy, RadiusMm = radius });
    }

    public static ToolResult RunWithSpec(SketchCircleSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sketch_circle failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sketch_circle requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SketchCircleSpec spec)
    {
        var skMgr = Internal.SketchSession.RequireSketchManager();
        var model = Internal.SketchSession.RequireActiveDoc();
        _ = Internal.SketchSession.RequireActiveSketch();

        var seg = skMgr.CreateCircleByRadius(
            spec.Cx / 1000.0, spec.Cy / 1000.0, 0.0,
            spec.RadiusMm / 1000.0) as ISketchSegment
            ?? throw new McpToolException(
                $"CreateCircleByRadius returned null for center ({spec.Cx}, {spec.Cy}) " +
                $"radius {spec.RadiusMm} mm.");

        // Add a DRIVING diameter dimension so the size is parametric / editable
        // (modify_feature can then change it). Select the circle, place the dim
        // annotation offset from it, and set Diametric so it reads as Ø not radius.
        // swInputDimValOnCreate must be OFF or AddDimension2 pops a modal "Modify"
        // dialog that blocks the API call (M46 finding).
        SwConnection.Instance.GetApp().SetUserPreferenceToggle(
            (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
        model.ClearSelection2(true);
        seg.Select2(false, 0);
        var placeX = (spec.Cx + spec.RadiusMm) / 1000.0 + 0.010;
        object dispObj = model.AddDimension2(placeX, spec.Cy / 1000.0, 0.0);
        if (dispObj is IDisplayDimension disp)
        {
            disp.Diametric = true;
        }

        return ToolResult.Ok(
            message: $"Added circle center=({spec.Cx}, {spec.Cy}) Ø{2 * spec.RadiusMm} mm (driving dimension) to active sketch",
            path: null);
    }
#endif
}
