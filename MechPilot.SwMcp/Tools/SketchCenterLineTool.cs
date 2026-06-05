using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Sketch a centerline (construction line). Centerlines are used as the
/// axis of revolution for revolve features when embedded in the same
/// sketch as the profile.
/// </summary>
[McpServerToolType]
public static class SketchCenterLineTool
{
    [McpServerTool(Name = "sketch_centerline")]
    [Description(
        "Add a centerline (construction line) from (x1, y1) to (x2, y2) to " +
        "the active sketch. Centerlines are not part of the sketch profile " +
        "but serve as the axis of revolution for the revolve feature when " +
        "embedded in the same sketch. Coordinates in mm. Requires an active sketch.")]
    public static ToolResult Run(
        [Description("Start X in mm.")] double x1,
        [Description("Start Y in mm.")] double y1,
        [Description("End X in mm.")] double x2,
        [Description("End Y in mm.")] double y2)
    {
        return RunWithSpec(new SketchCenterLineSpec { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
    }

    public static ToolResult RunWithSpec(SketchCenterLineSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sketch_centerline failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sketch_centerline requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SketchCenterLineSpec spec)
    {
        var skMgr = Internal.SketchSession.RequireSketchManager();
        _ = Internal.SketchSession.RequireActiveSketch();

        var seg = skMgr.CreateCenterLine(
            spec.X1 / 1000.0, spec.Y1 / 1000.0, 0.0,
            spec.X2 / 1000.0, spec.Y2 / 1000.0, 0.0)
            ?? throw new McpToolException(
                $"CreateCenterLine returned null for ({spec.X1}, {spec.Y1}) → ({spec.X2}, {spec.Y2}) mm.");
        _ = seg;
        return ToolResult.Ok(
            message: $"Added centerline ({spec.X1}, {spec.Y1}) → ({spec.X2}, {spec.Y2}) mm to active sketch",
            path: null);
    }
#endif
}
