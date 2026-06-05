using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Create an offset reference plane from an existing plane on the active
/// part. M32 generic-layer tool. Wraps <c>IFeatureManager.InsertRefPlane</c>
/// (6 args, Distance constraint = swRefPlaneReferenceConstraint_Distance = 8,
/// reflected in M28).
///
/// Pipeline:
///   1. RequireActiveDoc.
///   2. ClearSelection2.
///   3. SelectByID2(sourcePlane, "PLANE", mark=0) — try CN/EN aliases for
///      standard planes ("front"/"top"/"right"), else use literal name.
///   4. InsertRefPlane(Distance=8, distance_m, 0, 0, 0, 0).
///
/// Returns the new plane's auto-assigned SW name (typically "基准面1" /
/// "Plane1") in the result message — pass to <c>start_sketch</c> for
/// multi-plane workflows.
/// </summary>
[McpServerToolType]
public static class AddRefPlaneTool
{
    [McpServerTool(Name = "add_ref_plane")]
    [Description(
        "Create an offset reference plane parallel to sourcePlane at the " +
        "given distance (mm). sourcePlane is 'front' / 'top' / 'right' " +
        "(case-insensitive) for SW's default reference planes, or a literal " +
        "plane name (e.g. '基准面1') for an existing RefPlane. distance > 0 " +
        "offsets in the +normal direction; distance < 0 offsets in -normal. " +
        "Use reverse=true to explicitly flip. The new plane is auto-named " +
        "by SW (typically '基准面1' / 'Plane1') — the result message " +
        "includes this name for subsequent start_sketch calls.")]
    public static ToolResult Run(
        [Description("Source plane: 'front' / 'top' / 'right' or a literal SW plane name.")]
        string sourcePlane,
        [Description("Offset distance in mm (signed).")]
        double distance,
        [Description("If true, flip the offset direction. Default false.")]
        bool reverse = false)
    {
        return RunWithSpec(new AddRefPlaneSpec
        {
            SourcePlane = sourcePlane,
            DistanceMm = distance,
            Reverse = reverse,
        });
    }

    public static ToolResult RunWithSpec(AddRefPlaneSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_ref_plane failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("add_ref_plane requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    // swRefPlaneReferenceConstraints_e.Distance = 8 (bitflag, reflected in M28).
    private const int RefPlaneDistanceConstraint = 8;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> StandardPlaneAliases =
        StartSketchSpec.StandardPlaneAliases;

    private static ToolResult RunSw(AddRefPlaneSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;

        // ── 1. Resolve source plane name (standard alias or literal) ────────
        IReadOnlyList<string> candidates =
            StandardPlaneAliases.TryGetValue(spec.SourcePlane, out var aliases)
                ? aliases
                : new[] { spec.SourcePlane };

        model.ClearSelection2(true);
        string? selectedName = null;
        foreach (var name in candidates)
        {
            if (ext.SelectByID2(
                Name: name, Type: "PLANE",
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: false, Mark: 0,
                Callout: null, SelectOption: 0))
            {
                selectedName = name;
                break;
            }
        }
        if (selectedName == null)
        {
            throw new McpToolException(
                $"Cannot select source plane '{spec.SourcePlane}'. " +
                "For standard planes use 'front' / 'top' / 'right'; for an " +
                "existing RefPlane pass its literal name (typically '基准面1').");
        }

        // ── 2. InsertRefPlane with Distance constraint ──────────────────────
        var distanceM = (spec.Reverse ? -spec.DistanceMm : spec.DistanceMm) / 1000.0;
        var refPlane = fm.InsertRefPlane(
            FirstConstraint: RefPlaneDistanceConstraint,
            FirstConstraintAngleOrDistance: distanceM,
            SecondConstraint: 0,
            SecondConstraintAngleOrDistance: 0.0,
            ThirdConstraint: 0,
            ThirdConstraintAngleOrDistance: 0.0);

        if (refPlane == null)
        {
            throw new McpToolException(
                $"InsertRefPlane returned null for source '{selectedName}' " +
                $"distance {spec.DistanceMm} mm.");
        }

        // ── 3. Extract the new plane's name (returned as a Feature) ─────────
        var newPlaneName = (refPlane as IFeature)?.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Created offset plane '{newPlaneName}' (parallel to '{selectedName}', " +
                     $"offset {spec.DistanceMm} mm) — pass to start_sketch",
            path: null);
    }
#endif
}
