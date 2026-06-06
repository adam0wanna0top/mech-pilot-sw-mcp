using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Reads metadata (bbox / feature list / face+edge counts) from the currently
/// ACTIVE part — the one the generic primitives layer is building — WITHOUT
/// saving or closing it. M36.
///
/// Why this exists (M35 E2E finding): inspect_part can only read a saved
/// .sldprt, and save_part closes the active doc. So an LLM building a part
/// step-by-step had no way to verify geometry mid-build — it had to build
/// blind and only check after closing. inspect_active closes that gap: the
/// LLM can confirm a boss extruded in +Z, a cut landed, the bbox is right,
/// etc., and keep building on the same doc.
///
/// Reuses <see cref="Internal.PartMetadata"/> (shared with inspect_part); the
/// only differences are the doc source (active doc, not an opened file) and
/// that this tool leaves the doc open.
/// </summary>
[McpServerToolType]
public static class InspectActiveTool
{
    [McpServerTool(Name = "inspect_active")]
    [Description(
        "Inspect the part you are CURRENTLY building (the active document) " +
        "without saving or closing it. Returns the same metadata as " +
        "inspect_part — title, top-level feature count + list, total face / " +
        "edge count, and a bounding box in mm — read live from the active " +
        "part. Use it mid-build to verify geometry before continuing: e.g. " +
        "confirm a boss extruded in the +Z direction (check bbox), that a cut " +
        "removed material (face count rose), or how many bodies exist. " +
        "Requires an active part (call new_part first). Takes no arguments.")]
    public static ToolResult Run()
    {
        return RunWithSpec(new InspectActiveSpec());
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(InspectActiveSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            var model = Internal.SketchSession.RequireActiveDoc();
            // Note: no open, no close — the caller keeps building on this doc.
            return Internal.PartMetadata.Build(model);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"inspect_active failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "inspect_active requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }
}
