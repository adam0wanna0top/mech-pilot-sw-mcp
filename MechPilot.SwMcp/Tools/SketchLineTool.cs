using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;

namespace MechPilot.SwMcp.Tools;

/// <summary>Sketch a straight line segment between two points.</summary>
[McpServerToolType]
public static class SketchLineTool
{
    [McpServerTool(Name = "sketch_line")]
    [Description(
        "Add a line segment from (x1, y1) to (x2, y2) to the active sketch. " +
        "Coordinates are in mm in the sketch plane. Requires an active sketch " +
        "(call start_sketch first). The two endpoints must be distinct (zero-length lines are rejected).")]
    public static ToolResult Run(
        [Description("Start X coordinate in mm.")] double x1,
        [Description("Start Y coordinate in mm.")] double y1,
        [Description("End X coordinate in mm.")] double x2,
        [Description("End Y coordinate in mm.")] double y2)
    {
        return RunWithSpec(new SketchLineSpec { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
    }

    public static ToolResult RunWithSpec(SketchLineSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sketch_line failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sketch_line requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SketchLineSpec spec)
    {
        var skMgr = Internal.SketchSession.RequireSketchManager();
        _ = Internal.SketchSession.RequireActiveSketch();

        var seg = skMgr.CreateLine(
            spec.X1 / 1000.0, spec.Y1 / 1000.0, 0.0,
            spec.X2 / 1000.0, spec.Y2 / 1000.0, 0.0)
            ?? throw new McpToolException(
                $"CreateLine returned null for ({spec.X1}, {spec.Y1}) → ({spec.X2}, {spec.Y2}) mm. " +
                "Check the active sketch is on a valid plane.");
        _ = seg;
        return ToolResult.Ok(
            message: $"Added line ({spec.X1}, {spec.Y1}) → ({spec.X2}, {spec.Y2}) mm to active sketch",
            path: null);
    }
#endif
}
