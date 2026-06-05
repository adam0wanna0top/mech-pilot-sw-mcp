using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Sketch an arc defined by center + start + end + rotation direction.
/// Radius is taken from the center-to-start distance; the end point is
/// snapped to the same radius.
/// </summary>
[McpServerToolType]
public static class SketchArcCenterTool
{
    [McpServerTool(Name = "sketch_arc_center")]
    [Description(
        "Add an arc defined by its center (cx, cy), start point (x1, y1), " +
        "end point (x2, y2), and rotation direction (1=CCW, -1=CW viewed " +
        "from the sketch normal) to the active sketch. Radius is taken from " +
        "the center-to-start distance. NOTE: when the two endpoints lie on " +
        "the same axis through the center, CCW vs. CW is ambiguous — prefer " +
        "sketch_arc_3point in that case. Coordinates in mm. Requires an active sketch.")]
    public static ToolResult Run(
        [Description("Center X in mm.")] double cx,
        [Description("Center Y in mm.")] double cy,
        [Description("Start X in mm.")] double x1,
        [Description("Start Y in mm.")] double y1,
        [Description("End X in mm.")] double x2,
        [Description("End Y in mm.")] double y2,
        [Description("Direction: 1 (CCW) or -1 (CW). Default 1.")] int direction = 1)
    {
        return RunWithSpec(new SketchArcCenterSpec
        {
            Cx = cx,
            Cy = cy,
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Direction = direction,
        });
    }

    public static ToolResult RunWithSpec(SketchArcCenterSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sketch_arc_center failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sketch_arc_center requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SketchArcCenterSpec spec)
    {
        var skMgr = Internal.SketchSession.RequireSketchManager();
        _ = Internal.SketchSession.RequireActiveSketch();

        // SW CreateArc takes Direction as Int16.
        var seg = skMgr.CreateArc(
            XC: spec.Cx / 1000.0, YC: spec.Cy / 1000.0, Zc: 0.0,
            X1: spec.X1 / 1000.0, Y1: spec.Y1 / 1000.0, Z1: 0.0,
            X2: spec.X2 / 1000.0, Y2: spec.Y2 / 1000.0, Z2: 0.0,
            Direction: (short)spec.Direction)
            ?? throw new McpToolException(
                $"CreateArc returned null for center ({spec.Cx}, {spec.Cy}), " +
                $"start ({spec.X1}, {spec.Y1}), end ({spec.X2}, {spec.Y2}), " +
                $"direction={spec.Direction}. The start and end may not be " +
                "equidistant from the center.");
        _ = seg;
        return ToolResult.Ok(
            message: $"Added center arc: center=({spec.Cx}, {spec.Cy}), " +
                     $"start=({spec.X1}, {spec.Y1}), end=({spec.X2}, {spec.Y2}) mm, " +
                     $"direction={(spec.Direction == 1 ? "CCW" : "CW")}",
            path: null);
    }
#endif
}
