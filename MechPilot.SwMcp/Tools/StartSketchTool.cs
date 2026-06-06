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
        "Enter sketch mode on a plane OR a body face of the active part. plane is: " +
        "'front' / 'top' / 'right' (case-insensitive) for SW's default reference " +
        "planes; a literal SW plane name (e.g. 'Plane1' / '基准面1') for a " +
        "RefPlane from add_ref_plane; OR a FACE selector '+z' / '-z' / '+x' / " +
        "'-x' / '+y' / '-y' to sketch directly on the outermost planar body face " +
        "whose outward normal points that way (e.g. '+z' = the current top face). " +
        "The face option lets you build on top of / under / beside the body " +
        "without first creating a ref plane at that height — prefer it when " +
        "adding a feature onto an existing face. Requires an active part (call " +
        "new_part first; the face option also needs an existing solid body). " +
        "After this call, use sketch_line / sketch_arc_3point / sketch_arc_center " +
        "/ sketch_circle / sketch_centerline / sketch_rectangle_center to add " +
        "geometry, then end_sketch to exit and obtain the sketch's name for use " +
        "in extrude / revolve / loft / sweep.")]
    public static ToolResult Run(
        [Description("'front'/'top'/'right', a literal plane name like 'Plane1', or a face selector '+z'/'-z'/'+x'/'-x'/'+y'/'-y'.")]
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

        // Face selector: "+x"/"-x"/"+y"/"-y"/"+z"/"-z" → sketch on the EXTREME
        // planar body face whose outward normal points that way (M37). Lets the
        // LLM sketch on "the top face" without first computing its height for a
        // ref plane. Distinct tokens from the plane aliases, so no collision.
        if (TryParseFaceSelector(spec.Plane, out var axis, out var sign))
        {
            model.ClearSelection2(true);
            var face = Internal.PartGeometryHelpers.FindExtremePlanarFace(model, axis, sign)
                ?? throw new McpToolException(
                    $"No planar face found facing '{spec.Plane}'. The active part may have no " +
                    "solid body yet, or no planar face whose outward normal points that way. " +
                    "Build a body first, or use a reference plane ('front'/'top'/'right' or add_ref_plane).");
            if (!((IEntity)face).Select4(false, null))
            {
                throw new McpToolException(
                    $"Failed to select the '{spec.Plane}' face for sketching.");
            }
            model.SketchManager.InsertSketch(true);
            if (model.SketchManager.ActiveSketch == null)
            {
                throw new McpToolException(
                    $"InsertSketch did not produce an active sketch on the '{spec.Plane}' face.");
            }
            return ToolResult.Ok(
                message: $"Entered sketch mode on the '{spec.Plane}' face (extreme planar face facing {spec.Plane})",
                path: null);
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

    /// <summary>
    /// Parses a face selector like "+z" / "-x" (case-insensitive) into an
    /// (axis 0=X / 1=Y / 2=Z, sign +1 / -1) pair. Returns false for anything
    /// that isn't exactly a sign char followed by x/y/z, so plane names and
    /// RefPlane names fall through to the plane-resolution path.
    /// </summary>
    private static bool TryParseFaceSelector(string s, out int axis, out int sign)
    {
        axis = 0;
        sign = 0;
        if (s is not { Length: 2 })
        {
            return false;
        }
        sign = s[0] switch { '+' => 1, '-' => -1, _ => 0 };
        if (sign == 0)
        {
            return false;
        }
        axis = char.ToLowerInvariant(s[1]) switch { 'x' => 0, 'y' => 1, 'z' => 2, _ => -1 };
        return axis >= 0;
    }
#endif
}
