using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Sketch an arc through three points: start, end, and one intermediate
/// point on the curve. Prefer over sketch_arc_center when both endpoints
/// lie on the same axis (CCW/CW ambiguity).
/// </summary>
[McpServerToolType]
public static class SketchArc3PointTool
{
    [McpServerTool(Name = "sketch_arc_3point")]
    [Description(
        "Add an arc defined by three points to the active sketch: start " +
        "(x1, y1), end (x2, y2), and one intermediate point on the arc " +
        "(x3, y3). Prefer this over sketch_arc_center when the two endpoints " +
        "might lie on the same axis (which makes the standard CCW/CW " +
        "direction parameter ambiguous — the middle point uniquely defines " +
        "a 180° arc). Coordinates in mm. Requires an active sketch.")]
    public static ToolResult Run(
        [Description("Start X in mm.")] double x1,
        [Description("Start Y in mm.")] double y1,
        [Description("End X in mm.")] double x2,
        [Description("End Y in mm.")] double y2,
        [Description("Middle (on-curve) X in mm.")] double x3,
        [Description("Middle (on-curve) Y in mm.")] double y3)
    {
        return RunWithSpec(new SketchArc3PointSpec
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            X3 = x3,
            Y3 = y3,
        });
    }

    public static ToolResult RunWithSpec(SketchArc3PointSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sketch_arc_3point failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sketch_arc_3point requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SketchArc3PointSpec spec)
    {
        var skMgr = Internal.SketchSession.RequireSketchManager();
        _ = Internal.SketchSession.RequireActiveSketch();

        var seg = skMgr.Create3PointArc(
            X1: spec.X1 / 1000.0, Y1: spec.Y1 / 1000.0, Z1: 0.0,
            X2: spec.X2 / 1000.0, Y2: spec.Y2 / 1000.0, Z2: 0.0,
            X3: spec.X3 / 1000.0, Y3: spec.Y3 / 1000.0, Z3: 0.0)
            ?? throw new McpToolException(
                $"Create3PointArc returned null for ({spec.X1}, {spec.Y1}) → " +
                $"({spec.X2}, {spec.Y2}) via ({spec.X3}, {spec.Y3}) mm.");
        _ = seg;
        return ToolResult.Ok(
            message: $"Added 3-point arc from ({spec.X1}, {spec.Y1}) to ({spec.X2}, {spec.Y2}) " +
                     $"through ({spec.X3}, {spec.Y3}) mm to active sketch",
            path: null);
    }
#endif
}
