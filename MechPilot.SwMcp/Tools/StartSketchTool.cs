using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Enter sketch mode on a named plane of the active part. M30 generic
/// primitives layer — start of any sketch-based feature build session.
///
/// Plane can be:
///   • "front" / "top" / "right" (case-insensitive) — SW's default reference planes
///   • Any literal SW plane name (e.g. "Plane1" / "基准面1" for RefPlanes
///     created by add_ref_plane)
///
/// On success, SW enters sketch mode and subsequent sketch primitives add
/// geometry to the active sketch. Call end_sketch to exit and capture the
/// sketch's auto-assigned name (e.g. "草图1") for feature primitives.
/// </summary>
[McpServerToolType]
public static class StartSketchTool
{
    [McpServerTool(Name = "start_sketch")]
    [Description(
        "Enter sketch mode on a named plane of the active part. plane is " +
        "'front' / 'top' / 'right' (case-insensitive) for SW's default " +
        "reference planes, or a literal SW plane name (e.g. 'Plane1' or " +
        "'基准面1') for a custom RefPlane created with add_ref_plane. " +
        "Requires an active part (call new_part first). After this call, " +
        "use sketch_line / sketch_arc_3point / sketch_arc_center / " +
        "sketch_circle / sketch_centerline / sketch_rectangle_center to " +
        "add geometry, then end_sketch to exit and obtain the sketch's " +
        "name for use in extrude / revolve / loft / sweep.")]
    public static ToolResult Run(
        [Description("Plane name: 'front', 'top', 'right', or a literal SW plane name like 'Plane1'.")]
        string plane)
    {
        var spec = new StartSketchSpec { Plane = plane };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(StartSketchSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return StartSketchInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"start_sketch failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "start_sketch requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult StartSketchInSw(StartSketchSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;

        // Reject if already in sketch mode — LLM should end_sketch first.
        if (model.SketchManager.ActiveSketch != null)
        {
            throw new McpToolException(
                "A sketch is already active. Call end_sketch before starting a new one.");
        }

        // Resolve plane name. Standard aliases map to CN/EN pairs (tried in
        // order — CN first since SW UI is configured in 中文); any other
        // string is treated as a literal SW plane name (e.g. user-created
        // RefPlane).
        IReadOnlyList<string> candidates;
        if (StartSketchSpec.StandardPlaneAliases.TryGetValue(spec.Plane, out var aliases))
        {
            candidates = aliases;
        }
        else
        {
            candidates = new[] { spec.Plane };
        }

        model.ClearSelection2(true);
        var selectedName = SelectFirstPlane(ext, candidates);
        if (selectedName == null)
        {
            throw new McpToolException(
                $"Cannot select plane '{spec.Plane}'. Tried: " +
                $"{string.Join(" / ", candidates.Select(c => $"'{c}'"))}. " +
                "For standard planes use 'front' / 'top' / 'right'; for a " +
                "RefPlane created with add_ref_plane, pass its literal name " +
                "(typically 'Plane1' or '基准面1').");
        }

        model.SketchManager.InsertSketch(true);
        if (model.SketchManager.ActiveSketch == null)
        {
            throw new McpToolException(
                $"InsertSketch did not produce an active sketch on '{selectedName}'. " +
                "The plane selection may have been silently lost.");
        }

        return ToolResult.Ok(
            message: $"Entered sketch mode on '{selectedName}'",
            path: null);
    }

    private static string? SelectFirstPlane(IModelDocExtension ext, IReadOnlyList<string> candidates)
    {
        foreach (var name in candidates)
        {
            if (ext.SelectByID2(
                Name: name, Type: "PLANE",
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: false, Mark: 0,
                Callout: null, SelectOption: 0))
            {
                return name;
            }
        }
        return null;
    }
#endif
}
