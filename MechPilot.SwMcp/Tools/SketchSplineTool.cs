using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Sketch a smooth spline through 3+ points (M50). Unlocks free-form
/// profiles the line/arc primitives can't express — airfoil blade sections,
/// bottle/housing outlines, cam lobes — which then feed extrude / revolve /
/// loft / sweep like any other sketch geometry.
///
/// SW recipe (reflection-verified): <c>ISketchManager.CreateSpline2(
/// PointData, SimulateNaturalEnds)</c> with PointData = flat double[]
/// {x1, y1, z1, x2, y2, z2, ...} in METERS; natural ends = true (relaxed
/// curvature at both ends, the SW UI default).
///
/// No driving dimensions are added — a through-points spline has no single
/// "size" the way a circle has Ø (M46 scope: circle / rectangle only).
/// </summary>
[McpServerToolType]
public static class SketchSplineTool
{
    [McpServerTool(Name = "sketch_spline")]
    [Description(
        "Add a smooth spline through 3 or more points to the active sketch. " +
        "points is a FLAT list of coordinates in mm: [x1, y1, x2, y2, ...] " +
        "(even count, ≥ 6 numbers = 3 points). The curve passes through every " +
        "point in order with natural (relaxed) ends. Use it for free-form " +
        "profiles lines/arcs can't express — airfoil sections, bottle / " +
        "housing outlines, cam lobes — then close the contour (e.g. with " +
        "sketch_line) for extrude/revolve/loft, or leave it open as a sweep " +
        "path. Requires an active sketch (start_sketch first). The spline " +
        "carries no driving dimension (unlike sketch_circle's Ø).")]
    public static ToolResult Run(
        [Description("Flat list [x1, y1, x2, y2, ...] in mm; ≥ 3 points.")]
        double[] points)
    {
        return RunWithSpec(new SketchSplineSpec { Points = points });
    }

    public static ToolResult RunWithSpec(SketchSplineSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sketch_spline failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sketch_spline requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(SketchSplineSpec spec)
    {
        var skMgr = Internal.SketchSession.RequireSketchManager();
        _ = Internal.SketchSession.RequireActiveDoc();
        _ = Internal.SketchSession.RequireActiveSketch();

        // CreateSpline2 wants flat XYZ triplets in meters.
        var pointCount = spec.Points.Count / 2;
        var data = new double[pointCount * 3];
        for (int i = 0; i < pointCount; i++)
        {
            data[i * 3 + 0] = spec.Points[i * 2 + 0] / 1000.0;
            data[i * 3 + 1] = spec.Points[i * 2 + 1] / 1000.0;
            data[i * 3 + 2] = 0.0;
        }

        var seg = skMgr.CreateSpline2(data, true) as ISketchSegment
            ?? throw new McpToolException(
                $"CreateSpline2 returned null for {pointCount} points. The points " +
                "may be collinear duplicates or otherwise degenerate.");
        _ = seg;

        var first = $"({spec.Points[0]}, {spec.Points[1]})";
        var last = $"({spec.Points[^2]}, {spec.Points[^1]})";
        return ToolResult.Ok(
            message: $"Added spline through {pointCount} points {first} → {last} mm to active sketch",
            path: null);
    }
#endif
}
