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

/// <summary>
/// Sketch a centered axis-aligned rectangle by its center and one corner.
/// Width = 2 * |cornerX - centerX|, height = 2 * |cornerY - centerY|.
/// </summary>
[McpServerToolType]
public static class SketchRectangleCenterTool
{
    [McpServerTool(Name = "sketch_rectangle_center")]
    [Description(
        "Add a centered axis-aligned rectangle to the active sketch, defined " +
        "by its center (cx, cy) and one corner (cornerX, cornerY). The sides " +
        "are parallel to the sketch X / Y axes; the opposite corner is at " +
        "(2*cx - cornerX, 2*cy - cornerY). Width = 2 * |cornerX - cx|, " +
        "height = 2 * |cornerY - cy|. Coordinates in mm. Requires an active sketch.")]
    public static ToolResult Run(
        [Description("Center X in mm.")] double cx,
        [Description("Center Y in mm.")] double cy,
        [Description("Corner X in mm (any of the 4 corners).")] double cornerX,
        [Description("Corner Y in mm.")] double cornerY)
    {
        return RunWithSpec(new SketchRectangleCenterSpec
        {
            Cx = cx,
            Cy = cy,
            CornerX = cornerX,
            CornerY = cornerY,
        });
    }

    public static ToolResult RunWithSpec(SketchRectangleCenterSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sketch_rectangle_center failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sketch_rectangle_center requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SketchRectangleCenterSpec spec)
    {
        var skMgr = Internal.SketchSession.RequireSketchManager();
        var model = Internal.SketchSession.RequireActiveDoc();
        _ = Internal.SketchSession.RequireActiveSketch();

        var segsObj = skMgr.CreateCenterRectangle(
            spec.Cx / 1000.0, spec.Cy / 1000.0, 0.0,
            spec.CornerX / 1000.0, spec.CornerY / 1000.0, 0.0)
            ?? throw new McpToolException(
                $"CreateCenterRectangle returned null for center=({spec.Cx}, {spec.Cy}), " +
                $"corner=({spec.CornerX}, {spec.CornerY}) mm.");
        var width = 2.0 * Math.Abs(spec.CornerX - spec.Cx);
        var height = 2.0 * Math.Abs(spec.CornerY - spec.Cy);

        // Driving width + height dimensions on two adjacent sides so the size
        // is parametric / editable (M46 recipe, shared via SketchDimensioner
        // since M49).
        Internal.SketchDimensioner.AddRectangle(model, segsObj, spec.Cx, spec.Cy, width, height);

        return ToolResult.Ok(
            message: $"Added centered rectangle: center=({spec.Cx}, {spec.Cy}), " +
                     $"size {width} × {height} mm (driving dimensions) to active sketch",
            path: null);
    }

#endif
}
